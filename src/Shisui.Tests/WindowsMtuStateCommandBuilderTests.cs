using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsMtuStateCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsAdapterName()
    {
        var args = WindowsMtuStateCommandBuilder.BuildArguments("Wi-Fi 2");

        Assert.IsTrue(args.Contains("-InterfaceAlias 'Wi-Fi 2'"));
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInAdapterName()
    {
        var args = WindowsMtuStateCommandBuilder.BuildArguments("evil'; Remove-Item C:\\");

        Assert.IsTrue(args.Contains("evil''; Remove-Item C:\\"));
    }
}
