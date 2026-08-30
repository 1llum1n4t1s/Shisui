using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.UI.ViewModels;

namespace Shisui.Tests;

[TestClass]
public sealed class DnsSettingsViewModelTests
{
    private static readonly NetworkAdapterInfo Adapter =
        new("Ethernet", "Ethernet", null, true, [], []);

    [TestMethod]
    public async Task ApplyCommand_InvalidCustomAddress_DoesNotInvokeDnsService()
    {
        var dnsService = new FakeDnsConfigurationService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(dnsService, gate);
        viewModel.SelectedPreset = DnsPresetCatalog.Custom;
        viewModel.CustomIpv4Primary = "1.1.1.1\" index=2";

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dnsService.ApplyCallCount);
        StringAssert.Contains(viewModel.StatusText, "正しい形式");
    }

    [TestMethod]
    public async Task ResetToAutomaticCommand_FailedCommand_ShowsFailure()
    {
        var dnsService = new FakeDnsConfigurationService
        {
            ResetResults = [new(false, "netsh reset", 1, string.Empty, "failed")],
        };
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(dnsService, gate);

        await viewModel.ResetToAutomaticCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "戻せませんでした");
    }

    [TestMethod]
    public async Task QuickOptimization_AdapterCleanupThrows_ReportsPartialFailureAndSavesSettings()
    {
        var dnsService = new FakeDnsConfigurationService();
        var settingsService = new FakeSettingsService();
        using var gate = new NetworkMutationGate();
        var dnsViewModel = CreateViewModel(
            dnsService,
            gate,
            settingsService,
            new ThrowingAdapterNameService());
        var commandResults = new List<CommandExecutionResult>();
        dnsViewModel.CommandExecuted += (_, result) => commandResults.Add(result);
        var optimizationViewModel = new AutoOptimizationViewModel(dnsViewModel, gate);

        await optimizationViewModel.RunQuickOptimizationCommand.ExecuteAsync(null);

        Assert.IsTrue(settingsService.SaveCalled, "先に完了した設定を保存するべきです。");
        Assert.IsTrue(commandResults.Any(result => result.CommandLine == "接続名の整理" && !result.Success));
        StringAssert.Contains(dnsViewModel.StatusText, "一部の設定が失敗");
        StringAssert.Contains(dnsViewModel.AdapterNameStatusText, "接続名の整理に失敗");
    }

    private static DnsSettingsViewModel CreateViewModel(
        FakeDnsConfigurationService dnsService,
        NetworkMutationGate gate,
        FakeSettingsService? settingsService = null,
        INetworkAdapterNameService? adapterNameService = null) =>
        new(
            new FakeNetworkAdapterService(),
            dnsService,
            new FakeDnsCacheService(),
            settingsService ?? new FakeSettingsService(),
            new FakeNetworkDiagnosticsService(),
            gate,
            adapterNameService: adapterNameService);

    private sealed class FakeNetworkAdapterService : INetworkAdapterService
    {
        public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>([Adapter]);

        public Task<NetworkAdapterDetails?> GetAdapterDetailsAsync(string adapterId, CancellationToken ct = default) =>
            Task.FromResult<NetworkAdapterDetails?>(null);
    }

    private sealed class FakeDnsConfigurationService : IDnsConfigurationService
    {
        public int ApplyCallCount { get; private set; }

        public IReadOnlyList<CommandExecutionResult> ResetResults { get; init; } =
            [new(true, "netsh reset", 0, string.Empty, string.Empty)];

        public Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(
            string adapterId,
            DnsServerSet servers,
            CancellationToken ct = default)
        {
            ApplyCallCount++;
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>(
                [new(true, "netsh apply", 0, string.Empty, string.Empty)]);
        }

        public Task<IReadOnlyList<CommandExecutionResult>> ResetToAutomaticAsync(
            string adapterId,
            CancellationToken ct = default) => Task.FromResult(ResetResults);
    }

    private sealed class FakeDnsCacheService : IDnsCacheService
    {
        public Task<CommandExecutionResult> FlushAsync(CancellationToken ct = default) =>
            Task.FromResult(new CommandExecutionResult(true, "flush", 0, string.Empty, string.Empty));
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool SaveCalled { get; private set; }

        public Task SaveAsync(CancellationToken ct = default)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNetworkDiagnosticsService : INetworkDiagnosticsService
    {
        public Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default) =>
            Task.FromResult(PingResult.Failed(host, string.Empty));

        public Task<TraceRouteResult> TraceRouteAsync(string host, int maxHops, CancellationToken ct = default) =>
            Task.FromResult(new TraceRouteResult(false, host, [], string.Empty));
    }

    private sealed class ThrowingAdapterNameService : INetworkAdapterNameService
    {
        public Task<NetworkAdapterNameCleanupResult> CleanupAsync(
            string? currentName,
            CancellationToken ct = default) =>
            Task.FromException<NetworkAdapterNameCleanupResult>(new InvalidOperationException("cleanup failed"));
    }
}
