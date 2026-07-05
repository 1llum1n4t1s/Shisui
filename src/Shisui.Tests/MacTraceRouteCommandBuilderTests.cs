using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacTraceRouteCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsMaxHopsAndQuotedHost()
    {
        var args = MacTraceRouteCommandBuilder.BuildArguments("example.com", 30);

        Assert.AreEqual("-n -q 1 -m 30 \"example.com\"", args);
    }
}
