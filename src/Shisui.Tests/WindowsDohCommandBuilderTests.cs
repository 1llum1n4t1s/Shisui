using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDohCommandBuilderTests
{
    [TestMethod]
    public void BuildEnable_Ipv4Only_AddsEncryptionThenGlobalDoh()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", null, null);
        var commands = WindowsDohCommandBuilder.BuildEnable(servers, "https://cloudflare-dns.com/dns-query");

        CollectionAssert.AreEqual(new[]
        {
            "dnsclient add encryption server=\"1.1.1.1\" dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=yes",
            "dnsclient add encryption server=\"1.0.0.1\" dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=yes",
            "dnsclient set global doh=yes",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildEnable_Ipv4AndIpv6_RegistersAllFourAddresses()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001");
        var commands = WindowsDohCommandBuilder.BuildEnable(servers, "https://cloudflare-dns.com/dns-query");

        Assert.AreEqual(5, commands.Count); // 4 アドレス + グローバル設定 1 件
        Assert.IsTrue(commands[0].Contains("server=\"1.1.1.1\""));
        Assert.IsTrue(commands[1].Contains("server=\"1.0.0.1\""));
        Assert.IsTrue(commands[2].Contains("server=\"2606:4700:4700::1111\""));
        Assert.IsTrue(commands[3].Contains("server=\"2606:4700:4700::1001\""));
        Assert.AreEqual("dnsclient set global doh=yes", commands[4]);
    }

    [TestMethod]
    public void BuildEnable_PrimaryOnly_ProducesTwoCommands()
    {
        var servers = new DnsServerSet("8.8.8.8", null, null, null);
        var commands = WindowsDohCommandBuilder.BuildEnable(servers, "https://dns.google/dns-query");

        Assert.AreEqual(2, commands.Count);
        Assert.IsTrue(commands[0].Contains("server=\"8.8.8.8\""));
        Assert.AreEqual("dnsclient set global doh=yes", commands[1]);
    }

    [TestMethod]
    public void BuildDisable_DeletesEncryptionForEachAddress_AndDoesNotTouchGlobal()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", null, null);
        var commands = WindowsDohCommandBuilder.BuildDisable(servers);

        CollectionAssert.AreEqual(new[]
        {
            "dnsclient delete encryption server=\"1.1.1.1\" protocol=doh",
            "dnsclient delete encryption server=\"1.0.0.1\" protocol=doh",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildEnable_AddressContainingShellMetacharacters_IsQuoted()
    {
        // カスタム DNS 入力欄はユーザーの自由入力であり、クオートなしでは netsh の独自パーサーが
        // 空白等を追加パラメータとして誤解釈しうる (2026-07-06 /rere レビューで発見)。
        var servers = new DnsServerSet("1.1.1.1 dohtemplate=https://evil.example/dns-query", null, null, null);
        var commands = WindowsDohCommandBuilder.BuildEnable(servers, "https://cloudflare-dns.com/dns-query");

        Assert.IsTrue(commands[0].Contains("server=\"1.1.1.1 dohtemplate=https://evil.example/dns-query\""));
    }

    [TestMethod]
    public void CollectAddresses_SkipsNullAndWhitespace()
    {
        var servers = new DnsServerSet("1.1.1.1", null, "  ", "2606:4700:4700::1001");
        var addresses = WindowsDohCommandBuilder.CollectAddresses(servers);

        CollectionAssert.AreEqual(new[] { "1.1.1.1", "2606:4700:4700::1001" }, addresses.ToList());
    }
}
