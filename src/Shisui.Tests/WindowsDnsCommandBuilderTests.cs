using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDnsCommandBuilderTests
{
    [TestMethod]
    public void BuildApply_Ipv4Only_ProducesSetAndAddCommands()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", null, null);
        var commands = WindowsDnsCommandBuilder.BuildApply("Ethernet", servers);

        CollectionAssert.AreEqual(new[]
        {
            "interface ipv4 set dnsservers name=\"Ethernet\" source=static address=1.1.1.1 register=primary validate=no",
            "interface ipv4 add dnsservers name=\"Ethernet\" address=1.0.0.1 index=2 validate=no",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildApply_Ipv4AndIpv6_ProducesFourCommands()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001");
        var commands = WindowsDnsCommandBuilder.BuildApply("Wi-Fi", servers);

        Assert.AreEqual(4, commands.Count);
        Assert.IsTrue(commands[0].StartsWith("interface ipv4 set dnsservers"));
        Assert.IsTrue(commands[1].StartsWith("interface ipv4 add dnsservers"));
        Assert.IsTrue(commands[2].StartsWith("interface ipv6 set dnsservers"));
        Assert.IsTrue(commands[3].StartsWith("interface ipv6 add dnsservers"));
    }

    [TestMethod]
    public void BuildApply_PrimaryOnly_SkipsAddCommand()
    {
        var servers = new DnsServerSet("1.1.1.1", null, null, null);
        var commands = WindowsDnsCommandBuilder.BuildApply("Ethernet", servers);

        Assert.AreEqual(1, commands.Count);
        Assert.IsTrue(commands[0].Contains("set dnsservers"));
    }

    [TestMethod]
    public void BuildApply_AdapterNameWithSpaces_IsQuotedForNetshRawParser()
    {
        // netsh は CommandLineToArgvW ではなく生コマンドラインを独自再パースするため、
        // 日本語 Windows で頻出する "イーサネット 2" のようなスペース入り名も
        // 二重引用符が付いた状態でそのまま文字列に残る必要がある。
        var servers = new DnsServerSet("8.8.8.8", null, null, null);
        var commands = WindowsDnsCommandBuilder.BuildApply("イーサネット 2", servers);

        Assert.AreEqual("interface ipv4 set dnsservers name=\"イーサネット 2\" source=static address=8.8.8.8 register=primary validate=no", commands[0]);
    }

    [TestMethod]
    public void BuildResetToAutomatic_SetsBothFamiliesToDhcp()
    {
        var commands = WindowsDnsCommandBuilder.BuildResetToAutomatic("Ethernet");

        CollectionAssert.AreEqual(new[]
        {
            "interface ipv4 set dnsservers name=\"Ethernet\" source=dhcp",
            "interface ipv6 set dnsservers name=\"Ethernet\" source=dhcp",
        }, commands.ToList());
    }
}
