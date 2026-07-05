using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacNetworkServiceOrderParserTests
{
    private const string SampleOutput =
        "An asterisk (*) denotes that a network service is disabled.\n" +
        "(1) Wi-Fi\n" +
        "(Hardware Port: Wi-Fi, Device: en0)\n" +
        "\n" +
        "(2) Ethernet\n" +
        "(Hardware Port: Ethernet, Device: en1)\n" +
        "\n" +
        "(3) *Thunderbolt Bridge\n" +
        "(Hardware Port: Thunderbolt Bridge, Device: bridge0)\n";

    [TestMethod]
    public void ParseServiceNameToDevice_MapsEachService()
    {
        var map = MacNetworkServiceOrderParser.ParseServiceNameToDevice(SampleOutput);

        Assert.AreEqual("en0", map["Wi-Fi"]);
        Assert.AreEqual("en1", map["Ethernet"]);
    }

    [TestMethod]
    public void ParseServiceNameToDevice_StripsDisabledAsterisk()
    {
        var map = MacNetworkServiceOrderParser.ParseServiceNameToDevice(SampleOutput);

        Assert.IsTrue(map.ContainsKey("Thunderbolt Bridge"));
        Assert.AreEqual("bridge0", map["Thunderbolt Bridge"]);
    }

    [TestMethod]
    public void ParseServiceNameToDevice_EmptyOutput_ReturnsEmptyMap()
    {
        var map = MacNetworkServiceOrderParser.ParseServiceNameToDevice(string.Empty);
        Assert.AreEqual(0, map.Count);
    }
}
