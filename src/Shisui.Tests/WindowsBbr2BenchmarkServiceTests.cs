using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsBbr2BenchmarkServiceTests
{
    [TestMethod]
    public async Task RunAsync_MeasuresBothStates_AndRestoresExactProviders()
    {
        var original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Internet"] = "CUBIC",
            ["InternetCustom"] = "BBR2",
            ["Datacenter"] = "DCTCP",
            ["DatacenterCustom"] = "CUBIC",
            ["Compat"] = "NewReno",
        };
        var tcp = new FakeTcp(original);
        var ping = new FakePing();
        var service = new WindowsBbr2BenchmarkService(tcp, ping, new NetworkMutationGate());

        var results = await service.RunAsync(5_000_000);

        Assert.HasCount(2, results);
        Assert.HasCount(3, tcp.ProviderCalls);
        Assert.IsTrue(tcp.ProviderCalls[0].Values.All(v => v == "BBR2"));
        Assert.IsTrue(tcp.ProviderCalls[1].Values.All(v => v == "default"));
        CollectionAssert.AreEquivalent(original.ToList(), tcp.ProviderCalls[2].ToList());
        CollectionAssert.AreEqual(new[] { 3, 3 }, ping.SampleCounts);
    }

    private sealed class FakeTcp(IReadOnlyDictionary<string, string> original) : TcpTuningServiceTestStub
    {
        public List<IReadOnlyDictionary<string, string>> ProviderCalls { get; } = [];
        public override Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new TcpSettingsSnapshot(Bbr2Status.Partial,
                new Dictionary<TcpGlobalOption, string>(), "", original));
        public override Task<IReadOnlyList<CommandExecutionResult>> SetCongestionProvidersAsync(IReadOnlyDictionary<string, string> providers, CancellationToken ct = default)
        {
            ProviderCalls.Add(new Dictionary<string, string>(providers, StringComparer.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([Success(), Success(), Success(), Success(), Success()]);
        }
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
}
