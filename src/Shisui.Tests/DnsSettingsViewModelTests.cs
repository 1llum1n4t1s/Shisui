using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;
using Shisui.UI.ViewModels;

namespace Shisui.Tests;

[TestClass]
public sealed class DnsSettingsViewModelTests
{
    private static readonly NetworkAdapterInfo Adapter =
        new("Ethernet", "Ethernet", null, true, [], []);
    private static readonly NetworkAdapterInfo Adapter2 =
        new("Wi-Fi", "Wi-Fi", null, true, [], []);

    [TestMethod]
    public async Task GamingProfile_UsesSelectedAdapterAndReportsFailureWithoutQuickReset()
    {
        var dnsService = new FakeDnsConfigurationService();
        using var gate = new NetworkMutationGate();
        var dns = CreateViewModel(dnsService, gate);
        dns.SelectedAdapter = Adapter;
        var profile = new FakeGamingProfileService();
        var vm = new AutoOptimizationViewModel(dns, gate, gamingNetworkProfileService: profile);
        var results = new List<CommandExecutionResult>();
        vm.CommandExecuted += (_, result) => results.Add(result);

        await vm.ApplyGamingProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(Adapter.Id, profile.AppliedAdapter);
        Assert.AreEqual(0, dnsService.ApplyCallCount);
        Assert.AreEqual(1, results.Count);
        StringAssert.Contains(vm.GamingProfileStatusText, "未適用または失敗");
        Assert.IsFalse(vm.IsOperationRunning);

        await vm.RestoreGamingProfileCommand.ExecuteAsync(null);
        Assert.AreEqual(Adapter.Id, profile.RestoredAdapter);
        StringAssert.Contains(vm.GamingProfileStatusText, "復元確認");
    }

    [TestMethod]
    public async Task GamingProfile_ServiceExceptionAlwaysClearsBusy()
    {
        using var gate = new NetworkMutationGate();
        var dns = CreateViewModel(new FakeDnsConfigurationService(), gate);
        dns.SelectedAdapter = Adapter;
        var vm = new AutoOptimizationViewModel(dns, gate,
            gamingNetworkProfileService: new FakeGamingProfileService { Throw = true });
        await vm.ApplyGamingProfileCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsOperationRunning);
        StringAssert.Contains(vm.GamingProfileStatusText, "完了できませんでした");
    }

    [TestMethod]
    public async Task GamingProfile_WifiSelectionIsPassedToServiceAndNamedInResult()
    {
        using var gate = new NetworkMutationGate();
        var dns = CreateViewModel(new FakeDnsConfigurationService(), gate);
        dns.SelectedAdapter = Adapter2;
        var profile = new FakeGamingProfileService();
        var vm = new AutoOptimizationViewModel(dns, gate, gamingNetworkProfileService: profile);
        await vm.ApplyGamingProfileCommand.ExecuteAsync(null);
        Assert.AreEqual("Wi-Fi", profile.AppliedAdapter);
        StringAssert.Contains(vm.GamingProfileStatusText, "対象: Wi-Fi");
        await vm.RestoreGamingProfileCommand.ExecuteAsync(null);
        Assert.AreEqual("Wi-Fi", profile.RestoredAdapter);
    }

    private sealed class FakeGamingProfileService : IGamingNetworkProfileService
    {
        public string? AppliedAdapter { get; private set; }
        public string? RestoredAdapter { get; private set; }
        public bool Throw { get; init; }
        public Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(string adapterName, CancellationToken ct = default)
        {
            AppliedAdapter = adapterName;
            return Throw
                ? Task.FromException<IReadOnlyList<CommandExecutionResult>>(new IOException("test failure"))
                : Task.FromResult<IReadOnlyList<CommandExecutionResult>>([new(false, "適用確認", 1, "", "設定不一致")]);
        }
        public Task<IReadOnlyList<CommandExecutionResult>> RestoreAsync(string adapterName, CancellationToken ct = default)
        {
            RestoredAdapter = adapterName;
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([new(true, "復元", 0, "復元確認・再起動が必要", "")]);
        }
    }

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

    [TestMethod]
    public async Task QuickOptimization_ResetsTcpBeforeEnablingBbr2()
    {
        var tcpTuningService = new RecordingTcpTuningService();
        using var gate = new NetworkMutationGate();
        var dnsViewModel = CreateViewModel(
            new FakeDnsConfigurationService(),
            gate,
            tcpTuningService: tcpTuningService);
        var optimizationViewModel = new AutoOptimizationViewModel(dnsViewModel, gate);

        await optimizationViewModel.RunQuickOptimizationCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "ResetAll", "EnableBbr2", "RevertGlobalOptions", "RevertLegacyTweaks", "SetAutoTuningNormal", "EnableLossRecovery", "GetCurrentState" },
            tcpTuningService.Calls);
        StringAssert.Contains(dnsViewModel.OneClickOptimizeDescription, "BBR2 を有効化");
        StringAssert.Contains(dnsViewModel.OneClickOptimizeDescription, "ループバック Large MTU を無効化");
        StringAssert.Contains(dnsViewModel.StatusText, "実効 Normal を確認済み");
        StringAssert.Contains(dnsViewModel.StatusText, "実状態は未検証");
    }

    [TestMethod]
    [DataRow(Bbr2Status.Partial, "Normal", "Local", "NotConfigured")]
    [DataRow(Bbr2Status.Unknown, "Normal", "Local", "NotConfigured")]
    [DataRow(Bbr2Status.Enabled, "Normal", "GroupPolicy", "Disabled")]
    [DataRow(Bbr2Status.Enabled, "Normal", "", "")]
    public async Task QuickOptimization_UnexpectedState_DoesNotClaimSuccess(
        Bbr2Status bbr2, string local, string source, string policy)
    {
        var tcpService = new RecordingTcpTuningService
        {
            State = new(bbr2, new Dictionary<TcpGlobalOption, string>(), local,
                AutoTuningLevelGroupPolicy: policy, AutoTuningLevelEffective: source),
        };
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(new FakeDnsConfigurationService(), gate, tcpTuningService: tcpService);
        var results = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => results.Add(result);

        await new AutoOptimizationViewModel(viewModel, gate).RunQuickOptimizationCommand.ExecuteAsync(null);

        Assert.IsTrue(results.Any(result => !result.Success && result.CommandLine.Contains("適用後確認")));
        StringAssert.Contains(viewModel.StatusText, "状態を確認できませんでした");
        Assert.IsFalse(viewModel.StatusText.Contains("確認済み"));
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task QuickOptimization_StateReadThrows_StillSavesAndReleasesGate()
    {
        var settings = new FakeSettingsService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(new FakeDnsConfigurationService(), gate, settings,
            tcpTuningService: new RecordingTcpTuningService { ThrowOnRead = true });
        var results = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => results.Add(result);

        await new AutoOptimizationViewModel(viewModel, gate).RunQuickOptimizationCommand.ExecuteAsync(null);

        Assert.IsTrue(settings.SaveCalled);
        Assert.IsTrue(results.Any(result => result.CommandLine == "TCP 適用後確認" && !result.Success));
        Assert.IsFalse(viewModel.IsBusy);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var lease = await gate.EnterAsync(timeout.Token);
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QuickOptimization_WindowsServices_RunInOrderAndPreserveCommandFailures(bool failLossRecovery)
    {
        // 実サービス/ビルダー/パーサーを通し、外部プロセス実行だけを差し替える。
        var executor = new RecordingExecutor { FailLossRecovery = failLossRecovery };
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(new FakeDnsConfigurationService(), gate,
            tcpTuningService: new WindowsTcpTuningService(executor),
            maintenanceService: new WindowsNetworkMaintenanceService(executor));

        await new AutoOptimizationViewModel(viewModel, gate).RunQuickOptimizationCommand.ExecuteAsync(null);

        var calls = executor.Arguments;
        Assert.IsTrue(calls.IndexOf("int tcp reset") < calls.FindIndex(value => value.Contains("congestionprovider=BBR2")));
        Assert.IsTrue(calls.IndexOf("int tcp reset") < calls.FindIndex(value => value.Contains("rack=enabled")));
        Assert.AreEqual(4, calls.Count(value => value.Contains("rack=enabled taillossprobe=enabled")));
        Assert.IsTrue(calls.Contains("interface udp set global uro=default"));
        Assert.IsTrue(calls.Contains("interface udp set global uso=default"));
        Assert.AreEqual(WindowsTcpStateCommandBuilder.Arguments, calls[^1]);
        Assert.AreEqual(!failLossRecovery, viewModel.StatusText.Contains("実効 Normal を確認済み"));
    }

    [TestMethod]
    public async Task QuickOptimization_UdpFailure_DoesNotSkipOtherCommandsOrHideFailure()
    {
        var maintenance = new RecordingMaintenanceService();
        var tcpService = new RecordingTcpTuningService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(new FakeDnsConfigurationService(), gate,
            tcpTuningService: tcpService, maintenanceService: maintenance);
        var results = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => results.Add(result);

        await new AutoOptimizationViewModel(viewModel, gate).RunQuickOptimizationCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(WindowsMaintenanceCommandCatalog.All
            .Where(command => command.Definition.IncludeInOneClickOptimization)
            .Select(command => command.Definition.Id).ToArray(), maintenance.Calls);
        Assert.IsTrue(results.Any(result => result.CommandLine == "netsh-udp-uro-default" && !result.Success));
        CollectionAssert.Contains(tcpService.Calls, "EnableLossRecovery");
        CollectionAssert.Contains(tcpService.Calls, "GetCurrentState");
        StringAssert.Contains(viewModel.StatusText, "一部の設定が失敗");
    }

    [TestMethod]
    public async Task SelectedPresetChanged_OlderDohResult_DoesNotOverwriteLatestPreset()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DoH UI は Windows 専用です。");
        }

        var dohService = new ControllableDohService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(new FakeDnsConfigurationService(), gate, dohService: dohService);
        var google = new TaskCompletionSource<DohStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var quad9 = new TaskCompletionSource<DohStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        dohService.StatusHandler = servers => servers.Ipv4Primary switch
        {
            "8.8.8.8" => google.Task,
            "9.9.9.9" => quad9.Task,
            _ => Task.FromResult(DohStatus.Disabled),
        };

        viewModel.SelectedPreset = DnsPresetCatalog.GooglePublicDns;
        viewModel.SelectedPreset = DnsPresetCatalog.Quad9;
        quad9.SetResult(DohStatus.Disabled);
        await WaitUntilAsync(() => viewModel.DohStateText == "🔴 無効");

        google.SetResult(DohStatus.Enabled);
        await Task.Yield();
        await Task.Yield();

        Assert.IsFalse(viewModel.UseDoh);
        Assert.AreEqual("🔴 無効", viewModel.DohStateText);
    }

    [TestMethod]
    public async Task SelectedAdapterChanged_OlderDetails_DoesNotOverwriteLatestAdapter()
    {
        var adapterService = new ControllableNetworkAdapterService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(
            new FakeDnsConfigurationService(),
            gate,
            adapterService: adapterService);
        var ethernet = new TaskCompletionSource<NetworkAdapterDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifi = new TaskCompletionSource<NetworkAdapterDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapterService.DetailsHandler = id => id == Adapter.Id ? ethernet.Task : wifi.Task;

        viewModel.SelectedAdapter = Adapter2;
        viewModel.SelectedAdapter = Adapter;
        ethernet.SetResult(new(Adapter.Id, "AA-AA-AA-AA-AA-AA", "100 Mbps", null, true));
        await WaitUntilAsync(() => viewModel.AdapterDetails?.Id == Adapter.Id);

        wifi.SetResult(new(Adapter2.Id, "BB-BB-BB-BB-BB-BB", "1 Gbps", null, true));
        await Task.Yield();
        await Task.Yield();

        Assert.AreEqual(Adapter.Id, viewModel.AdapterDetails?.Id);
    }

    [TestMethod]
    public async Task ConcurrentApplyAndAdapterCleanup_BusyRemainsTrueUntilBothFinish()
    {
        var applyResult = new TaskCompletionSource<IReadOnlyList<CommandExecutionResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dnsService = new FakeDnsConfigurationService
        {
            ApplyHandler = (_, _) =>
            {
                applyStarted.TrySetResult();
                return applyResult.Task;
            },
        };
        var cleanupService = new ControllableAdapterNameService();
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(dnsService, gate, adapterNameService: cleanupService);

        var applyTask = viewModel.ApplyCommand.ExecuteAsync(null);
        await applyStarted.Task;
        var cleanupTask = viewModel.CleanupAdapterNameCommand.ExecuteAsync(null);
        applyResult.SetResult([new(true, "apply", 0, string.Empty, string.Empty)]);
        await cleanupService.Started.Task;
        await applyTask;

        Assert.IsTrue(viewModel.IsBusy, "接続名整理が継続中なのに Busy が解除されました。");

        cleanupService.Completion.SetResult(new(true, Adapter.DisplayName, Adapter.DisplayName, 0, 0, false, [], null));
        await cleanupTask;
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task CleanupAdapterName_ReloadAfterRename_HoldsMutationGateUntilSelectionIsUpdated()
    {
        var adapterService = new ControllableNetworkAdapterService();
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadResult = new TaskCompletionSource<IReadOnlyList<NetworkAdapterInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        adapterService.AdaptersHandler = () =>
        {
            if (Interlocked.Increment(ref loadCount) == 1)
            {
                return Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>([Adapter]);
            }

            reloadStarted.TrySetResult();
            return reloadResult.Task;
        };
        var cleanupService = new ImmediateAdapterNameService(
            new(true, Adapter.DisplayName, "Ethernet", 1, 0, true, [], null));
        using var gate = new NetworkMutationGate();
        var viewModel = CreateViewModel(
            new FakeDnsConfigurationService(),
            gate,
            adapterNameService: cleanupService,
            adapterService: adapterService);

        var cleanupTask = viewModel.CleanupAdapterNameCommand.ExecuteAsync(null);
        await reloadStarted.Task;
        var competingLeaseTask = gate.EnterAsync();

        Assert.AreNotEqual(competingLeaseTask, await Task.WhenAny(competingLeaseTask, Task.Delay(100)),
            "再読込中にネットワーク変更ゲートが解放されました。");

        reloadResult.SetResult([Adapter]);
        await cleanupTask;
        using var competingLease = await competingLeaseTask;
        Assert.AreEqual(Adapter.Id, viewModel.SelectedAdapter?.Id);
    }

    private static DnsSettingsViewModel CreateViewModel(
        FakeDnsConfigurationService dnsService,
        NetworkMutationGate gate,
        FakeSettingsService? settingsService = null,
        INetworkAdapterNameService? adapterNameService = null,
        ITcpTuningService? tcpTuningService = null,
        INetworkMaintenanceService? maintenanceService = null,
        INetworkAdapterService? adapterService = null,
        IDohConfigurationService? dohService = null) =>
        new(
            adapterService ?? new FakeNetworkAdapterService(),
            dnsService,
            new FakeDnsCacheService(),
            settingsService ?? new FakeSettingsService(),
            new FakeNetworkDiagnosticsService(),
            gate,
            dohService: dohService,
            tcpTuningService: tcpTuningService,
            maintenanceService: maintenanceService,
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
        public Func<string, DnsServerSet, Task<IReadOnlyList<CommandExecutionResult>>>? ApplyHandler { get; init; }

        public IReadOnlyList<CommandExecutionResult> ResetResults { get; init; } =
            [new(true, "netsh reset", 0, string.Empty, string.Empty)];

        public Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(
            string adapterId,
            DnsServerSet servers,
            CancellationToken ct = default)
        {
            ApplyCallCount++;
            if (ApplyHandler is not null)
            {
                return ApplyHandler(adapterId, servers);
            }

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

    private sealed class ControllableDohService : IDohConfigurationService
    {
        public Func<DnsServerSet, Task<DohStatus>> StatusHandler { get; set; } =
            _ => Task.FromResult(DohStatus.Disabled);

        public Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(
            DnsServerSet servers,
            string dohTemplate,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CommandExecutionResult>>([]);

        public Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(
            DnsServerSet servers,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CommandExecutionResult>>([]);

        public Task<DohStatus> GetStatusAsync(DnsServerSet servers, CancellationToken ct = default) =>
            StatusHandler(servers);
    }

    private sealed class ControllableNetworkAdapterService : INetworkAdapterService
    {
        public Func<Task<IReadOnlyList<NetworkAdapterInfo>>> AdaptersHandler { get; set; } =
            () => Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>([Adapter, Adapter2]);

        public Func<string, Task<NetworkAdapterDetails?>> DetailsHandler { get; set; } =
            _ => Task.FromResult<NetworkAdapterDetails?>(null);

        public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default) =>
            AdaptersHandler();

        public Task<NetworkAdapterDetails?> GetAdapterDetailsAsync(
            string adapterId,
            CancellationToken ct = default) => DetailsHandler(adapterId);
    }

    private sealed class ControllableAdapterNameService : INetworkAdapterNameService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<NetworkAdapterNameCleanupResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NetworkAdapterNameCleanupResult> CleanupAsync(
            string? currentName,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            return Completion.Task;
        }
    }

    private sealed class ImmediateAdapterNameService(NetworkAdapterNameCleanupResult result) : INetworkAdapterNameService
    {
        public Task<NetworkAdapterNameCleanupResult> CleanupAsync(
            string? currentName,
            CancellationToken ct = default) => Task.FromResult(result);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingTcpTuningService : TcpTuningServiceTestStub
    {
        public List<string> Calls { get; } = [];
        public TcpSettingsSnapshot State { get; init; } = new(Bbr2Status.Enabled,
            new Dictionary<TcpGlobalOption, string>(), "Normal", AutoTuningLevelEffective: "Local");
        public bool ThrowOnRead { get; init; }

        public override Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
        {
            Calls.Add("GetCurrentState");
            return ThrowOnRead ? Task.FromException<TcpSettingsSnapshot>(new InvalidOperationException("read failed"))
                : Task.FromResult(State);
        }

        public override Task<IReadOnlyList<CommandExecutionResult>> EnableLossRecoveryAsync(CancellationToken ct = default)
        {
            Calls.Add("EnableLossRecovery");
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([Success()]);
        }

        public override Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default)
        {
            Calls.Add("ResetAll");
            return Task.FromResult(Success());
        }

        public override Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default)
        {
            Calls.Add("EnableBbr2");
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([Success()]);
        }

        public override Task<IReadOnlyList<CommandExecutionResult>> RevertGlobalOptionsToDefaultAsync(CancellationToken ct = default)
        {
            Calls.Add("RevertGlobalOptions");
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([Success()]);
        }

        public override Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default)
        {
            Calls.Add("RevertLegacyTweaks");
            return Task.FromResult(Success());
        }

        public override Task<CommandExecutionResult> SetAutoTuningLevelAsync(
            AutoTuningLevel level,
            CancellationToken ct = default)
        {
            Calls.Add($"SetAutoTuning{level}");
            return Task.FromResult(Success());
        }
    }

    private sealed class RecordingMaintenanceService : INetworkMaintenanceService
    {
        public List<string> Calls { get; } = [];
        public IReadOnlyList<MaintenanceCommandDefinition> GetAvailableCommands() =>
            WindowsMaintenanceCommandCatalog.All.Select(command => command.Definition).ToArray();
        public IReadOnlyDictionary<string, string> GetBatchableCategoryLabels() =>
            WindowsMaintenanceCommandCatalog.BatchableCategoryLabels;
        public Task<CommandExecutionResult> RunAsync(string commandId, CancellationToken ct = default)
        {
            Calls.Add(commandId);
            var success = commandId != "netsh-udp-uro-default";
            return Task.FromResult(new CommandExecutionResult(success, commandId, success ? 0 : 1,
                string.Empty, success ? string.Empty : "unsupported"));
        }
    }

    private sealed class RecordingExecutor : ICommandExecutor
    {
        public List<string> Arguments { get; } = [];
        public bool FailLossRecovery { get; init; }
        public Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
        {
            Arguments.Add(arguments);
            var output = arguments == WindowsTcpStateCommandBuilder.Arguments
                ? "CC=Internet|BBR2\nCC=InternetCustom|BBR2\nCC=Datacenter|BBR2\nCC=DatacenterCustom|BBR2\nCC=Compat|BBR2\nAUTOTUNE=Normal\nAUTOTUNE_SOURCE=Local\nAUTOTUNE_POLICY=NotConfigured"
                : string.Empty;
            var success = !(FailLossRecovery && arguments.Contains("template=InternetCustom rack="));
            return Task.FromResult(new CommandExecutionResult(success, $"{fileName} {arguments}", success ? 0 : 1,
                output, success ? string.Empty : "unsupported"));
        }
    }
}
