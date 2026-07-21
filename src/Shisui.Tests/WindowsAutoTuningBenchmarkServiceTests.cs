using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsAutoTuningBenchmarkServiceTests
{
    [TestMethod]
    public async Task RunAsync_UsesDownloadSpeedMeasurement_AndRestoresOriginalLevel()
    {
        var tcp = new FakeTcpTuningService();
        var downloadSpeed = new FakeDownloadSpeedMeasurementService();
        var service = new WindowsAutoTuningBenchmarkService(tcp, downloadSpeed, new NetworkMutationGate());

        var results = await service.RunAsync(testSizeBytes: 5_000_000);

        Assert.HasCount(5, results);
        Assert.AreEqual(5, downloadSpeed.CallCount);
        CollectionAssert.AreEqual(new[] { 5, 5, 5, 5, 5 }, downloadSpeed.SampleCounts);
        CollectionAssert.AreEqual(new[]
        {
            AutoTuningLevel.Disabled,
            AutoTuningLevel.HighlyRestricted,
            AutoTuningLevel.Restricted,
            AutoTuningLevel.Normal,
            AutoTuningLevel.Experimental,
            AutoTuningLevel.Normal,
        }, tcp.AutoTuningSetCalls);
    }

    [TestMethod]
    public async Task RunAsync_WhenCurrentLevelIsUnknown_DoesNotChangeOrMeasure()
    {
        var tcp = new FakeTcpTuningService(currentLevel: "Unknown");
        var downloadSpeed = new FakeDownloadSpeedMeasurementService();
        var service = new WindowsAutoTuningBenchmarkService(tcp, downloadSpeed, new NetworkMutationGate());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RunAsync(testSizeBytes: 5_000_000));

        Assert.IsEmpty(tcp.AutoTuningSetCalls);
        Assert.AreEqual(0, downloadSpeed.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_WhenLevelChangeFails_SkipsMeasurementAndReportsFailure()
    {
        var tcp = new FakeTcpTuningService(failOnSetCallNumber: 2);
        var downloadSpeed = new FakeDownloadSpeedMeasurementService();
        var service = new WindowsAutoTuningBenchmarkService(tcp, downloadSpeed, new NetworkMutationGate());

        var results = await service.RunAsync(testSizeBytes: 5_000_000);

        Assert.HasCount(5, results);
        Assert.IsFalse(results[1].Success);
        StringAssert.Contains(results[1].ErrorMessage, "失敗");
        Assert.AreEqual(4, downloadSpeed.CallCount);
        CollectionAssert.AreEqual(new[]
        {
            AutoTuningLevel.Disabled,
            AutoTuningLevel.HighlyRestricted,
            AutoTuningLevel.Restricted,
            AutoTuningLevel.Normal,
            AutoTuningLevel.Experimental,
            AutoTuningLevel.Normal,
        }, tcp.AutoTuningSetCalls);
    }

    [TestMethod]
    public async Task RunAsync_WhenRestoreFails_ReportsFailure()
    {
        var tcp = new FakeTcpTuningService(failOnSetCallNumber: 6);
        var service = new WindowsAutoTuningBenchmarkService(
            tcp, new FakeDownloadSpeedMeasurementService(), new NetworkMutationGate());

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RunAsync(testSizeBytes: 5_000_000));

        StringAssert.Contains(ex.Message, "復元に失敗");
    }

    private sealed class FakeDownloadSpeedMeasurementService : IDownloadSpeedMeasurementService
    {
        public int CallCount { get; private set; }
        public List<int> SampleCounts { get; } = [];

        public Task<DownloadSpeedMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            CallCount++;
            SampleCounts.Add(sampleCount);
            progress?.Report(0);
            return Task.FromResult(new DownloadSpeedMeasurementResult(true, 500, 450, 550, sampleCount, null));
        }
    }

    private sealed class FakeTcpTuningService(
        string currentLevel = "Normal", int? failOnSetCallNumber = null) : ITcpTuningService
    {
        public List<AutoTuningLevel> AutoTuningSetCalls { get; } = [];

        public Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new TcpSettingsSnapshot(Bbr2Status.Disabled, new Dictionary<TcpGlobalOption, string>(), currentLevel));

        public Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default)
        {
            AutoTuningSetCalls.Add(level);
            var success = AutoTuningSetCalls.Count != failOnSetCallNumber;
            return Task.FromResult(new CommandExecutionResult(
                success,
                "netsh",
                success ? 0 : 1,
                string.Empty,
                success ? string.Empty : "失敗"));
        }

        public Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> RevertGlobalOptionsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommandExecutionResult>> SetMtuAsync(string adapterId, int mtu, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
