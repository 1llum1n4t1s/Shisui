using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルへ実際に切り替え、Hetzner のテストファイルを受信している最中の
/// Cloudflare Ping を測る。速度ではなく、TCP 受信負荷がある状態のレイテンシを比較する。
/// </summary>
/// <remarks>
/// auto-tuning は TCP の受信ウィンドウに作用するため、無負荷の ICMP Ping だけを測っても設定差は反映されない。
/// そこで各サンプルでは新しい TCP 接続でダウンロードを開始し、データを受信している間に Ping を送る。
/// ウィンドウスケールは接続確立時に決まるため、レベルごとに新しい <see cref="HttpClient"/> を生成する。
///
/// ダウンロード先は Hetzner の公開テストファイルを使用し、5 リージョンをレベルごとに同じ順序で回す。
/// Ping 先は全サンプル共通の <c>1.1.1.1</c> とし、ダウンロード先の地理的距離を Ping 値へ直接混ぜず、
/// 主にローカル回線の負荷によって増えた待ち時間を比較する。1 レベルにつき複数回計測して平均・最小・最大を残し、
/// HTTP または Ping が失敗したサンプルだけを除外する。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoTuningBenchmarkService(
    ITcpTuningService tcpTuningService,
    ILoadedPingMeasurementService loadedPingMeasurementService) : IAutoTuningBenchmarkService
{
    private const int SamplesPerLevel = 5;

    private static readonly IReadOnlyList<AutoTuningLevel> LevelsToTest =
    [
        AutoTuningLevel.Disabled,
        AutoTuningLevel.HighlyRestricted,
        AutoTuningLevel.Restricted,
        AutoTuningLevel.Normal,
        AutoTuningLevel.Experimental,
    ];

    public async Task<IReadOnlyList<AutoTuningBenchmarkResult>> RunAsync(
        int testSizeBytes, IProgress<AutoTuningBenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        var originalState = await tcpTuningService.GetCurrentStateAsync(ct);
        var originalLevel = Enum.TryParse<AutoTuningLevel>(originalState.AutoTuningLevel, ignoreCase: true, out var parsed)
            ? parsed
            : AutoTuningLevel.Normal;

        var totalSteps = LevelsToTest.Count * SamplesPerLevel;
        var results = new List<AutoTuningBenchmarkResult>(LevelsToTest.Count);
        try
        {
            for (var i = 0; i < LevelsToTest.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var level = LevelsToTest[i];

                await tcpTuningService.SetAutoTuningLevelAsync(level, ct);
                await Task.Delay(500, ct);

                results.Add(await MeasureLevelAsync(level, testSizeBytes, SamplesPerLevel, i * SamplesPerLevel, totalSteps, progress, ct));
            }
        }
        finally
        {
            // 完了・キャンセル・例外のどの場合も、呼び出し元のキャンセル状態に関係なく開始前の設定へ戻す。
            await tcpTuningService.SetAutoTuningLevelAsync(originalLevel, CancellationToken.None);
        }

        return results;
    }

    private async Task<AutoTuningBenchmarkResult> MeasureLevelAsync(
        AutoTuningLevel level, int testSizeBytes, int samplesPerLevel, int stepOffset, int totalSteps,
        IProgress<AutoTuningBenchmarkProgress>? progress, CancellationToken ct)
    {
        var levelProgress = progress is null
            ? null
            : new InlineProgress<int>(sampleIndex =>
                progress.Report(new AutoTuningBenchmarkProgress(level, stepOffset + sampleIndex, totalSteps)));
        var measurement = await loadedPingMeasurementService.MeasureAsync(
            testSizeBytes, samplesPerLevel, levelProgress, ct);

        return new AutoTuningBenchmarkResult(
            level,
            measurement.Success,
            measurement.AveragePingMs,
            measurement.MinPingMs,
            measurement.MaxPingMs,
            measurement.SampleCount,
            measurement.ErrorMessage);
    }
}
