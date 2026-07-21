using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// Hetzner の公開テストファイルを受信している最中に Cloudflare Ping を測り、TCP設定比較で共用する。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLoadedPingMeasurementService : ILoadedPingMeasurementService
{
    private const int InterSampleDelayMs = 300;
    private const string PingTarget = "1.1.1.1";
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MeasurementTimeout = TimeSpan.FromSeconds(30);

    public async Task<LoadedPingMeasurementResult> MeasureAsync(
        int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        testSizeBytes = Math.Clamp(testSizeBytes, 1, WindowsBenchmarkDownloadCatalog.MaxSafeTestSizeBytes);
        sampleCount = Math.Max(1, sampleCount);

        var pingMilliseconds = new List<double>(sampleCount);
        string? lastError = null;

        // 呼び出し1回が1つのTCP構成に対応する。構成切り替え後の新しい接続になるよう毎回生成する。
        using var client = new HttpClient();

        for (var i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(i);

            if (i > 0)
            {
                await Task.Delay(InterSampleDelayMs, ct);
            }

            // 各構成で同じサンプル番号に同じリージョンを割り当て、地理的な条件差を揃える。
            var target = WindowsBenchmarkDownloadCatalog.Targets[i % WindowsBenchmarkDownloadCatalog.Targets.Count];
            var sample = await MeasureOnceAsync(client, target, testSizeBytes, ct);
            if (sample.Success && sample.PingMilliseconds is { } pingMs)
            {
                pingMilliseconds.Add(pingMs);
            }
            else
            {
                lastError = sample.ErrorMessage;
            }
        }

        var summary = WindowsAutoTuningBenchmarkMath.SummarizePingMilliseconds(pingMilliseconds);
        return summary is { } s
            ? new LoadedPingMeasurementResult(true, s.Average, s.Min, s.Max, pingMilliseconds.Count, null)
            : new LoadedPingMeasurementResult(false, null, null, null, 0, lastError);
    }

    private static async Task<SingleMeasurement> MeasureOnceAsync(
        HttpClient client, WindowsBenchmarkDownloadCatalog.DownloadTarget target, int testSizeBytes, CancellationToken ct)
    {
        using var measurementCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        measurementCts.CancelAfter(MeasurementTimeout);
        var measurementCt = measurementCts.Token;

        try
        {
            using var request = WindowsBenchmarkDownloadCatalog.CreateRangeRequest(target, testSizeBytes);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, measurementCt);
            if (!response.IsSuccessStatusCode)
            {
                return new SingleMeasurement(
                    false, null, $"[{target.Name}] {WindowsBenchmarkDownloadCatalog.FormatHttpError(response)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(measurementCt);
            var buffer = new byte[81920];

            // 最初のデータ到着を確認し、残りの受信とPingを並行させることで確実に負荷中を測る。
            if (await stream.ReadAsync(buffer, measurementCt) == 0)
            {
                return new SingleMeasurement(false, null, $"[{target.Name}] ダウンロードデータを受信できませんでした");
            }

            using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(measurementCt);
            var loadTask = DrainAsync(stream, buffer, loadCts.Token);
            PingReply reply;
            try
            {
                using var ping = new Ping();
                reply = await ping.SendPingAsync(PingTarget, PingTimeout, cancellationToken: measurementCt);
            }
            finally
            {
                loadCts.Cancel();
                try
                {
                    await loadTask;
                }
                catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
                {
                }
            }

            return reply.Status == IPStatus.Success
                ? new SingleMeasurement(true, reply.RoundtripTime, null)
                : new SingleMeasurement(false, null, $"[{target.Name}] Ping {reply.Status}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] タイムアウトしました");
        }
        catch (PingException ex)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] Ping: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] {ex.Message}");
        }
    }

    private static async Task DrainAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        while (await stream.ReadAsync(buffer, ct) > 0)
        {
        }
    }

    private readonly record struct SingleMeasurement(bool Success, double? PingMilliseconds, string? ErrorMessage);
}
