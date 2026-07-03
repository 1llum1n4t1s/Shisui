using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacDnsCommandBuilderTests
{
    [TestMethod]
    public void BuildApply_MixesIpv4AndIpv6InSingleOrderedList()
    {
        var servers = new DnsServerSet("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001");
        var args = MacDnsCommandBuilder.BuildApply("Wi-Fi", servers);

        Assert.AreEqual("-setdnsservers \"Wi-Fi\" 1.1.1.1 1.0.0.1 2606:4700:4700::1111 2606:4700:4700::1001", args);
    }

    [TestMethod]
    public void BuildApply_Ipv4Only_OmitsIpv6Addresses()
    {
        var servers = new DnsServerSet("8.8.8.8", "8.8.4.4", null, null);
        var args = MacDnsCommandBuilder.BuildApply("Ethernet", servers);

        Assert.AreEqual("-setdnsservers \"Ethernet\" 8.8.8.8 8.8.4.4", args);
    }

    [TestMethod]
    public void BuildResetToAutomatic_UsesEmptyKeyword()
    {
        Assert.AreEqual("-setdnsservers \"Wi-Fi\" Empty", MacDnsCommandBuilder.BuildResetToAutomatic("Wi-Fi"));
    }

    [TestMethod]
    public void BuildGetCurrent_QuotesServiceName()
    {
        Assert.AreEqual("-getdnsservers \"USB 10/100/1000 LAN\"", MacDnsCommandBuilder.BuildGetCurrent("USB 10/100/1000 LAN"));
    }
}
