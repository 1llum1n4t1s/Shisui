using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsPingResultParserTests
{
    [TestMethod]
    public void Parse_AllSucceed_ComputesAverageAndReceivedCount()
    {
        var stdout = "STATUS=0\nRTT=10\nSTATUS=0\nRTT=20\nSTATUS=0\nRTT=30\n";

        var result = WindowsPingResultParser.Parse(stdout, "1.1.1.1", sent: 3);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, result.Sent);
        Assert.AreEqual(3, result.Received);
        Assert.AreEqual(20.0, result.AverageRoundtripMs);
        Assert.AreEqual(10.0, result.MinimumRoundtripMs);
        Assert.AreEqual(30.0, result.MaximumRoundtripMs);
        Assert.AreEqual(30.0, result.P95RoundtripMs);
        Assert.AreEqual(10.0, result.JitterMs);
        Assert.AreEqual(0.0, result.LossPercent);
    }

    [TestMethod]
    public void Parse_PartialLoss_OnlyAveragesSuccessfulReplies()
    {
        var stdout = "STATUS=0\nRTT=10\nSTATUS=11010\nRTT=0\nSTATUS=0\nRTT=30\n";

        var result = WindowsPingResultParser.Parse(stdout, "1.1.1.1", sent: 3);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(20.0, result.AverageRoundtripMs);
        Assert.AreEqual(100.0 / 3, result.LossPercent!.Value, 0.000_001);
        Assert.IsNull(result.JitterMs);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsFailure()
    {
        var result = WindowsPingResultParser.Parse(string.Empty, "unreachable.invalid", sent: 4);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Received);
        Assert.IsNull(result.AverageRoundtripMs);
        Assert.IsNull(result.MinimumRoundtripMs);
        Assert.IsNull(result.MaximumRoundtripMs);
        Assert.IsNull(result.P95RoundtripMs);
        Assert.IsNull(result.JitterMs);
        Assert.AreEqual(100.0, result.LossPercent);
    }

    [TestMethod]
    public void Parse_NearestRankP95_IsDeterministic()
    {
        var stdout = string.Join(
            '\n',
            Enumerable.Range(1, 20).SelectMany(value => new[] { "STATUS=0", $"RTT={value}" }));

        var result = WindowsPingResultParser.Parse(stdout, "example.com", sent: 20);

        Assert.AreEqual(19.0, result.P95RoundtripMs);
    }

    [TestMethod]
    public void Parse_FailedGap_DoesNotBridgeJitter()
    {
        const string stdout =
            "STATUS=0\nRTT=10\n" +
            "STATUS=0\nRTT=14\n" +
            "STATUS=11010\nRTT=\n" +
            "STATUS=0\nRTT=40\n" +
            "STATUS=0\nRTT=46\n";

        var result = WindowsPingResultParser.Parse(stdout, "example.com", sent: 5);

        Assert.AreEqual(4, result.Received);
        Assert.AreEqual(5.0, result.JitterMs);
    }

    [TestMethod]
    public void Parse_MalformedNegativeAndNonFiniteRtt_DoNotCountAsReplies()
    {
        const string stdout =
            "STATUS=0\nRTT=1,5\n" +
            "STATUS=0\nRTT=-1\n" +
            "STATUS=0\nRTT=NaN\n" +
            "STATUS=0\nRTT=Infinity\n" +
            "STATUS=0\nRTT=2.5\n";

        var result = WindowsPingResultParser.Parse(stdout, "example.com", sent: 5);

        Assert.AreEqual(1, result.Received);
        Assert.AreEqual(2.5, result.AverageRoundtripMs);
        Assert.AreEqual(80.0, result.LossPercent);
        Assert.IsNull(result.JitterMs);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Parse_UsesInvariantCultureForRtt()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var result = WindowsPingResultParser.Parse("STATUS=0\nRTT=1.5\n", "example.com", sent: 1);

            Assert.AreEqual(1, result.Received);
            Assert.AreEqual(1.5, result.AverageRoundtripMs);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    public void Parse_MissingRtt_DoesNotBridgeJitter()
    {
        const string stdout = "STATUS=0\nRTT=10\nSTATUS=0\nSTATUS=0\nRTT=20\n";

        var result = WindowsPingResultParser.Parse(stdout, "example.com", sent: 3);

        Assert.AreEqual(2, result.Received);
        Assert.IsNull(result.JitterMs);
    }

    [TestMethod]
    public void Parse_ExtraSamples_AreIgnoredBeyondSentCount()
    {
        const string stdout = "STATUS=0\nRTT=10\nSTATUS=0\nRTT=20\nSTATUS=0\nRTT=30\n";

        var result = WindowsPingResultParser.Parse(stdout, "example.com", sent: 2);

        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(0.0, result.LossPercent);
        Assert.AreEqual(15.0, result.AverageRoundtripMs);
    }

    [TestMethod]
    public void LossPercent_NoSentProbes_IsNull()
    {
        var result = new PingResult(false, "example.com", 0, 0, null, string.Empty);

        Assert.IsNull(result.LossPercent);
    }
}
