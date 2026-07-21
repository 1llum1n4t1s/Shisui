using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[SupportedOSPlatform("windows")]
[TestClass]
public class WindowsLegacyNetworkDiagnosticsTests
{
    private const string AdapterOutput = """
        DESCRIPTION=Contoso 10GbE
        DRIVER_VERSION=1.2.3
        DRIVER_DATE=2010-01-02
        LINK_SPEED=1 Gbps
        RX_ERRORS=4
        TX_ERRORS=1
        RX_DISCARDS=120
        TX_DISCARDS=0
        RX_PACKETS=50000
        TX_PACKETS=50000
        TASK_OFFLOAD_DISABLED=1
        """;

    [TestMethod]
    public void BuildAdapterSnapshotArguments_QuotesPowerShellLiteralAndUsesFixedKeys()
    {
        var args = WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildAdapterSnapshotArguments("Wi-Fi 'Test'");

        StringAssert.Contains(args, "Get-NetAdapterStatistics -Name 'Wi-Fi ''Test'''", args);
        StringAssert.Contains(args, "[string]$a.DriverDate", args);
        Assert.IsFalse(args.Contains("DriverDate.ToString", StringComparison.Ordinal), args);
        StringAssert.Contains(args, "'RX_ERRORS='", args);
        StringAssert.Contains(args, "'TASK_OFFLOAD_DISABLED='", args);
    }

    [TestMethod]
    public void BuildResetAdapterAdvancedPropertiesArguments_TargetsOnlySelectedAdapter()
    {
        var args = WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildResetAdapterAdvancedPropertiesArguments("Ethernet 2");

        Assert.AreEqual(
            "-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop';Reset-NetAdapterAdvancedProperty -Name 'Ethernet 2' -DisplayName '*' -Confirm:$false;'RESET=Ethernet 2'\"",
            args);
    }

    [TestMethod]
    public void ParseAdapterSnapshot_ParsesLocaleIndependentValues()
    {
        var snapshot = WindowsLegacyNetworkDiagnosticsParser.ParseAdapterSnapshot(AdapterOutput);

        Assert.AreEqual("Contoso 10GbE", snapshot.Description);
        Assert.AreEqual("1.2.3", snapshot.DriverVersion);
        Assert.AreEqual(new DateTime(2010, 1, 2), snapshot.DriverDate);
        Assert.AreEqual((ulong)4, snapshot.ReceivedPacketErrors);
        Assert.AreEqual((ulong)120, snapshot.ReceivedDiscardedPackets);
        Assert.IsTrue(snapshot.TaskOffloadDisabled);
    }

    [TestMethod]
    public void ParseWinsockSendAutoTuning_RecognizesEnglishAndJapaneseValueTokens()
    {
        Assert.IsTrue(WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning(
            "Winsock send autotuning is enabled."));
        Assert.IsFalse(WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning(
            "Winsock send autotuning is disabled."));
        Assert.IsTrue(WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning(
            "Winsock 送信自動チューニングは有効にされています。"));
        Assert.IsFalse(WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning(
            "Winsock 送信自動チューニングは無効にされています。"));
        Assert.IsNull(WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning("状態不明"));
    }

    [TestMethod]
    public void ParseProblemDeviceCount_CountsDeviceElements()
    {
        const string xml = """
            <?xml version="1.0"?>
            <PnpUtil><Device InstanceId="A"/><Device InstanceId="B"/></PnpUtil>
            """;

        Assert.AreEqual(2, WindowsLegacyNetworkDiagnosticsParser.ParseProblemDeviceCount(xml));
        Assert.AreEqual(0, WindowsLegacyNetworkDiagnosticsParser.ParseProblemDeviceCount("not xml"));
        Assert.IsFalse(WindowsLegacyNetworkDiagnosticsParser.TryParseProblemDeviceCount("not xml", out _));
    }

    [TestMethod]
    public async Task DiagnoseAsync_FindsOldPcProblemsAndMapsRepairPaths()
    {
        var executor = new FakeExecutor();
        var ghosts = new FakeGhostAdapterService();
        var service = new WindowsLegacyNetworkDiagnosticsService(executor, ghosts);

        var report = await service.DiagnoseAsync("Ethernet");

        Assert.IsTrue(report.HasWarnings);
        Assert.IsTrue(report.RecommendNicReset);
        Assert.AreEqual(1, report.ProblemDeviceCount);
        Assert.AreEqual(1, report.GhostAdapterCount);
        Assert.IsTrue(report.Findings.Any(f => f.RepairPath == LegacyNetworkRepairPath.QuickOptimization));
        Assert.IsTrue(report.Findings.Any(f => f.RepairPath == LegacyNetworkRepairPath.GhostAdapters));
        Assert.IsTrue(report.Findings.Any(f => f.RepairPath == LegacyNetworkRepairPath.NicAdvancedPropertiesReset));
    }

    [TestMethod]
    public async Task ResetAdapterAdvancedPropertiesAsync_ReturnsLoggedCommandResult()
    {
        var executor = new FakeExecutor();
        var service = new WindowsLegacyNetworkDiagnosticsService(executor, new FakeGhostAdapterService());

        var result = await service.ResetAdapterAdvancedPropertiesAsync("Ethernet");

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.CommandLine, "Reset-NetAdapterAdvancedProperty");
    }

    private sealed class FakeExecutor : ICommandExecutor
    {
        public Task<CommandExecutionResult> RunAsync(
            string fileName, string arguments, CancellationToken ct = default)
        {
            var output = arguments switch
            {
                WindowsLegacyNetworkDiagnosticsCommandBuilder.WinsockArguments =>
                    "Winsock 送信自動チューニングは無効にされています。",
                WindowsLegacyNetworkDiagnosticsCommandBuilder.ProblemDevicesArguments =>
                    "<PnpUtil><Device InstanceId=\"PROBLEM\"/></PnpUtil>",
                _ when arguments.Contains("Get-NetAdapterStatistics", StringComparison.Ordinal) => AdapterOutput,
                _ when arguments.Contains("Reset-NetAdapterAdvancedProperty", StringComparison.Ordinal) => "RESET=Ethernet",
                _ => string.Empty,
            };
            return Task.FromResult(new CommandExecutionResult(
                true,
                $"{fileName} {arguments}",
                0,
                output,
                string.Empty));
        }
    }

    private sealed class FakeGhostAdapterService : IGhostAdapterService
    {
        public Task<IReadOnlyList<GhostAdapterInfo>> GetGhostAdaptersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GhostAdapterInfo>>([
                new("ROOT\\OLDVPN\\0000", "Old VPN", "Vendor", "oem1.inf", false),
            ]);

        public Task<CommandExecutionResult> RemoveGhostAdapterAsync(
            string instanceId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
