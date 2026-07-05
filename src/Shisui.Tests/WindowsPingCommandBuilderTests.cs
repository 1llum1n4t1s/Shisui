using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsPingCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsHostAndCount()
    {
        var args = WindowsPingCommandBuilder.BuildArguments("1.1.1.1", 4);

        Assert.IsTrue(args.Contains("-ComputerName '1.1.1.1'"));
        Assert.IsTrue(args.Contains("-Count 4"));
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInHost()
    {
        // シングルクォートを二重化してリテラル文字列の外へ抜け出せないようにする
        var args = WindowsPingCommandBuilder.BuildArguments("evil'; Remove-Item C:\\", 1);

        Assert.IsTrue(args.Contains("evil''; Remove-Item C:\\"));
    }
}
