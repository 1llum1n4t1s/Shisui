using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;

namespace Shisui.Tests;

[TestClass]
public class DnsPresetCatalogTests
{
    [TestMethod]
    public void CloudflareStandard_HasOfficialAddresses()
    {
        var servers = DnsPresetCatalog.CloudflareStandard.Servers;
        Assert.AreEqual("1.1.1.1", servers.Ipv4Primary);
        Assert.AreEqual("1.0.0.1", servers.Ipv4Secondary);
        Assert.AreEqual("2606:4700:4700::1111", servers.Ipv6Primary);
        Assert.AreEqual("2606:4700:4700::1001", servers.Ipv6Secondary);
    }

    [TestMethod]
    public void CloudflareMalwareBlock_HasOfficialAddresses()
    {
        var servers = DnsPresetCatalog.CloudflareMalwareBlock.Servers;
        Assert.AreEqual("1.1.1.2", servers.Ipv4Primary);
        Assert.AreEqual("1.0.0.2", servers.Ipv4Secondary);
        Assert.AreEqual("2606:4700:4700::1112", servers.Ipv6Primary);
        Assert.AreEqual("2606:4700:4700::1002", servers.Ipv6Secondary);
    }

    [TestMethod]
    public void CloudflareMalwareAdultBlock_HasOfficialAddresses()
    {
        var servers = DnsPresetCatalog.CloudflareMalwareAdultBlock.Servers;
        Assert.AreEqual("1.1.1.3", servers.Ipv4Primary);
        Assert.AreEqual("1.0.0.3", servers.Ipv4Secondary);
        Assert.AreEqual("2606:4700:4700::1113", servers.Ipv6Primary);
        Assert.AreEqual("2606:4700:4700::1003", servers.Ipv6Secondary);
    }

    [TestMethod]
    public void GooglePublicDns_HasOfficialAddresses()
    {
        var servers = DnsPresetCatalog.GooglePublicDns.Servers;
        Assert.AreEqual("8.8.8.8", servers.Ipv4Primary);
        Assert.AreEqual("8.8.4.4", servers.Ipv4Secondary);
        Assert.AreEqual("2001:4860:4860::8888", servers.Ipv6Primary);
        Assert.AreEqual("2001:4860:4860::8844", servers.Ipv6Secondary);
    }

    [TestMethod]
    public void Quad9_HasOfficialAddressesAndEncryptionTemplates()
    {
        var preset = DnsPresetCatalog.Quad9;
        Assert.AreEqual("9.9.9.9", preset.Servers.Ipv4Primary);
        Assert.AreEqual("149.112.112.112", preset.Servers.Ipv4Secondary);
        Assert.AreEqual("2620:fe::fe", preset.Servers.Ipv6Primary);
        Assert.AreEqual("2620:fe::9", preset.Servers.Ipv6Secondary);
        Assert.AreEqual("https://dns.quad9.net/dns-query", preset.DohTemplate);
        Assert.AreEqual("dns.quad9.net", preset.DotHost);
    }

    [TestMethod]
    public void NextDns_HasLinkedIpAddresses_AndNoEncryptionTemplates()
    {
        var preset = DnsPresetCatalog.NextDns;
        Assert.AreEqual("45.90.28.0", preset.Servers.Ipv4Primary);
        Assert.AreEqual("45.90.30.0", preset.Servers.Ipv4Secondary);
        Assert.AreEqual("2a07:a8c0::", preset.Servers.Ipv6Primary);
        Assert.AreEqual("2a07:a8c1::", preset.Servers.Ipv6Secondary);
        // Linked IP はアカウント別サブドメインが必要なため DoH/DoT は固定値で表現できず非対応。
        Assert.IsNull(preset.DohTemplate);
        Assert.IsNull(preset.DotHost);
    }

    [TestMethod]
    public void CloudflareTiers_HaveDistinctDotHostsMatchingTheirDohTier()
    {
        // DoH と同じ「ティアごとにホスト名が異なる」罠 (security./family. サブドメイン) が
        // DoT にもそのまま存在するため、標準ホストの使い回しになっていないことを確認する。
        Assert.AreEqual("cloudflare-dns.com", DnsPresetCatalog.CloudflareStandard.DotHost);
        Assert.AreEqual("security.cloudflare-dns.com", DnsPresetCatalog.CloudflareMalwareBlock.DotHost);
        Assert.AreEqual("family.cloudflare-dns.com", DnsPresetCatalog.CloudflareMalwareAdultBlock.DotHost);
        Assert.AreEqual("dns.google", DnsPresetCatalog.GooglePublicDns.DotHost);
    }

    [TestMethod]
    public void Custom_HasNoPresetAddresses()
    {
        Assert.IsTrue(DnsPresetCatalog.Custom.Servers.IsEmpty);
    }

    [TestMethod]
    public void BuiltIn_HasUniqueIds()
    {
        var ids = DnsPresetCatalog.BuiltIn.Select(p => p.Id).ToList();
        CollectionAssert.AllItemsAreUnique(ids);
    }
}
