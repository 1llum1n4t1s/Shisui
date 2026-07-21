using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsTcpOptionBenchmarkServiceTests
{
    [TestMethod]
    public async Task RunAsync_RssUsesSpeedAndRestoresOriginalState()
    {
        var tcp = new FakeTcp("Enabled");
        var speed = new FakeSpeed();
        var service = new WindowsTcpOptionBenchmarkService(tcp, new UnusedPing(), speed, new NetworkMutationGate());

        var results = await service.RunAsync(TcpGlobalOption.Rss, TcpOptionBenchmarkMetric.DownloadSpeed, 5_000_000);

        Assert.HasCount(2, results);
        Assert.AreEqual(100d, results[0].AverageValue);
        Assert.AreEqual(80d, results[1].AverageValue);
        CollectionAssert.AreEqual(new[] { true, false, true }, tcp.SetCalls);
        CollectionAssert.AreEqual(new[] { 5, 5 }, speed.SampleCounts);
    }

    [TestMethod]
    public async Task RunAsync_TimestampsAllowed_RefusesUnsafeMeasurement()
    {
        var tcp = new FakeTcp("Allowed");
        var service = new WindowsTcpOptionBenchmarkService(tcp, new UnusedPing(), new FakeSpeed(), new NetworkMutationGate());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.RunAsync(TcpGlobalOption.Timestamps, TcpOptionBenchmarkMetric.LoadedPing, 5_000_000));

        Assert.IsEmpty(tcp.SetCalls);
    }

    private sealed class FakeTcp(string original) : TcpTuningServiceTestStub
    {
        public List<bool> SetCalls { get; } = [];
        public override Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new TcpSettingsSnapshot(Bbr2Status.Disabled,
                new Dictionary<TcpGlobalOption, string>
                {
                    [TcpGlobalOption.Rss] = original,
                    [TcpGlobalOption.Timestamps] = original,
                }));
        public override Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default)
        {
            SetCalls.Add(enabled);
            return Task.FromResult(Success());
        }
    }

    private sealed class FakeSpeed : IDownloadSpeedMeasurementService
    {
        public List<int> SampleCounts { get; } = [];
        public Task<DownloadSpeedMeasurementResult> MeasureAsync(int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            SampleCounts.Add(sampleCount);
            var average = SampleCounts.Count == 1 ? 100d : 80d;
            return Task.FromResult(new DownloadSpeedMeasurementResult(true, average, average - 5, average + 5, sampleCount, null));
        }
    }

    private sealed class UnusedPing : ILoadedPingMeasurementService
    {
        public Task<LoadedPingMeasurementResult> MeasureAsync(int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default) =>
            throw new AssertFailedException("速度計測では呼ばれません");
    }
}
