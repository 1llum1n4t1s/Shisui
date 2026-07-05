using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacPingResultParserTests
{
    private const string SuccessOutput =
        "PING google.com (142.250.207.14): 56 data bytes\n" +
        "64 bytes from 142.250.207.14: icmp_seq=0 ttl=115 time=12.345 ms\n" +
        "64 bytes from 142.250.207.14: icmp_seq=1 ttl=115 time=11.876 ms\n" +
        "\n" +
        "--- google.com ping statistics ---\n" +
        "2 packets transmitted, 2 packets received, 0.0% packet loss\n" +
        "round-trip min/avg/max/stddev = 11.876/12.111/12.345/0.235 ms\n";

    [TestMethod]
    public void Parse_SuccessOutput_ExtractsReceivedAndAverage()
    {
        var result = MacPingResultParser.Parse(SuccessOutput, "google.com", sent: 2);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(12.111, result.AverageRoundtripMs);
    }

    [TestMethod]
    public void Parse_TotalLoss_ReturnsFailureWithNullAverage()
    {
        const string lossOutput =
            "PING unreachable.invalid (10.0.0.1): 56 data bytes\n" +
            "\n" +
            "--- unreachable.invalid ping statistics ---\n" +
            "4 packets transmitted, 0 packets received, 100.0% packet loss\n";

        var result = MacPingResultParser.Parse(lossOutput, "unreachable.invalid", sent: 4);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Received);
        Assert.IsNull(result.AverageRoundtripMs);
    }
}
