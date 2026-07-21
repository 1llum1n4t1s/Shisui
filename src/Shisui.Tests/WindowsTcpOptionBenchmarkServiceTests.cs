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
        CollectionAssert.AreEqual(new[] { 3, 3 }, speed.SampleCounts);
    }

    [TestMethod]
    public async Task RunAsync_TimestampsAllowed_MeasuresAndRestoresWindowsDefault()
    {
        var tcp = new FakeTcp("Allowed");
        var ping = new FakePing();
        var service = new WindowsTcpOptionBenchmarkService(tcp, ping, new FakeSpeed(), new NetworkMutationGate());

        var results = await service.RunAsync(
            TcpGlobalOption.Timestamps,
            TcpOptionBenchmarkMetric.LoadedPing,
            5_000_000);

        Assert.HasCount(2, results);
        CollectionAssert.AreEqual(new[] { true, false }, tcp.SetCalls);
        CollectionAssert.AreEqual(new[] { TcpGlobalOption.Timestamps }, tcp.RevertToDefaultCalls);
        CollectionAssert.AreEqual(new[] { 3, 3 }, ping.SampleCounts);
    }

    [TestMethod]
    public async Task RunAsync_TimestampsAllowed_WhenMeasurementIsCanceled_RestoresWindowsDefault()
    {
        var tcp = new FakeTcp("Allowed");
        var service = new WindowsTcpOptionBenchmarkService(
            tcp,
            new CancelingPing(),
            new FakeSpeed(),
            new NetworkMutationGate());

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            service.RunAsync(
                TcpGlobalOption.Timestamps,
                TcpOptionBenchmarkMetric.LoadedPing,
                5_000_000));

        CollectionAssert.AreEqual(new[] { true }, tcp.SetCalls);
        CollectionAssert.AreEqual(new[] { TcpGlobalOption.Timestamps }, tcp.RevertToDefaultCalls);
    }

    private sealed class FakeTcp(string original) : TcpTuningServiceTestStub
    {
        public List<bool> SetCalls { get; } = [];
        public List<TcpGlobalOption> RevertToDefaultCalls { get; } = [];
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

        public override Task<CommandExecutionResult> RevertTcpGlobalOptionToDefaultAsync(
            TcpGlobalOption option, CancellationToken ct = default)
        {
            RevertToDefaultCalls.Add(option);
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

    private sealed class FakePing : ILoadedPingMeasurementService
    {
        public List<int> SampleCounts { get; } = [];

        public Task<LoadedPingMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            SampleCounts.Add(sampleCount);
            return Task.FromResult(new LoadedPingMeasurementResult(true, 12, 10, 14, sampleCount, null));
        }
    }

    private sealed class CancelingPing : ILoadedPingMeasurementService
    {
        public Task<LoadedPingMeasurementResult> MeasureAsync(
            int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default) =>
            Task.FromCanceled<LoadedPingMeasurementResult>(new CancellationToken(canceled: true));
    }
}
