using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>RSC 有効・無効を切り替え、同条件のTCP受信負荷中Pingで比較する。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRscBenchmarkService(
    ITcpTuningService tcpTuningService,
    ILoadedPingMeasurementService loadedPingMeasurementService) : IRscBenchmarkService
{
    private const int SettingSettleDelayMs = 500;
    private static readonly bool[] StatesToTest = [true, false];

    public async Task<IReadOnlyList<RscBenchmarkResult>> RunAsync(
        int testSizeBytes, int samplesPerState = 5, IProgress<RscBenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        samplesPerState = Math.Max(1, samplesPerState);

        var originalSnapshot = await tcpTuningService.GetCurrentStateAsync(ct);
        var originalRawValue = originalSnapshot.GetOptionValue(TcpGlobalOption.Rsc);
        var originalEnabled = originalRawValue.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
            ? true
            : originalRawValue.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                ? false
                : throw new InvalidOperationException("RSC の現在値を取得できないため、安全に計測できませんでした");

        var totalSteps = StatesToTest.Length * samplesPerState;
        var results = new List<RscBenchmarkResult>(StatesToTest.Length);
        try
        {
            for (var stateIndex = 0; stateIndex < StatesToTest.Length; stateIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var enabled = StatesToTest[stateIndex];
                var setResult = await tcpTuningService.SetTcpGlobalOptionAsync(TcpGlobalOption.Rsc, enabled, ct);
                if (!setResult.Success)
                {
                    var error = string.IsNullOrWhiteSpace(setResult.StandardError)
                        ? "RSC の設定変更に失敗しました"
                        : setResult.StandardError.Trim();
                    results.Add(new RscBenchmarkResult(enabled, false, null, null, null, 0, error));
                    continue;
                }

                await Task.Delay(SettingSettleDelayMs, ct);

                var stepOffset = stateIndex * samplesPerState;
                var stateProgress = progress is null
                    ? null
                    : new InlineProgress<int>(sampleIndex =>
                        progress.Report(new RscBenchmarkProgress(enabled, stepOffset + sampleIndex, totalSteps)));
                var measurement = await loadedPingMeasurementService.MeasureAsync(
                    testSizeBytes, samplesPerState, stateProgress, ct);

                results.Add(new RscBenchmarkResult(
                    enabled,
                    measurement.Success,
                    measurement.AveragePingMs,
                    measurement.MinPingMs,
                    measurement.MaxPingMs,
                    measurement.SampleCount,
                    measurement.ErrorMessage));
            }
        }
        finally
        {
            // キャンセル済みでも開始前の実効状態へ戻す。復元失敗は安全上、成功扱いにしない。
            var restoreResult = await tcpTuningService.SetTcpGlobalOptionAsync(
                TcpGlobalOption.Rsc, originalEnabled, CancellationToken.None);
            if (!restoreResult.Success)
            {
                throw new InvalidOperationException("RSC の開始前状態への復元に失敗しました。実行ログを確認してください");
            }
        }

        return results;
    }
}
