using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacTraceRouteParserTests
{
    [TestMethod]
    public void Parse_MixOfRepliesAndTimeouts_ParsesEachHop()
    {
        var stdout =
            "traceroute to google.com (142.250.207.14), 30 hops max, 60 byte packets\n" +
            " 1  192.168.1.1  1.234 ms\n" +
            " 2  10.0.0.1  5.678 ms\n" +
            " 3  *\n" +
            " 4  142.250.207.14  12.345 ms\n";

        var hops = MacTraceRouteParser.Parse(stdout);

        Assert.AreEqual(4, hops.Count);

        Assert.AreEqual(1, hops[0].HopNumber);
        Assert.AreEqual("192.168.1.1", hops[0].Address);
        Assert.AreEqual(1.234, hops[0].RoundtripMs);

        Assert.AreEqual(3, hops[2].HopNumber);
        Assert.IsNull(hops[2].Address);
        Assert.IsNull(hops[2].RoundtripMs);

        Assert.AreEqual(4, hops[3].HopNumber);
        Assert.AreEqual("142.250.207.14", hops[3].Address);
        Assert.AreEqual(12.345, hops[3].RoundtripMs);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        var hops = MacTraceRouteParser.Parse(string.Empty);

        Assert.AreEqual(0, hops.Count);
    }
}
