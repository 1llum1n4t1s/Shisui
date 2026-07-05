using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsTraceRouteParserTests
{
    [TestMethod]
    public void ParseHopAddresses_ReturnsInOrder()
    {
        var stdout = "HOP=192.168.1.1\nHOP=10.0.0.1\nHOP=142.250.207.14\n";

        var hops = WindowsTraceRouteParser.ParseHopAddresses(stdout);

        CollectionAssert.AreEqual(new[] { "192.168.1.1", "10.0.0.1", "142.250.207.14" }, hops.ToList());
    }

    [TestMethod]
    public void ParseHopAddresses_EmptyOutput_ReturnsEmpty()
    {
        var hops = WindowsTraceRouteParser.ParseHopAddresses(string.Empty);

        Assert.AreEqual(0, hops.Count);
    }
}
