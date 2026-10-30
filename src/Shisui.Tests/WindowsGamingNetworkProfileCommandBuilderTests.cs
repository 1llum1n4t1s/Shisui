using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public sealed class WindowsGamingNetworkProfileCommandBuilderTests
{
    private static readonly Guid AdapterGuid = Guid.Parse("fb283a95-51d8-466b-b72f-c8d27361ca9b");

    [TestMethod]
    public void BuildQueryArguments_HostileAdapterNameIsLiteralAndOutputIsUtf8()
    {
        const string hostileName = "Ethernet';throw 'owned";

        var script = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments(hostileName));

        StringAssert.Contains(script, "'Ethernet'';throw ''owned'");
        StringAssert.Contains(script, "[StringComparison]::OrdinalIgnoreCase");
        StringAssert.Contains(script, "[Text.UTF8Encoding]::new($false)");
        StringAssert.Contains(script, "[WildcardPattern]::Escape([string]$a[0].Name)");
        StringAssert.Contains(script, "Get-NetAdapterAdvancedProperty -Name $escaped -IncludeHidden -AllProperties");
        StringAssert.Contains(script, "DriverProvider=[string]$a[0].DriverProvider");
        StringAssert.Contains(script, "PnPDeviceId=[string]$a[0].PnPDeviceID");
        StringAssert.Contains(script, "LowPowerEnable");
        StringAssert.Contains(script, "UAPSDSupport");
        StringAssert.Contains(script, "EnableGreenEthernet");
        StringAssert.Contains(script, "GigaLite");
        StringAssert.Contains(script, "PowerSavingMode");
        StringAssert.Contains(script, "PCI\\VEN_10EC&*");
        StringAssert.Contains(script, "USB\\VID_0BDA&*");
        Assert.DoesNotContain("Get-NetAdapterAdvancedProperty -InputObject", script);
    }

    [TestMethod]
    public void BuildSetPropertyArguments_UsesExactAllowlistedInputObjectAndNoRestart()
    {
        var script = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet",
            AdapterGuid,
            "Intel Ethernet Controller",
            6,
            "Intel",
            "PCI\\VEN_8086&DEV_1234",
            WindowsGamingNetworkProfileCommandBuilder.InterruptModerationKeyword,
            "1",
            "0"));

        StringAssert.Contains(script, "[guid]$a[0].InterfaceGuid -ne [guid]'fb283a95-51d8-466b-b72f-c8d27361ca9b'");
        StringAssert.Contains(script, "$_.RegistryKeyword -ceq '*InterruptModeration'");
        StringAssert.Contains(script, "$p[0].InterfaceDescription");
        StringAssert.Contains(script, "$p[0].InstanceID");
        StringAssert.Contains(script, "$a[0].DriverProvider");
        StringAssert.Contains(script, "$a[0].PnPDeviceID");
        StringAssert.Contains(script, ".ToString('B')+'::'+'*InterruptModeration'");
        StringAssert.Contains(script, "Set-NetAdapterAdvancedProperty -InputObject $p[0] -RegistryValue '0' -NoRestart");
        Assert.DoesNotContain("-DisplayName", script);
    }

    [TestMethod]
    public async Task GeneratedScripts_ParseWithWindowsPowerShellAstParser()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell 5.1 の AST パーサーを使う Windows 専用テストです。");
        }

        var generatedScripts = new[]
        {
            Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet';throw 'owned")),
            Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Ethernet';throw 'owned",
                AdapterGuid,
                "Intel Ethernet Controller",
                6,
                "Intel",
                "PCI\\VEN_8086&DEV_1234",
                WindowsGamingNetworkProfileCommandBuilder.EnergyEfficientEthernetKeyword,
                "1",
                "0")),
            Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Wi-Fi';throw 'owned",
                AdapterGuid,
                "RZ616 Wi-Fi 6E 160MHz",
                71,
                "MediaTek, Inc.",
                "PCI\\VEN_14C3&DEV_0616&SUBSYS_061614C3",
                WindowsGamingNetworkProfileCommandBuilder.MediaTekLowPowerKeyword,
                "1",
                "0")),
            Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Ethernet';throw 'owned",
                AdapterGuid,
                "Realtek PCIe GbE Family Controller",
                6,
                "Realtek",
                "PCI\\VEN_10EC&DEV_8168&SUBSYS_012310EC&REV_15\\4&ABCDEF&0&00E5",
                WindowsGamingNetworkProfileCommandBuilder.RealtekGreenEthernetKeyword,
                "1",
                "0")),
        };
        var executor = new ProcessCommandExecutor();

        foreach (var script in generatedScripts)
        {
            // 外側の EncodedCommand だけで隔離し、二重 Base64 による Windows の引数長超過を避ける。
            var source = script.Replace("'", "''", StringComparison.Ordinal);
            var checker =
                $"$s='{source}';" +
                "$tokens=$null;$errors=$null;" +
                "[Management.Automation.Language.Parser]::ParseInput($s,[ref]$tokens,[ref]$errors)|Out-Null;" +
                "if($errors.Count -ne 0){[Console]::Error.WriteLine(($errors|ForEach-Object Message)-join [Environment]::NewLine);exit 1}";
            var arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(checker));

            var result = await executor.RunAsync(WindowsGamingNetworkProfileCommandBuilder.FileName, arguments);

            Assert.IsTrue(result.Success, result.StandardError);
        }
    }

    [TestMethod]
    public void BuildSetPropertyArguments_UnknownPropertyIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Ethernet", AdapterGuid, "Controller", 6, "Intel", "PCI\\VEN_8086&DEV_1234", "*FlowControl", "1", "0"));
    }

    [TestMethod]
    [DataRow(2, 2, true)]
    [DataRow(2, 1, true)]
    [DataRow(2, 0, false)]
    [DataRow(1, 2, false)]
    [DataRow(0, 2, false)]
    [DataRow(-1, 2, false)]
    public async Task Query_UsesPowerManagementOnlyWhenMasterAndFeatureAreSupported(int master, int selective, bool present)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        var script = MockCommands(master, selective) + Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet"));
        var result = await RunScript(script);
        Assert.IsTrue(result.Success, result.StandardError);
        Assert.IsTrue(WindowsGamingNetworkProfileParser.TryParse(result.StandardOutput, out var state, out var error), error);
        Assert.AreEqual(present, state!.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword));
        Assert.IsTrue(state.Properties.Any(p => p.RegistryKeyword == "*EEE"));
    }

    [TestMethod]
    public async Task PowerManagementWrite_RechecksIdentityMasterAndValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        var write = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Realtek Controller", 6, "Realtek", "PCI\\VEN_10EC&DEV_8168\\TEST",
            WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword, "2", "1"));
        StringAssert.Contains(write, "Set-NetAdapterPowerManagement -InputObject $pm[0] -SelectiveSuspend $setting -NoRestart");
        Assert.DoesNotContain("Set-NetAdapterAdvancedProperty", write);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Intel", 6, "Intel", "PCI\\VEN_8086&DEV_1234",
            WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword, "2", "1"));
        Assert.IsTrue((await RunScript(MockCommands(2, 2) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(1, 2) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 1) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2, wrongIdentity: true) + write)).Success);
    }

    [TestMethod]
    public async Task Query_PowerManagementErrorIsReportedWithoutDiscardingOtherProperties()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        var query = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet"));
        var result = await RunScript(MockCommands(2, 2, wrongIdentity: true) + query);
        Assert.IsTrue(result.Success, result.StandardError);
        Assert.IsTrue(WindowsGamingNetworkProfileParser.TryParse(result.StandardOutput, out var state, out var error), error);
        StringAssert.Contains(state!.PowerManagementError, "identity mismatch");
        Assert.IsTrue(state.Properties.Any(p => p.RegistryKeyword == "*EEE"));
        Assert.IsFalse(state.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword));
    }

    [TestMethod]
    public async Task Query_PowerManagementUnavailableOrFails_LeavesOtherPropertiesAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        var query = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet"));
        const string stub = "function Get-NetAdapterPowerManagement {[CmdletBinding()]param($Name,[switch]$IncludeHidden) ";
        foreach (var (body, hasError) in new[] { ("}", false), ("throw 'query failure'}", true) })
        {
            var result = await RunScript(MockCommands(2, 2) + stub + body + ";" + query);
            Assert.IsTrue(result.Success, result.StandardError);
            Assert.IsTrue(WindowsGamingNetworkProfileParser.TryParse(result.StandardOutput, out var state, out var error), error);
            Assert.AreEqual(hasError, !string.IsNullOrEmpty(state!.PowerManagementError));
            Assert.IsTrue(state.Properties.Any(p => p.RegistryKeyword == "*EEE"));
            Assert.IsFalse(state.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword));
        }
    }

    [TestMethod]
    public async Task DeviceIdleQuery_ValidatesExactInstanceAndTypes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        const string valid = "[pscustomobject]@{InstanceName='PCI\\VEN_10EC&DEV_8168\\TEST_0';Active=$true;Enable=$true}";
        const string wrong = "[pscustomobject]@{InstanceName='PCI\\VEN_10EC&DEV_8168\\TESTX_0';Active=$true;Enable=$true}";
        var query = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet"));
        foreach (var (body, present, hasError) in new[]
        {
            (valid, true, false),
            (wrong, false, false),
            ("[pscustomobject]@{InstanceName=\"PCI\\VEN_10EC&DEV_8168\\TEST_0`n\";Active=$true;Enable=$true}", false, false),
            ("", false, false),
            (valid.Replace("Active=$true", "Active=$false", StringComparison.Ordinal), false, false),
            (valid.Replace("Enable=$true", "Enable='true'", StringComparison.Ordinal), false, true),
            (valid + ";" + valid, false, true),
            ("throw 'WMI failure'", false, true),
        })
        {
            var result = await RunScript(MockCommands(2, 2) + DeviceCimMock(body) + query);
            Assert.IsTrue(result.Success, result.StandardError);
            Assert.IsTrue(WindowsGamingNetworkProfileParser.TryParse(result.StandardOutput, out var state, out var error), error);
            Assert.AreEqual(present, state!.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword), body);
            Assert.AreEqual(hasError, !string.IsNullOrEmpty(state.DeviceIdleError), body);
            Assert.IsTrue(state.Properties.Any(p => p.RegistryKeyword == "*EEE"));
        }
    }

    [TestMethod]
    public async Task DeviceIdleWrite_RechecksTargetIdentityTypeAndCurrentValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        const string valid = "[pscustomobject]@{InstanceName='PCI\\VEN_10EC&DEV_8168\\TEST_0';Active=$true;Enable=$true}";
        var write = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Realtek Controller", 6, "Realtek", "PCI\\VEN_10EC&DEV_8168\\TEST",
            WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword, "1", "0", "rt68cx21x64.sys"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Intel", 6, "Intel", "PCI\\VEN_8086&DEV_1234",
            WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword, "1", "0", "rt68cx21x64.sys"));
        Assert.IsTrue((await RunScript(MockCommands(2, 2) + DeviceCimMock(valid) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + DeviceCimMock(valid.Replace("Enable=$true", "Enable=$false", StringComparison.Ordinal)) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + DeviceCimMock(valid + ";" + valid) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + DeviceCimMock(valid.Replace("Active=$true", "Active=$false", StringComparison.Ordinal)) + write)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2).Replace("DriverProvider='Realtek'", "DriverProvider='Changed'", StringComparison.Ordinal) + DeviceCimMock(valid) + write)).Success);
        var restore = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Realtek Controller", 6, "Realtek", "PCI\\VEN_10EC&DEV_8168\\TEST",
            WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword, "0", "1", "rt68cx21x64.sys"));
        var restoreMock = DeviceCimMock(valid.Replace("Enable=$true", "Enable=$false", StringComparison.Ordinal))
            .Replace("$Property.Enable -ne $false", "$Property.Enable -ne $true", StringComparison.Ordinal);
        Assert.IsTrue((await RunScript(MockCommands(2, 2) + restoreMock + restore)).Success);
    }

    [TestMethod]
    public async Task WdfQuery_RequiresKnownDriverAndExistingBinaryDword_WithoutWmiFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        var query = Decode(WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments("Ethernet"));
        const string rejectWmi = "function Get-CimInstance {[CmdletBinding()]param($Namespace,$ClassName) throw 'WMI must not be queried'};";
        foreach (var (kind, value, driver, present) in new[]
        {
            ("valid", 0, "rt68cx21x64.sys", true),
            ("valid", 1, "RT68CX21X64.SYS", true),
            ("missing", 0, "rt68cx21x64.sys", false),
            ("string", 0, "rt68cx21x64.sys", false),
            ("valid", 2, "rt68cx21x64.sys", false),
            ("valid", 0, "unknown.sys", false),
        })
        {
            var mock = MockCommands(2, 2).Replace("DriverFileName='rt68cx21x64.sys'", $"DriverFileName='{driver}'", StringComparison.Ordinal);
            var result = await RunScript(mock + WdfMock(kind, value) + rejectWmi + query);
            Assert.IsTrue(result.Success, result.StandardError);
            Assert.IsTrue(WindowsGamingNetworkProfileParser.TryParse(result.StandardOutput, out var state, out var error), error);
            Assert.AreEqual(present, state!.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.WdfDeviceIdleEnabledKeyword), kind);
            Assert.AreEqual(!present, !string.IsNullOrEmpty(state.DeviceIdleError), kind);
            Assert.IsFalse(state.DeviceIdleError.Contains("WMI must not", StringComparison.Ordinal));
            Assert.IsFalse(state.Properties.Any(p => p.RegistryKeyword == WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword));
        }
    }

    [TestMethod]
    public async Task WdfWrite_RevalidatesDriverAndValueAndBlocksWmiWhenWdfAppears()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell が必要です。");
        }

        string Build(string key, string expected, string target) => Decode(WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
            "Ethernet", AdapterGuid, "Realtek Controller", 6, "Realtek", "PCI\\VEN_10EC&DEV_8168\\TEST",
            key, expected, target, "rt68cx21x64.sys"));
        var apply = Build(WindowsGamingNetworkProfilePolicy.WdfDeviceIdleEnabledKeyword, "1", "0");
        var restore = Build(WindowsGamingNetworkProfilePolicy.WdfDeviceIdleEnabledKeyword, "0", "1");
        Assert.IsTrue((await RunScript(MockCommands(2, 2) + WdfMock("valid", 1) + apply)).Success);
        Assert.IsTrue((await RunScript(MockCommands(2, 2) + WdfMock("valid", 0) + restore)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + WdfMock("valid", 0) + apply)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + WdfMock("string", 1) + apply)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + WdfMock("missing", 1) + apply)).Success);
        Assert.IsFalse((await RunScript(MockCommands(2, 2).Replace("rt68cx21x64.sys", "other.sys", StringComparison.Ordinal) + WdfMock("valid", 1) + apply)).Success);
        var wmi = Build(WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword, "1", "0");
        Assert.IsFalse((await RunScript(MockCommands(2, 2) + WdfMock("valid", 1) + wmi)).Success);
    }

    private static string DeviceCimMock(string body) =>
        "function Get-CimInstance {[CmdletBinding()]param($Namespace,$ClassName) " + body + "};" +
        "function Set-CimInstance {[CmdletBinding()]param($InputObject,$Property) " +
        "if($null -eq $InputObject -or $Property.Enable -ne $false){throw 'unexpected WMI write'}};";

    private static string MockCommands(int master, int selective, bool wrongIdentity = false) =>
        "function Get-NetAdapter {[CmdletBinding()]param($Name,[switch]$IncludeHidden) " +
        "[pscustomobject]@{Name='Ethernet';InterfaceGuid='fb283a95-51d8-466b-b72f-c8d27361ca9b';" +
        "InterfaceDescription='Realtek Controller';HardwareInterface=$true;InterfaceType=6;DriverProvider='Realtek';DriverFileName='rt68cx21x64.sys';PnPDeviceID='PCI\\VEN_10EC&DEV_8168\\TEST'}};" +
        "function Get-NetAdapterAdvancedProperty {[CmdletBinding()]param($Name,[switch]$IncludeHidden,[switch]$AllProperties) " +
        "[pscustomobject]@{Name='Ethernet';InterfaceDescription='Realtek Controller';InstanceID='{fb283a95-51d8-466b-b72f-c8d27361ca9b}::*EEE';" +
        "RegistryKeyword='*EEE';RegistryValue=@('1');ValidRegistryValues=@('0','1')}};" +
        "function Get-NetAdapterPowerManagement {[CmdletBinding()]param($Name,[switch]$IncludeHidden) " +
        $"[pscustomobject]@{{Name='Ethernet';InterfaceDescription='Realtek Controller';InstanceID='{(wrongIdentity ? "{00000000-0000-0000-0000-000000000001}" : "{fb283a95-51d8-466b-b72f-c8d27361ca9b}")}';" +
        $"AllowComputerToTurnOffDevice={(master == -1 ? "$null" : master.ToString(System.Globalization.CultureInfo.InvariantCulture))};SelectiveSuspend={selective}}}}};" +
        "function Set-NetAdapterPowerManagement {[CmdletBinding(SupportsShouldProcess)]param($InputObject,$SelectiveSuspend,[switch]$NoRestart) " +
        "if($SelectiveSuspend -ne 'Disabled' -or -not $NoRestart){throw 'unexpected set'}};" + DeviceCimMock("") + WdfMock("absent", 0);

    private static string WdfMock(string kind, int value) =>
        "$global:testWdf=[pscustomobject]@{};" +
        $"$global:testWdf | Add-Member ScriptMethod GetValueNames {{ {(kind == "missing" ? "@()" : "@('IdleInWorkingState')")} }};" +
        $"$global:testWdf | Add-Member ScriptMethod GetValueKind {{ param($name) [Microsoft.Win32.RegistryValueKind]::{(kind == "string" ? "String" : "DWord")} }};" +
        $"$global:testWdf | Add-Member ScriptMethod GetValue {{ param($name) {value} }};" +
        "$global:testWdf | Add-Member ScriptMethod SetValue { param($name,$value,$type) if($name -ne 'IdleInWorkingState' -or $value -notin @(0,1) -or $type -ne [Microsoft.Win32.RegistryValueKind]::DWord){throw 'unexpected registry write'} };" +
        "function Get-Item {[CmdletBinding()]param($LiteralPath) $key=[pscustomobject]@{};" +
        $"$key | Add-Member ScriptMethod OpenSubKey {{param($path,$write) {(kind == "absent" ? "$null" : "$global:testWdf")} }};return $key}};";

    private static Task<Shisui.Core.Models.CommandExecutionResult> RunScript(string script) =>
        new ProcessCommandExecutor().RunAsync("powershell", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

    [TestMethod]
    public void BuildSetPropertyArguments_CrossMediaOrProviderPropertyIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Wi-Fi", AdapterGuid, "RZ616", 71, "Intel", "PCI\\VEN_8086&DEV_1234", "LowPowerEnable", "1", "0"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Wi-Fi", AdapterGuid, "RZ616", 71, "MediaTek, Inc.", "PCI\\VEN_14C3&DEV_0616", "*EEE", "1", "0"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                "Ethernet", AdapterGuid, "Unknown", 6, "Unknown Vendor",
                "PCI\\VEN_9999&DEV_0001",
                "EnableGreenEthernet", "1", "0"));
    }

    private static string Decode(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
