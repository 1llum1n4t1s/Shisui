using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.UI.ViewModels;

namespace Shisui.Tests;

[TestClass]
public class TcpTuningViewModelTests
{
    [TestMethod]
    public async Task EnableLossRecoveryCommand_OneOfFourCommandsFails_DoesNotReportSuccess()
    {
        var service = new FakeTcpTuningService
        {
            LossRecoveryResults =
            [
                Success(),
                Success(),
                new(false, "netsh failure", 1, string.Empty, "failed"),
                Success(),
            ],
        };
        var viewModel = CreateViewModel(service);
        var logged = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => logged.Add(result);

        await viewModel.EnableLossRecoveryCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "失敗");
        Assert.HasCount(4, logged);
        Assert.AreEqual(1, logged.Count(result => !result.Success));
    }

    [TestMethod]
    public async Task EnableBbr2Command_CommandsSucceedButReadBackIsPartial_DoesNotReportSuccess()
    {
        var service = new FakeTcpTuningService
        {
            Bbr2Results = [Success(), Success()],
            CurrentState = CreateSnapshot(Bbr2Status.Partial),
        };
        var viewModel = CreateViewModel(service);
        var logged = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => logged.Add(result);

        await viewModel.EnableBbr2Command.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "失敗");
        Assert.IsTrue(logged.Any(result => result.CommandLine == "BBR2 適用後確認" && !result.Success));
        Assert.IsTrue(service.GetCurrentStateCallCount >= 1);
    }

    [TestMethod]
    public async Task EnableBbr2Command_CommandsAndReadBackSucceed_ReportsVerifiedSuccess()
    {
        var service = new FakeTcpTuningService
        {
            Bbr2Results = [Success(), Success()],
            CurrentState = CreateSnapshot(Bbr2Status.Enabled),
        };
        var viewModel = CreateViewModel(service);

        await viewModel.EnableBbr2Command.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "有効化を確認しました");
        Assert.IsTrue(service.GetCurrentStateCallCount >= 1);
    }

    [TestMethod]
    public async Task SetAutoTuningLevelCommand_GroupPolicyOverridesRequestedValue_DoesNotReportSuccess()
    {
        var service = new FakeTcpTuningService
        {
            CurrentState = CreateSnapshot(
                autoTuningLevel: "Normal",
                autoTuningLevelGroupPolicy: "Disabled",
                autoTuningLevelEffective: "GroupPolicy"),
        };
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAutoTuningLevel = AutoTuningLevel.Normal;
        var logged = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => logged.Add(result);

        await viewModel.SetAutoTuningLevelCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "失敗");
        Assert.IsTrue(logged.Any(result =>
            result.CommandLine == "受信ウィンドウ適用後確認 (Internet)" && !result.Success));
    }

    [TestMethod]
    public async Task SetAutoTuningLevelCommand_UnknownEffectiveSource_DoesNotUseLocalValue()
    {
        var service = new FakeTcpTuningService
        {
            CurrentState = CreateSnapshot(
                autoTuningLevel: "Normal",
                autoTuningLevelGroupPolicy: string.Empty,
                autoTuningLevelEffective: "UnknownSource"),
        };
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAutoTuningLevel = AutoTuningLevel.Normal;

        await viewModel.SetAutoTuningLevelCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "失敗");
    }

    [TestMethod]
    public async Task EnableBbr2Command_StateReadThrows_LogsVerificationFailureAndDoesNotThrow()
    {
        var service = new FakeTcpTuningService
        {
            Bbr2Results = [Success(), Success()],
            StateException = new InvalidOperationException("read failed"),
        };
        var viewModel = CreateViewModel(service);
        var logged = new List<CommandExecutionResult>();
        viewModel.CommandExecuted += (_, result) => logged.Add(result);

        await viewModel.EnableBbr2Command.ExecuteAsync(null);

        StringAssert.Contains(viewModel.StatusText, "失敗");
        Assert.IsTrue(logged.Any(result =>
            result.CommandLine == "TCP 適用後確認" &&
            !result.Success &&
            result.StandardError.Contains("read failed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SelectedAdapterChanged_OlderMtuResult_DoesNotOverwriteLatestAdapter()
    {
        var ethernet = new NetworkAdapterInfo("Ethernet", "Ethernet", null, true, [], []);
        var wifi = new NetworkAdapterInfo("Wi-Fi", "Wi-Fi", null, true, [], []);
        var adapterService = new FakeNetworkAdapterService([ethernet, wifi]);
        var service = new FakeTcpTuningService();
        var viewModel = new TcpTuningViewModel(service, adapterService, new FakeNetworkMutationGate());
        viewModel.Initialize();
        var ethernetMtu = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wifiMtu = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.MtuHandler = id => id == ethernet.Id ? ethernetMtu.Task : wifiMtu.Task;

        viewModel.SelectedAdapter = wifi;
        viewModel.SelectedAdapter = ethernet;
        ethernetMtu.SetResult(1492);
        await WaitUntilAsync(() => viewModel.MtuStateText == "現在の MTU: 1492");

        wifiMtu.SetResult(1500);
        await Task.Yield();
        await Task.Yield();

        Assert.AreEqual("現在の MTU: 1492", viewModel.MtuStateText);
    }

    [TestMethod]
    public async Task RevertMtuCommand_SelectionChangesWhileWaitingForGate_UsesOriginalAdapter()
    {
        var ethernet = new NetworkAdapterInfo("Ethernet", "有線LAN", null, true, [], []);
        var wifi = new NetworkAdapterInfo("Wi-Fi", "Wi-Fi", null, true, [], []);
        var service = new FakeTcpTuningService();
        var gate = new BlockingNetworkMutationGate();
        var viewModel = new TcpTuningViewModel(service, new FakeNetworkAdapterService(), gate)
        {
            SelectedAdapter = ethernet,
        };

        var operation = viewModel.RevertMtuCommand.ExecuteAsync(null);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.SelectedAdapter = wifi;
        gate.Release();
        await operation;

        Assert.AreEqual(ethernet.Id, service.RevertedAdapterId);
        StringAssert.Contains(viewModel.StatusText, ethernet.DisplayName);
        Assert.AreEqual(wifi, viewModel.SelectedAdapter);
    }

    private static TcpTuningViewModel CreateViewModel(FakeTcpTuningService service) =>
        new(service, new FakeNetworkAdapterService(), new FakeNetworkMutationGate());

    private static CommandExecutionResult Success() =>
        new(true, "netsh", 0, string.Empty, string.Empty);

    private static TcpSettingsSnapshot CreateSnapshot(
        Bbr2Status bbr2 = Bbr2Status.Enabled,
        string autoTuningLevel = "Normal",
        string autoTuningLevelGroupPolicy = "",
        string autoTuningLevelEffective = "Local") =>
        new(
            bbr2,
            new Dictionary<TcpGlobalOption, string>(),
            autoTuningLevel,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Internet"] = "BBR2",
                ["InternetCustom"] = "BBR2",
                ["Datacenter"] = "BBR2",
                ["DatacenterCustom"] = "BBR2",
                ["Compat"] = bbr2 == Bbr2Status.Partial ? "CUBIC" : "BBR2",
            },
            autoTuningLevelGroupPolicy,
            autoTuningLevelEffective);

    private sealed class FakeTcpTuningService : TcpTuningServiceTestStub
    {
        public IReadOnlyList<CommandExecutionResult> Bbr2Results { get; init; } = [Success()];
        public IReadOnlyList<CommandExecutionResult> LossRecoveryResults { get; init; } =
            [Success(), Success(), Success(), Success()];
        public TcpSettingsSnapshot CurrentState { get; init; } = CreateSnapshot();
        public Exception? StateException { get; init; }
        public int GetCurrentStateCallCount { get; private set; }
        public string? RevertedAdapterId { get; private set; }
        public Func<string, Task<int?>> MtuHandler { get; set; } = _ => Task.FromResult<int?>(null);

        public override Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default) =>
            Task.FromResult(Bbr2Results);

        public override Task<IReadOnlyList<CommandExecutionResult>> EnableLossRecoveryAsync(CancellationToken ct = default) =>
            Task.FromResult(LossRecoveryResults);

        public override Task<CommandExecutionResult> SetAutoTuningLevelAsync(
            AutoTuningLevel level,
            CancellationToken ct = default) =>
            Task.FromResult(Success());

        public override Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
        {
            GetCurrentStateCallCount++;
            return StateException is null
                ? Task.FromResult(CurrentState)
                : Task.FromException<TcpSettingsSnapshot>(StateException);
        }

        public override Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default) =>
            MtuHandler(adapterId);

        public override Task<IReadOnlyList<CommandExecutionResult>> RevertMtuToDefaultAsync(
            string adapterId,
            CancellationToken ct = default)
        {
            RevertedAdapterId = adapterId;
            return Task.FromResult<IReadOnlyList<CommandExecutionResult>>([Success(), Success()]);
        }
    }

    private sealed class FakeNetworkAdapterService(IReadOnlyList<NetworkAdapterInfo>? adapters = null) : INetworkAdapterService
    {
        public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default) =>
            Task.FromResult(adapters ?? []);

        public Task<NetworkAdapterDetails?> GetAdapterDetailsAsync(
            string adapterId,
            CancellationToken ct = default) =>
            Task.FromResult<NetworkAdapterDetails?>(null);
    }

    private sealed class FakeNetworkMutationGate : INetworkMutationGate
    {
        public Task<IDisposable> EnterAsync(CancellationToken ct = default) =>
            Task.FromResult<IDisposable>(new Lease());

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class BlockingNetworkMutationGate : INetworkMutationGate
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new Lease();
        }

        public void Release() => release.TrySetResult();

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }


    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
