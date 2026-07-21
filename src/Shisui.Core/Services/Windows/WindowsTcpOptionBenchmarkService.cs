using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Core.Services.Windows;

/// <summary>ECN・RSS・TCPタイムスタンプを固定3回ずつ有効/無効で比較する。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTcpOptionBenchmarkService(
    ITcpTuningService tcpTuningService,
    ILoadedPingMeasurementService loadedPingMeasurementService,
    IDownloadSpeedMeasurementService downloadSpeedMeasurementService,
    INetworkMutationGate networkMutationGate) : ITcpOptionBenchmarkService
{
    private static readonly bool[] StatesToTest = [true, false];

    public async Task<IReadOnlyList<TcpOptionBenchmarkResult>> RunAsync(
        TcpGlobalOption option,
        TcpOptionBenchmarkMetric metric,
        int testSizeBytes,
        IProgress<TcpOptionBenchmarkProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (option is not (TcpGlobalOption.EcnCapability or TcpGlobalOption.Rss or TcpGlobalOption.Timestamps))
        {
            throw new ArgumentOutOfRangeException(nameof(option), "このA/B計測ではECN・RSS・TCPタイムスタンプだけを扱います");
        }

        using var mutationLease = await networkMutationGate.EnterAsync(ct);
        var originalSnapshot = await tcpTuningService.GetCurrentStateAsync(ct);
        var originalRawValue = originalSnapshot.GetOptionValue(option);
        var restoreToWindowsDefault = option == TcpGlobalOption.Timestamps &&
            originalRawValue.Equals("Allowed", StringComparison.OrdinalIgnoreCase);
        var originalEnabled = originalRawValue.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
            ? (bool?)true
            : originalRawValue.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        if (originalEnabled is null && !restoreToWindowsDefault)
        {
            throw new InvalidOperationException(
                $"{GetDisplayName(option)} の現在値を正確に復元できないため、安全に計測できませんでした");
        }

        var totalSteps = StatesToTest.Length * WindowsBenchmarkDownloadCatalog.SamplesPerCandidate;
        var results = new List<TcpOptionBenchmarkResult>(StatesToTest.Length);
        try
        {
            for (var stateIndex = 0; stateIndex < StatesToTest.Length; stateIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var enabled = StatesToTest[stateIndex];
                var setResult = await tcpTuningService.SetTcpGlobalOptionAsync(option, enabled, ct);
                if (!setResult.Success)
                {
                    results.Add(new TcpOptionBenchmarkResult(option, enabled, metric, false, null, null, null, 0,
                        string.IsNullOrWhiteSpace(setResult.StandardError) ? "設定変更に失敗しました" : setResult.StandardError.Trim()));
                    continue;
                }

                await Task.Delay(WindowsBenchmarkDownloadCatalog.SettingSettleDelayMs, ct);
                var offset = stateIndex * WindowsBenchmarkDownloadCatalog.SamplesPerCandidate;
                var stateProgress = progress is null ? null : new InlineProgress<int>(sampleIndex =>
                    progress.Report(new TcpOptionBenchmarkProgress(option, enabled, offset + sampleIndex, totalSteps)));

                if (metric == TcpOptionBenchmarkMetric.DownloadSpeed)
                {
                    var measurement = await downloadSpeedMeasurementService.MeasureAsync(
                        testSizeBytes,
                        WindowsBenchmarkDownloadCatalog.SamplesPerCandidate,
                        stateProgress,
                        ct);
                    results.Add(new TcpOptionBenchmarkResult(option, enabled, metric, measurement.Success,
                        measurement.AverageMbps, measurement.MinMbps, measurement.MaxMbps,
                        measurement.SampleCount, measurement.ErrorMessage));
                }
                else
                {
                    var measurement = await loadedPingMeasurementService.MeasureAsync(
                        testSizeBytes,
                        WindowsBenchmarkDownloadCatalog.SamplesPerCandidate,
                        stateProgress,
                        ct);
                    results.Add(new TcpOptionBenchmarkResult(option, enabled, metric, measurement.Success,
                        measurement.AveragePingMs, measurement.MinPingMs, measurement.MaxPingMs,
                        measurement.SampleCount, measurement.ErrorMessage));
                }
            }
        }
        finally
        {
            var restoreResult = restoreToWindowsDefault
                ? await tcpTuningService.RevertTcpGlobalOptionToDefaultAsync(option, CancellationToken.None)
                : await tcpTuningService.SetTcpGlobalOptionAsync(
                    option,
                    originalEnabled!.Value,
                    CancellationToken.None);
            if (!restoreResult.Success)
            {
                throw new InvalidOperationException($"{GetDisplayName(option)} の開始前状態への復元に失敗しました。実行ログを確認してください");
            }
        }

        return results;
    }

    private static string GetDisplayName(TcpGlobalOption option) => option switch
    {
        TcpGlobalOption.EcnCapability => "ECN",
        TcpGlobalOption.Rss => "RSS",
        TcpGlobalOption.Timestamps => "TCPタイムスタンプ",
        _ => option.ToString(),
    };
}
