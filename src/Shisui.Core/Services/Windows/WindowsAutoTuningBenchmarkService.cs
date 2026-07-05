using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルへ実際に切り替えながら、Hetzner が公開しているスピードテスト用
/// ファイルから交互にダウンロードして実測スループットを比較する。
/// </summary>
/// <remarks>
/// auto-tuning level を netsh で切り替えても、既に確立済みの TCP 接続の受信ウィンドウスケールは
/// 変わらない (ウィンドウスケールは接続確立時の 3-way handshake でのみ決まる)。そのためレベルごとに
/// 新しい <see cref="HttpClient"/> を使い、必ず新規コネクションを張ってから計測する
/// (使い回すと直前のレベルのウィンドウスケールのまま計測してしまい、比較にならない)。
///
/// **計測先には当初 Cloudflare の <c>speed.cloudflare.com/__down?bytes=N</c> を使っていたが、2026-07-06
/// 実機で2つの制約に連続で当たったため撤去した**: (1) <c>bytes</c> が 100,000,000 (100MB) 以上だと即座に
/// 403 Forbidden (curl で 99,999,999→200、100,000,000→403 の境界を確認、複数回再現する安定した挙動)。
/// (2) 短時間の大量リクエストは 429 Too Many Requests でレート制限される (テストサイズ80MB・5回×5レベル
/// =25リクエスト/約2GBの集中で発生、`Retry-After: 2811` すなわち約47分のクールダウンが提示された。1MB
/// 程度の小さいリクエストは制限中でも通ったため、リクエスト数ベースというより帯域/データ量ベースの制限と
/// 見られる)。ゆろ君から「レート制限の影響がない別の計測先(Google 等)を使えないか」という指摘を受け、
/// Google 公式のスピードテスト用ダウンロード API は調査したが見当たらなかった (Google Cloud Storage の
/// 公開サンプルファイルも試したが 403、`generate_204` は疎通確認専用で 0 バイトのため使えない)。
/// 代わりに **Hetzner (Cloudflare とは無関係の別会社・別インフラの大手ホスティング事業者) が公式に
/// 「Test Files」として複数リージョンで公開している固定サイズファイル** (<c>https://&lt;region&gt;-speed.hetzner.com/100MB.bin</c>)
/// を HTTP Range リクエストで部分ダウンロードする形に全面的に切り替えた。単発の「スピードテスト専用ツール」
/// (Cloudflare の <c>__down</c> のような) は人間が時々使う想定でレート制限が厳しめな一方、こちらは
/// 素の静的ファイル配信 (レスポンスヘッダーの <c>Server: nginx</c> の通り) であり、2026-07-06 時点では
/// 403/429 いずれも未確認。
///
/// 5 リージョン (fsn1/nbg1/hil/sin/ash) を <see cref="Targets"/> としてサンプルごとに順番に使う。
/// **ローテーションはレベル内のサンプル番号 (0始まり) を基準にする**(グローバルな通し番号ではない):
/// これにより、どのレベルも「1本目は fsn1、2本目は nbg1…」と同じ並びで計測されるため、計測先ごとの
/// 速度差 (地理的距離等) がレベル間比較を歪めない。既定の計測先数(5) = 既定の計測回数上限(5) なので、
/// 通常の利用では同じ計測先が複数回使われることもない。
///
/// 単発計測は瞬間的な回線状況のブレをそのまま拾ってしまう (2026-07-06、ゆろ君の実機で「結果にバラつきが
/// ある」との指摘あり)。そのため 1 レベルにつき既定 5 回計測して平均を取り、参考として最小/最大も残す。
/// サンプル間にも <see cref="InterSampleDelayMs"/> の間隔を空け、バーストにならないようにする。万一 429
/// 等が返っても致命的エラーにはせず、通常の失敗サンプルと同様に扱う (該当サンプルを除いた回数で平均を取る、
/// 計測先を分散済みなので1箇所が一時的に不調でも他の計測先で継続できる)。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoTuningBenchmarkService(ITcpTuningService tcpTuningService) : IAutoTuningBenchmarkService
{
    /// <summary>Hetzner のテストファイルは 100MiB (104,857,600 バイト) なので、それに対して
    /// 十分なマージンを取った安全な上限。</summary>
    private const int MaxSafeTestSizeBytes = 90_000_000;

    private const int InterSampleDelayMs = 300;

    private static readonly IReadOnlyList<DownloadTarget> Targets =
    [
        new("Hetzner fsn1", bytes => CreateRangeRequest("https://fsn1-speed.hetzner.com/100MB.bin", bytes)),
        new("Hetzner nbg1", bytes => CreateRangeRequest("https://nbg1-speed.hetzner.com/100MB.bin", bytes)),
        new("Hetzner hil", bytes => CreateRangeRequest("https://hil-speed.hetzner.com/100MB.bin", bytes)),
        new("Hetzner sin", bytes => CreateRangeRequest("https://sin-speed.hetzner.com/100MB.bin", bytes)),
        new("Hetzner ash", bytes => CreateRangeRequest("https://ash-speed.hetzner.com/100MB.bin", bytes)),
    ];

    private static readonly IReadOnlyList<AutoTuningLevel> LevelsToTest =
    [
        AutoTuningLevel.Disabled,
        AutoTuningLevel.HighlyRestricted,
        AutoTuningLevel.Restricted,
        AutoTuningLevel.Normal,
        AutoTuningLevel.Experimental,
    ];

    public async Task<IReadOnlyList<AutoTuningBenchmarkResult>> RunAsync(
        int testSizeBytes, int samplesPerLevel = 5, IProgress<AutoTuningBenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        testSizeBytes = Math.Min(testSizeBytes, MaxSafeTestSizeBytes);
        samplesPerLevel = Math.Max(1, samplesPerLevel);

        var originalState = await tcpTuningService.GetCurrentStateAsync(ct);
        var originalLevel = Enum.TryParse<AutoTuningLevel>(originalState.AutoTuningLevel, ignoreCase: true, out var parsed)
            ? parsed
            : AutoTuningLevel.Normal;

        var totalSteps = LevelsToTest.Count * samplesPerLevel;
        var results = new List<AutoTuningBenchmarkResult>(LevelsToTest.Count);
        try
        {
            for (var i = 0; i < LevelsToTest.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var level = LevelsToTest[i];

                await tcpTuningService.SetAutoTuningLevelAsync(level, ct);
                await Task.Delay(500, ct);

                results.Add(await MeasureLevelAsync(level, testSizeBytes, samplesPerLevel, i * samplesPerLevel, totalSteps, progress, ct));
            }
        }
        finally
        {
            // 計測完了・キャンセル・例外いずれの場合も、計測開始前のレベル (取得できなければ既定の
            // Normal) に戻す。ここは呼び出し元のキャンセルとは無関係に必ず実行したいので CancellationToken.None。
            await tcpTuningService.SetAutoTuningLevelAsync(originalLevel, CancellationToken.None);
        }

        return results;
    }

    private static async Task<AutoTuningBenchmarkResult> MeasureLevelAsync(
        AutoTuningLevel level, int testSizeBytes, int samplesPerLevel, int stepOffset, int totalSteps,
        IProgress<AutoTuningBenchmarkProgress>? progress, CancellationToken ct)
    {
        var throughputs = new List<double>(samplesPerLevel);
        string? lastError = null;

        for (var i = 0; i < samplesPerLevel; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new AutoTuningBenchmarkProgress(level, stepOffset + i, totalSteps));

            if (i > 0)
            {
                await Task.Delay(InterSampleDelayMs, ct);
            }

            // サンプル番号 (レベル内での通し番号) で計測先を固定する。こうすることで、どのレベルも
            // 同じ並びの計測先で計測されるため、計測先ごとの速度差がレベル間比較を歪めない。
            var target = Targets[i % Targets.Count];
            var sample = await MeasureOnceAsync(target, testSizeBytes, ct);
            if (sample.Success && sample.ThroughputMbps is { } mbps)
            {
                throughputs.Add(mbps);
            }
            else
            {
                lastError = sample.ErrorMessage;
            }
        }

        var summary = WindowsAutoTuningBenchmarkMath.Summarize(throughputs);
        return summary is { } s
            ? new AutoTuningBenchmarkResult(level, true, s.Average, s.Min, s.Max, throughputs.Count, null)
            : new AutoTuningBenchmarkResult(level, false, null, null, null, 0, lastError);
    }

    private static async Task<SingleMeasurement> MeasureOnceAsync(DownloadTarget target, int testSizeBytes, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = target.CreateRequest(testSizeBytes);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new SingleMeasurement(false, null, $"[{target.Name}] {FormatHttpError(response)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += read;
            }

            stopwatch.Stop();
            return new SingleMeasurement(true, WindowsAutoTuningBenchmarkMath.ComputeThroughputMbps(totalRead, stopwatch.Elapsed), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient 自身の Timeout 発火 (ユーザーによるキャンセルではない) → このサンプルの失敗として次へ続ける。
            return new SingleMeasurement(false, null, $"[{target.Name}] タイムアウトしました");
        }
        catch (HttpRequestException ex)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] {ex.Message}");
        }
    }

    private static string FormatHttpError(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        var suffix = retryAfter is { } wait
            ? wait.TotalMinutes >= 1 ? $"、約{Math.Ceiling(wait.TotalMinutes)}分後に再試行可能" : $"、約{Math.Ceiling(wait.TotalSeconds)}秒後に再試行可能"
            : string.Empty;
        return $"HTTP 429 (Too Many Requests){suffix}";
    }

    private static HttpRequestMessage CreateRangeRequest(string url, int bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, bytes - 1);
        return request;
    }

    private readonly record struct SingleMeasurement(bool Success, double? ThroughputMbps, string? ErrorMessage);

    private sealed record DownloadTarget(string Name, Func<int, HttpRequestMessage> CreateRequest);
}
