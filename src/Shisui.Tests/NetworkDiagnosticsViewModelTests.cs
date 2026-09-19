using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.UI.ViewModels;

namespace Shisui.Tests;

[TestClass]
public sealed class NetworkDiagnosticsViewModelTests
{
    [TestMethod]
    public async Task Ping_UsesSelectedCountAndShowsLossAndTailLatency()
    {
        var service = new RecordingDiagnostics();
        var vm = new NetworkDiagnosticsViewModel(service) { Host = "127.0.0.1", PingCount = 100 };
        await vm.PingCommand.ExecuteAsync(null);
        Assert.AreEqual(100, service.Count);
        StringAssert.Contains(vm.PingResultText, "損失率");
        StringAssert.Contains(vm.PingResultText, "p95");
        StringAssert.Contains(vm.PingResultText, "ジッター");
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task Ping_InvalidCountDoesNotCallService()
    {
        var service = new RecordingDiagnostics();
        var vm = new NetworkDiagnosticsViewModel(service) { Host = "127.0.0.1", PingCount = 1000 };
        await vm.PingCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.Count);
    }

    [TestMethod]
    public async Task Ping_CancelPropagatesAndClearsBusy()
    {
        var service = new RecordingDiagnostics { WaitForCancellation = true };
        var vm = new NetworkDiagnosticsViewModel(service) { Host = "127.0.0.1" };
        var running = vm.PingCommand.ExecuteAsync(null);
        vm.PingCancelCommand.Execute(null);
        await running;
        StringAssert.Contains(vm.StatusText, "中止");
        Assert.IsFalse(vm.IsBusy);
    }

    private sealed class RecordingDiagnostics : INetworkDiagnosticsService
    {
        public int Count { get; private set; }
        public bool WaitForCancellation { get; init; }
        public async Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default)
        {
            Count = count;
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new(true, host, count, count - 1, 10, "")
            {
                MinimumRoundtripMs = 5, MaximumRoundtripMs = 20,
                P95RoundtripMs = 15, JitterMs = 3,
            };
        }
        public Task<TraceRouteResult> TraceRouteAsync(string host, int maxHops, CancellationToken ct = default) =>
            Task.FromResult(new TraceRouteResult(false, host, [], ""));
    }
}
