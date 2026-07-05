using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacIfConfigParserTests
{
    private const string ActiveOutput =
        "en0: flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500\n" +
        "\toptions=6463<RXCSUM,TXCSUM,TSO4,TSO6,CHANNEL_IO,PARTIAL_CSUM,ZEROINVERT_CSUM>\n" +
        "\tether ac:de:48:00:11:22\n" +
        "\tinet 192.168.1.5 netmask 0xffffff00 broadcast 192.168.1.255\n" +
        "\tmedia: autoselect (1000baseT <full-duplex>)\n" +
        "\tstatus: active\n";

    [TestMethod]
    public void Parse_ActiveInterface_ExtractsMacMediaAndStatus()
    {
        var details = MacIfConfigParser.Parse(ActiveOutput, "Wi-Fi");

        Assert.IsNotNull(details);
        Assert.AreEqual("ac:de:48:00:11:22", details.MacAddress);
        Assert.AreEqual("autoselect (1000baseT <full-duplex>)", details.LinkSpeedText);
        Assert.IsTrue(details.IsUp);
    }

    [TestMethod]
    public void Parse_InactiveInterface_IsUpFalse()
    {
        const string inactiveOutput =
            "en1: flags=8822<BROADCAST,SMART,SIMPLEX,MULTICAST> mtu 1500\n" +
            "\tether ac:de:48:00:33:44\n" +
            "\tstatus: inactive\n";

        var details = MacIfConfigParser.Parse(inactiveOutput, "Ethernet");

        Assert.IsNotNull(details);
        Assert.IsFalse(details.IsUp);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsNull()
    {
        Assert.IsNull(MacIfConfigParser.Parse(string.Empty, "Wi-Fi"));
    }
}
