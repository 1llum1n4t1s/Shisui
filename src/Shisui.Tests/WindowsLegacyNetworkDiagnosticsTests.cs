using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using System.Text;
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
        var script = DecodeScript(args);

        StringAssert.Contains(script, "Get-NetAdapter -Name '*' -IncludeHidden", script);
        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Wi-Fi ''Test''',[StringComparison]::OrdinalIgnoreCase)", script);
        StringAssert.Contains(script, "Get-NetAdapterStatistics -Name $escaped", script);
        StringAssert.Contains(script, "[string]$a[0].DriverDate", script);
        Assert.IsFalse(script.Contains("DriverDate.ToString", StringComparison.Ordinal), script);
        StringAssert.Contains(script, "'RX_ERRORS='", script);
        StringAssert.Contains(script, "'TASK_OFFLOAD_DISABLED='", script);
    }

    [TestMethod]
    public void BuildResetAdapterAdvancedPropertiesArguments_TargetsOnlySelectedAdapter()
    {
        var args = WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildResetAdapterAdvancedPropertiesArguments("Ethernet 2");
        var script = DecodeScript(args);

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Ethernet 2',[StringComparison]::OrdinalIgnoreCase)", script);
        StringAssert.Contains(script, "$escaped=[WildcardPattern]::Escape([string]$a[0].Name)", script);
        StringAssert.Contains(script, "Reset-NetAdapterAdvancedProperty -Name $escaped -DisplayName '*'", script);
    }

    [TestMethod]
    public void BuildResetAdapterAdvancedPropertiesArguments_DoubleQuoteCannotBreakOuterCommand()
    {
        var arguments = WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildResetAdapterAdvancedPropertiesArguments("Ether\"net");
        var script = DecodeScript(arguments);

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Ether\"net',[StringComparison]::OrdinalIgnoreCase)");
        StringAssert.Contains(script, "Reset-NetAdapterAdvancedProperty -Name $escaped");
        Assert.IsFalse(arguments.Contains("Ether\"net", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildResetAdapterAdvancedPropertiesArguments_WildcardNameIsResolvedByExactEquality()
    {
        var script = DecodeScript(
            WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildResetAdapterAdvancedPropertiesArguments(
                "ローカル エリア接続* 9"));

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'ローカル エリア接続* 9',[StringComparison]::OrdinalIgnoreCase)");
        StringAssert.Contains(script, "Reset-NetAdapterAdvancedProperty -Name $escaped");
        Assert.IsFalse(script.Contains("Reset-NetAdapterAdvancedProperty -Name 'ローカル エリア接続* 9'", StringComparison.Ordinal));
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
        StringAssert.Contains(result.CommandLine, "-EncodedCommand ");
        StringAssert.Contains(DecodeScript(result.CommandLine), "Reset-NetAdapterAdvancedProperty");
    }

    private sealed class FakeExecutor : ICommandExecutor
    {
        public Task<CommandExecutionResult> RunAsync(
            string fileName, string arguments, CancellationToken ct = default)
        {
            var commandText = arguments.Contains("-EncodedCommand ", StringComparison.Ordinal)
                ? DecodeScript(arguments)
                : arguments;
            var output = commandText switch
            {
                WindowsLegacyNetworkDiagnosticsCommandBuilder.WinsockArguments =>
                    "Winsock 送信自動チューニングは無効にされています。",
                WindowsLegacyNetworkDiagnosticsCommandBuilder.ProblemDevicesArguments =>
                    "<PnpUtil><Device InstanceId=\"PROBLEM\"/></PnpUtil>",
                _ when commandText.Contains("Get-NetAdapterStatistics", StringComparison.Ordinal) => AdapterOutput,
                _ when commandText.Contains("Reset-NetAdapterAdvancedProperty", StringComparison.Ordinal) => "RESET=Ethernet",
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

    private static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
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
