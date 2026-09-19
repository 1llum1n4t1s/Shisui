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

        Assert.IsTrue(args.Contains("$ping.Send('1.1.1.1',1000)"));
        Assert.IsTrue(args.Contains("$i -lt 4"));
        Assert.IsTrue(args.Contains("$delay=1000-[int]$probe.ElapsedMilliseconds"));
        Assert.IsTrue(args.Contains("Start-Sleep -Milliseconds $delay"));
        Assert.IsTrue(args.Contains("OutputEncoding=$utf8"));
        Assert.IsTrue(args.Contains("InvariantCulture"));
        Assert.IsTrue(args.Contains("catch{'STATUS=-1';'RTT='}"));
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInHost()
    {
        // シングルクォートを二重化してリテラル文字列の外へ抜け出せないようにする
        var args = WindowsPingCommandBuilder.BuildArguments("evil'; Remove-Item C:\\", 1);

        Assert.IsTrue(args.Contains("evil''; Remove-Item C:\\"));
    }

    [TestMethod]
    public void BuildArguments_HostContainsDoubleQuote_Throws()
    {
        // 生の " は外側の -Command "..." の引用符コンテキストを破り、後続テキストが別コマンドとして
        // 注入される (2026-07-06 /rere レビューで発見)。シングルクオートの二重化では防げないため拒否する。
        Assert.ThrowsExactly<ArgumentException>(() =>
            WindowsPingCommandBuilder.BuildArguments("x\" ; Start-Process calc ; \"", 4));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(101)]
    public void BuildArguments_CountOutsideBounds_Throws(int count)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsPingCommandBuilder.BuildArguments("1.1.1.1", count));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(100)]
    public void BuildArguments_CountAtBounds_IsAccepted(int count)
    {
        var args = WindowsPingCommandBuilder.BuildArguments("1.1.1.1", count);

        Assert.IsTrue(args.Contains($"$i -lt {count}"));
    }
}
