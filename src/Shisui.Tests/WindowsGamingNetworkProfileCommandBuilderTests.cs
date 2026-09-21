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
            var source = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var checker =
                $"$s=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{source}'));" +
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
