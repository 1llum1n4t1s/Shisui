using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDohCommandBuilderTests
{
    private static string Decode(string args) => Encoding.Unicode.GetString(Convert.FromBase64String(args.Split(' ').Last()));
    [TestMethod]
    public void BuildEnable_Ipv4Only_RepairsThenVerifiesGlobalDoh()
    {
        var script = Decode(WindowsDohCommandBuilder.BuildEnableArguments(new("1.1.1.1", "1.0.0.1", null, null), "https://cloudflare-dns.com/dns-query"));
        StringAssert.Contains(script, "Set-DnsClientDohServerAddress");
        StringAssert.Contains(script, "Add-DnsClientDohServerAddress");
        StringAssert.Contains(script, "dnsclient set global doh=yes");
        StringAssert.Contains(script, "DOHREPAIR=VERIFIED");
    }
    [TestMethod]
    public void BuildEnable_Ipv4AndIpv6_RegistersAllFourAddresses()
    {
        var script = Decode(WindowsDohCommandBuilder.BuildEnableArguments(new("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"), "https://cloudflare-dns.com/dns-query"));
        foreach (var address in new[] { "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001" })
            StringAssert.Contains(script, "'" + address + "'");
    }
    [TestMethod]
    public void BuildEnable_PrimaryOnly_ProducesRepairScript() =>
        StringAssert.Contains(Decode(WindowsDohCommandBuilder.BuildEnableArguments(new("8.8.8.8", null, null, null), "https://dns.google/dns-query")), "'8.8.8.8'");
    [TestMethod]
    public void BuildDisable_DeletesEncryptionForEachAddress_AndDoesNotTouchGlobal() =>
        CollectionAssert.AreEqual(new[] { "dnsclient delete encryption server=\"1.1.1.1\" protocol=doh", "dnsclient delete encryption server=\"1.0.0.1\" protocol=doh" }, WindowsDohCommandBuilder.BuildDisable(new("1.1.1.1", "1.0.0.1", null, null)).ToList());
    [TestMethod]
    public void BuildEnable_InvalidAddress_ThrowsBeforeBuildingCommand() =>
        Assert.ThrowsExactly<ArgumentException>(() => WindowsDohCommandBuilder.BuildEnableArguments(new("1.1.1.1 dohtemplate=https://evil.example/dns-query", null, null, null), "https://cloudflare-dns.com/dns-query"));
    [TestMethod]
    public void CollectAddresses_SkipsNullAndWhitespace() =>
        CollectionAssert.AreEqual(new[] { "1.1.1.1", "2606:4700:4700::1001" }, WindowsDohCommandBuilder.CollectAddresses(new("1.1.1.1", "  ", "2606:4700:4700::1001", null)).ToList());
}
