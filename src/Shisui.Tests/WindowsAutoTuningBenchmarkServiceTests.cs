using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsAutoTuningBenchmarkServiceTests
{
    [TestMethod]
    public async Task RunAsync_UsesSharedLoadedPingMeasurement_AndRestoresOriginalLevel()
    {
        var tcp = new FakeTcpTuningService();
        var loadedPing = new FakeLoadedPingMeasurementService();
        var service = new WindowsAutoTuningBenchmarkService(tcp, loadedPing);

        var results = await service.RunAsync(testSizeBytes: 5_000_000);

        Assert.HasCount(5, results);
        Assert.AreEqual(5, loadedPing.CallCount);
        CollectionAssert.AreEqual(new[] { 5, 5, 5, 5, 5 }, loadedPing.SampleCounts);
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

    private sealed class FakeLoadedPingMeasurementService : ILoadedPingMeasurementService
    {
        public int CallCount { get; private set; }
        public List<int> SampleCounts { get; } = [];

        public Task<LoadedPingMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            CallCount++;
            SampleCounts.Add(sampleCount);
            progress?.Report(0);
            return Task.FromResult(new LoadedPingMeasurementResult(true, 10, 9, 11, sampleCount, null));
        }
    }

    private sealed class FakeTcpTuningService : ITcpTuningService
    {
        public List<AutoTuningLevel> AutoTuningSetCalls { get; } = [];

        public Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new TcpSettingsSnapshot(Bbr2Status.Disabled, new Dictionary<TcpGlobalOption, string>(), "Normal"));

        public Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default)
        {
            AutoTuningSetCalls.Add(level);
            return Task.FromResult(new CommandExecutionResult(true, "netsh", 0, string.Empty, string.Empty));
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
