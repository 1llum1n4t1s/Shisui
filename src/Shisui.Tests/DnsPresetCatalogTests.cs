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
