using System.Text;

namespace Shisui.Tests;

internal static class PowerShellTestHelper
{
    public static string DecodeScript(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var encoded = arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
