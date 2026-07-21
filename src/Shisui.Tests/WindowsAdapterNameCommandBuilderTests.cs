using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAdapterNameCommandBuilderTests
{
    [TestMethod]
    public void QueryArguments_EnumeratesVisibleAndHiddenAdaptersWithStableFields()
    {
        var script = DecodeScript(WindowsAdapterNameCommandBuilder.QueryArguments);

        StringAssert.Contains(script, "Get-NetAdapter -Name '*' -IncludeHidden");
        StringAssert.Contains(script, "$_.Name");
    }

    [TestMethod]
    public void BuildRenameArguments_QuotesNamesInsideEncodedPowerShellScript()
    {
        var arguments = WindowsAdapterNameCommandBuilder.BuildRenameArguments(
            "ゆろち's LAN 3",
            "ゆろち's LAN");
        var script = DecodeScript(arguments);

        StringAssert.Contains(script, "Rename-NetAdapter");
        StringAssert.Contains(script, "[string]::Equals($_.Name,'ゆろち''s LAN 3'");
        StringAssert.Contains(script, "-NewName 'ゆろち''s LAN'");
        StringAssert.Contains(script, "-PassThru");
        StringAssert.Contains(script, "-Confirm:$false");
    }

    private static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
