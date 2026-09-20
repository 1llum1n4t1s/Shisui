using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAdapterDetailsCommandBuilderTests
{
    [TestMethod]
    public void BuildArguments_AdapterNameWithQuotes_RemainsLiteralInsideEncodedScript()
    {
        var arguments = WindowsAdapterDetailsCommandBuilder.BuildArguments("ゆろち's \"LAN\"");
        var script = DecodeScript(arguments);

        StringAssert.StartsWith(arguments, "-NoProfile -NonInteractive -EncodedCommand ");
        StringAssert.Contains(script, "Get-NetAdapter -Name '*' -IncludeHidden");
        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'ゆろち''s \"LAN\"',[StringComparison]::OrdinalIgnoreCase)");
        StringAssert.Contains(script, "$a[0] | %{");
        Assert.IsFalse(arguments.Contains("ゆろち", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildArguments_WildcardNameIsResolvedByExactEquality()
    {
        var script = DecodeScript(WindowsAdapterDetailsCommandBuilder.BuildArguments("LAN[1]*"));

        StringAssert.Contains(script, "[string]::Equals([string]$_.Name,'LAN[1]*',[StringComparison]::OrdinalIgnoreCase)");
        Assert.IsFalse(script.Contains("Get-NetAdapter -Name 'LAN[1]*'", StringComparison.Ordinal));
    }

    private static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
