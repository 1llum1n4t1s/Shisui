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

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Wi-Fi 2',[StringComparison]::OrdinalIgnoreCase)");
        StringAssert.Contains(script, "Get-NetIPInterface -InterfaceIndex ([uint32]$a[0].ifIndex)");
    }

    [TestMethod]
    public void BuildArguments_EscapesSingleQuoteInAdapterName()
    {
        var script = DecodeScript(WindowsMtuStateCommandBuilder.BuildArguments("evil'; Remove-Item C:\\"));

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'evil''; Remove-Item C:\\',[StringComparison]::OrdinalIgnoreCase)");
    }

    [TestMethod]
    public void BuildArguments_DoubleQuoteInAdapterName_RemainsInsideEncodedScript()
    {
        var arguments = WindowsMtuStateCommandBuilder.BuildArguments("Wi-\"Fi");
        var script = DecodeScript(arguments);

        StringAssert.StartsWith(arguments, "-NoProfile -NonInteractive -EncodedCommand ");
        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Wi-\"Fi',[StringComparison]::OrdinalIgnoreCase)");
        Assert.IsFalse(script.Contains("-InterfaceAlias", StringComparison.Ordinal));
        Assert.IsFalse(arguments.Contains("Wi-\"Fi", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildArguments_WildcardNameUsesExactAdapterThenNumericIndex()
    {
        var script = DecodeScript(WindowsMtuStateCommandBuilder.BuildArguments("Wi-*"));

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'Wi-*',[StringComparison]::OrdinalIgnoreCase)");
        StringAssert.Contains(script, "-InterfaceIndex ([uint32]$a[0].ifIndex)");
        Assert.IsFalse(script.Contains("-InterfaceAlias 'Wi-*'", StringComparison.Ordinal));
    }

    private static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
