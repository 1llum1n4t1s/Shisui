using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルへ実際に切り替え、Hetzner のテストファイルの
/// ダウンロード速度を比較する。
/// </summary>
/// <remarks>
/// auto-tuning は TCP の受信ウィンドウに作用するため、ICMP Ping ではなく TCP の受信速度を測る。
/// 各サンプルでは新しい TCP 接続でダウンロードを開始する。
/// ウィンドウスケールは接続確立時に決まるため、レベルごとに新しい <see cref="HttpClient"/> を生成する。
///
/// ダウンロード先は Hetzner の公開テストファイルを使用し、5 リージョンをレベルごとに同じ順序で回す。
/// 1 レベルにつき複数回計測して平均・最小・最大速度を残し、HTTP が失敗したサンプルだけを除外する。
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoTuningBenchmarkService(
    ITcpTuningService tcpTuningService,
    IDownloadSpeedMeasurementService downloadSpeedMeasurementService,
    INetworkMutationGate networkMutationGate) : IAutoTuningBenchmarkService
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
        using var mutationLease = await networkMutationGate.EnterAsync(ct);
        var originalState = await tcpTuningService.GetCurrentStateAsync(ct);
        if (!Enum.TryParse<AutoTuningLevel>(originalState.AutoTuningLevel, ignoreCase: true, out var originalLevel))
        {
            throw new InvalidOperationException(
                "Auto-Tuning の現在値を取得できないため、安全に計測できませんでした");
        }

        var totalSteps = LevelsToTest.Count * SamplesPerLevel;
        var results = new List<AutoTuningBenchmarkResult>(LevelsToTest.Count);
        try
        {
            for (var i = 0; i < LevelsToTest.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var level = LevelsToTest[i];

                var setResult = await tcpTuningService.SetAutoTuningLevelAsync(level, ct);
                if (!setResult.Success)
                {
                    var error = string.IsNullOrWhiteSpace(setResult.StandardError)
                        ? $"Auto-Tuning を {level} へ変更できませんでした"
                        : setResult.StandardError.Trim();
                    results.Add(new AutoTuningBenchmarkResult(level, false, null, null, null, 0, error));
                    continue;
                }

                await Task.Delay(500, ct);

                results.Add(await MeasureLevelAsync(level, testSizeBytes, SamplesPerLevel, i * SamplesPerLevel, totalSteps, progress, ct));
            }
        }
        finally
        {
            // 完了・キャンセル・例外のどの場合も、呼び出し元のキャンセル状態に関係なく開始前の設定へ戻す。
            var restoreResult = await tcpTuningService.SetAutoTuningLevelAsync(originalLevel, CancellationToken.None);
            if (!restoreResult.Success)
            {
                throw new InvalidOperationException(
                    "Auto-Tuning の開始前状態への復元に失敗しました。実行ログを確認してください");
            }
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
        var measurement = await downloadSpeedMeasurementService.MeasureAsync(
            testSizeBytes, samplesPerLevel, levelProgress, ct);

        return new AutoTuningBenchmarkResult(
            level,
            measurement.Success,
            measurement.AverageMbps,
            measurement.MinMbps,
            measurement.MaxMbps,
            measurement.SampleCount,
            measurement.ErrorMessage);
    }
}
