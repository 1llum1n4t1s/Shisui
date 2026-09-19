using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsMtuStateCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_ContainsAdapterName()
    {
        var script = DecodeScript(WindowsMtuStateCommandBuilder.BuildArguments("Wi-Fi 2"));

        StringAssert.Contains(script, "-InterfaceAlias 'Wi-Fi 2'");
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInAdapterName()
    {
        var script = DecodeScript(WindowsMtuStateCommandBuilder.BuildArguments("evil'; Remove-Item C:\\"));

        StringAssert.Contains(script, "evil''; Remove-Item C:\\");
    }

    [TestMethod]
    public void BuildArguments_DoubleQuoteInAdapterName_RemainsInsideEncodedScript()
    {
        var arguments = WindowsMtuStateCommandBuilder.BuildArguments("Wi-\"Fi");
        var script = DecodeScript(arguments);

        StringAssert.StartsWith(arguments, "-NoProfile -NonInteractive -EncodedCommand ");
        StringAssert.Contains(script, "-InterfaceAlias 'Wi-\"Fi'");
        Assert.IsFalse(arguments.Contains("Wi-\"Fi", StringComparison.Ordinal));
    }

    private static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
