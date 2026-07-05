using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsTraceRouteCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsHostAndHops()
    {
        var args = WindowsTraceRouteCommandBuilder.BuildArguments("example.com", 20);

        Assert.IsTrue(args.Contains("-ComputerName 'example.com'"));
        Assert.IsTrue(args.Contains("-Hops 20"));
        Assert.IsTrue(args.Contains("-TraceRoute"));
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInHost()
    {
        var args = WindowsTraceRouteCommandBuilder.BuildArguments("evil'; Remove-Item C:\\", 10);

        Assert.IsTrue(args.Contains("evil''; Remove-Item C:\\"));
    }
}
