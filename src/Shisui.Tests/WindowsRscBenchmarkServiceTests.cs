using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsRscBenchmarkServiceTests
{
    [TestMethod]
    public async Task RunAsync_MeasuresBothStates_AndRestoresOriginalState()
    {
        var tcp = new FakeTcpTuningService(originalRscEnabled: true);
        var loadedPing = new FakeLoadedPingMeasurementService(
            new LoadedPingMeasurementResult(true, 10, 8, 12, 1, null),
            new LoadedPingMeasurementResult(true, 13, 11, 15, 1, null));
        var service = new WindowsRscBenchmarkService(tcp, loadedPing, new NetworkMutationGate());

        var results = await service.RunAsync(testSizeBytes: 5_000_000);

        Assert.HasCount(2, results);
        Assert.IsTrue(results[0].Enabled);
        Assert.AreEqual(10, results[0].AveragePingMs);
        Assert.IsFalse(results[1].Enabled);
        Assert.AreEqual(13, results[1].AveragePingMs);
        CollectionAssert.AreEqual(new[] { 3, 3 }, loadedPing.SampleCounts);
        CollectionAssert.AreEqual(new[] { true, false, true }, tcp.RscSetCalls);
    }

    [TestMethod]
    public async Task RunAsync_WhenMeasurementIsCanceled_RestoresOriginalState()
    {
        var tcp = new FakeTcpTuningService(originalRscEnabled: false);
        var service = new WindowsRscBenchmarkService(
            tcp, new CancelingLoadedPingMeasurementService(), new NetworkMutationGate());

        try
        {
            await service.RunAsync(testSizeBytes: 5_000_000);
            Assert.Fail("OperationCanceledException が必要です");
        }
        catch (OperationCanceledException)
        {
        }

        CollectionAssert.AreEqual(new[] { true, false }, tcp.RscSetCalls);
    }

    [TestMethod]
    public async Task RunAsync_WhenRestoreFails_ReportsFailure()
    {
        var tcp = new FakeTcpTuningService(originalRscEnabled: true, failOnSetCallNumber: 3);
        var loadedPing = new FakeLoadedPingMeasurementService(
            new LoadedPingMeasurementResult(true, 10, 8, 12, 1, null),
            new LoadedPingMeasurementResult(true, 13, 11, 15, 1, null));
        var service = new WindowsRscBenchmarkService(tcp, loadedPing, new NetworkMutationGate());

        try
        {
            await service.RunAsync(testSizeBytes: 5_000_000);
            Assert.Fail("InvalidOperationException が必要です");
        }
        catch (InvalidOperationException ex)
        {
            StringAssert.Contains(ex.Message, "復元に失敗");
        }

        CollectionAssert.AreEqual(new[] { true, false, true }, tcp.RscSetCalls);
    }

    private sealed class FakeLoadedPingMeasurementService(params LoadedPingMeasurementResult[] results)
        : ILoadedPingMeasurementService
    {
        private int index;
        public List<int> SampleCounts { get; } = [];

        public Task<LoadedPingMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            SampleCounts.Add(sampleCount);
            progress?.Report(0);
            return Task.FromResult(results[index++]);
        }
    }

    private sealed class CancelingLoadedPingMeasurementService : ILoadedPingMeasurementService
    {
        public Task<LoadedPingMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default) =>
            Task.FromCanceled<LoadedPingMeasurementResult>(new CancellationToken(canceled: true));
    }

    private sealed class FakeTcpTuningService(bool originalRscEnabled, int? failOnSetCallNumber = null) : ITcpTuningService
    {
        public List<bool> RscSetCalls { get; } = [];

        public Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new TcpSettingsSnapshot(
                Bbr2Status.Disabled,
                new Dictionary<TcpGlobalOption, string>
                {
                    [TcpGlobalOption.Rsc] = originalRscEnabled ? "Enabled" : "Disabled",
                }));

        public Task<CommandExecutionResult> SetTcpGlobalOptionAsync(
            TcpGlobalOption option, bool enabled, CancellationToken ct = default)
        {
            Assert.AreEqual(TcpGlobalOption.Rsc, option);
            RscSetCalls.Add(enabled);
            var success = RscSetCalls.Count != failOnSetCallNumber;
            return Task.FromResult(new CommandExecutionResult(
                success,
                "netsh",
                success ? 0 : 1,
                string.Empty,
                success ? string.Empty : "失敗"));
        }

        public Task<CommandExecutionResult> RevertTcpGlobalOptionToDefaultAsync(TcpGlobalOption option, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> SetCongestionProvidersAsync(IReadOnlyDictionary<string, string> providers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> RevertGlobalOptionsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> RevertMtuToDefaultAsync(string adapterId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
