using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Core.Services.Windows;

/// <summary>5テンプレートだけをBBR2/既定へ切り替え、負荷時Pingを比較する。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBbr2BenchmarkService(
    ITcpTuningService tcpTuningService,
    ILoadedPingMeasurementService loadedPingMeasurementService,
    INetworkMutationGate networkMutationGate) : IBbr2BenchmarkService
{
    private static readonly bool[] StatesToTest = [true, false];

    public async Task<IReadOnlyList<Bbr2BenchmarkResult>> RunAsync(
        int testSizeBytes,
        IProgress<Bbr2BenchmarkProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var mutationLease = await networkMutationGate.EnterAsync(ct);
        var snapshot = await tcpTuningService.GetCurrentStateAsync(ct);
        var originalProviders = snapshot.GetCongestionProviders();
        if (originalProviders.Count != WindowsTcpCommandBuilder.SupplementalTemplates.Count ||
            WindowsTcpCommandBuilder.SupplementalTemplates.Any(template => !originalProviders.ContainsKey(template)))
        {
            throw new InvalidOperationException("BBR2 の開始前設定を正確に復元できないため、安全に計測できませんでした");
        }

        var results = new List<Bbr2BenchmarkResult>(2);
        try
        {
            for (var stateIndex = 0; stateIndex < StatesToTest.Length; stateIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var enabled = StatesToTest[stateIndex];
                var providers = WindowsTcpCommandBuilder.SupplementalTemplates.ToDictionary(
                    template => template,
                    _ => enabled ? "BBR2" : "default",
                    StringComparer.OrdinalIgnoreCase);
                var setResults = await tcpTuningService.SetCongestionProvidersAsync(providers, ct);
                if (setResults.Any(result => !result.Success))
                {
                    results.Add(new Bbr2BenchmarkResult(enabled, false, null, null, null, 0, "輻輳制御の設定変更に失敗しました"));
                    continue;
                }

                await Task.Delay(WindowsBenchmarkDownloadCatalog.SettingSettleDelayMs, ct);
                var offset = stateIndex * WindowsBenchmarkDownloadCatalog.SamplesPerCandidate;
                var stateProgress = progress is null ? null : new InlineProgress<int>(sampleIndex =>
                    progress.Report(new Bbr2BenchmarkProgress(
                        enabled,
                        offset + sampleIndex,
                        StatesToTest.Length * WindowsBenchmarkDownloadCatalog.SamplesPerCandidate)));
                var measurement = await loadedPingMeasurementService.MeasureAsync(
                    testSizeBytes,
                    WindowsBenchmarkDownloadCatalog.SamplesPerCandidate,
                    stateProgress,
                    ct);
                results.Add(new Bbr2BenchmarkResult(enabled, measurement.Success,
                    measurement.AveragePingMs, measurement.MinPingMs, measurement.MaxPingMs,
                    measurement.SampleCount, measurement.ErrorMessage));
            }
        }
        finally
        {
            var restoreResults = await tcpTuningService.SetCongestionProvidersAsync(originalProviders, CancellationToken.None);
            if (restoreResults.Any(result => !result.Success))
            {
                throw new InvalidOperationException("BBR2 の開始前状態への復元に失敗しました。実行ログを確認してください");
            }
        }

        return results;
    }
}
