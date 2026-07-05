using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAdapterDetailsParserTests
{
    [TestMethod]
    public void Parse_FullOutput_ExtractsAllFields()
    {
        const string stdout = "MAC=AC-DE-48-00-11-22\nLINKSPEED=1 Gbps\nMEDIATYPE=802.3\nSTATUS=Up";

        var details = WindowsAdapterDetailsParser.Parse(stdout, "Ethernet");

        Assert.IsNotNull(details);
        Assert.AreEqual("Ethernet", details.Id);
        Assert.AreEqual("AC-DE-48-00-11-22", details.MacAddress);
        Assert.AreEqual("1 Gbps", details.LinkSpeedText);
        Assert.AreEqual("802.3", details.MediaType);
        Assert.IsTrue(details.IsUp);
    }

    [TestMethod]
    public void Parse_DownStatus_IsUpFalse()
    {
        const string stdout = "MAC=AC-DE-48-00-11-22\nLINKSPEED=\nMEDIATYPE=802.3\nSTATUS=Disconnected";

        var details = WindowsAdapterDetailsParser.Parse(stdout, "Ethernet");

        Assert.IsNotNull(details);
        Assert.IsFalse(details.IsUp);
        Assert.IsNull(details.LinkSpeedText);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsNull()
    {
        Assert.IsNull(WindowsAdapterDetailsParser.Parse(string.Empty, "Ethernet"));
    }
}
