using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.MacOS;

namespace Shisui.Tests;

[TestClass]
public class MacPingCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsCountAndQuotedHost()
    {
        var args = MacPingCommandBuilder.BuildArguments("1.1.1.1", 4);

        Assert.AreEqual("-c 4 \"1.1.1.1\"", args);
    }
}
