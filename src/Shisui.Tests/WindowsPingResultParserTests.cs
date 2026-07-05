using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    }

    [TestMethod]
    public void Parse_PartialLoss_OnlyAveragesSuccessfulReplies()
    {
        var stdout = "STATUS=0\nRTT=10\nSTATUS=11010\nRTT=0\nSTATUS=0\nRTT=30\n";

        var result = WindowsPingResultParser.Parse(stdout, "1.1.1.1", sent: 3);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(20.0, result.AverageRoundtripMs);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsFailure()
    {
        var result = WindowsPingResultParser.Parse(string.Empty, "unreachable.invalid", sent: 4);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Received);
        Assert.IsNull(result.AverageRoundtripMs);
    }
}
