using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDotCommandBuilderTests
{
    [TestMethod]
    public void BuildEnable_Ipv4Only_AddsEncryptionWithPortThenGlobalDot()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", null, null);
        var commands = WindowsDotCommandBuilder.BuildEnable(servers, "cloudflare-dns.com");

        CollectionAssert.AreEqual(new[]
        {
            "dnsclient add encryption server=\"1.1.1.1\" dothost=cloudflare-dns.com:853 autoupgrade=yes udpfallback=yes",
            "dnsclient add encryption server=\"1.0.0.1\" dothost=cloudflare-dns.com:853 autoupgrade=yes udpfallback=yes",
            "dnsclient set global dot=yes",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildEnable_Ipv4AndIpv6_RegistersAllFourAddresses()
    {
        var servers = new DnsServerSet("9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9");
        var commands = WindowsDotCommandBuilder.BuildEnable(servers, "dns.quad9.net");

        Assert.AreEqual(5, commands.Count); // 4 アドレス + グローバル設定 1 件
        Assert.IsTrue(commands[0].Contains("server=\"9.9.9.9\"") && commands[0].Contains("dothost=dns.quad9.net:853"));
        Assert.IsTrue(commands[1].Contains("server=\"149.112.112.112\""));
        Assert.IsTrue(commands[2].Contains("server=\"2620:fe::fe\""));
        Assert.IsTrue(commands[3].Contains("server=\"2620:fe::9\""));
        Assert.AreEqual("dnsclient set global dot=yes", commands[4]);
    }

    [TestMethod]
    public void BuildDisable_DeletesEncryptionForEachAddress_WithDotProtocol_AndDoesNotTouchGlobal()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", null, null);
        var commands = WindowsDotCommandBuilder.BuildDisable(servers);

        CollectionAssert.AreEqual(new[]
        {
            "dnsclient delete encryption server=\"1.1.1.1\" protocol=dot",
            "dnsclient delete encryption server=\"1.0.0.1\" protocol=dot",
        }, commands.ToList());
    }
}
