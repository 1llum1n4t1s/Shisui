using System.Text.RegularExpressions;

namespace Shisui.Core.Services.Windows;

public static partial class WindowsAdapterNameNormalizer
{
    public static string GetBaseConnectionName(string name)
    {
        var match = ConnectionOrdinalSuffixRegex().Match(name);
        return match.Success ? match.Groups["base"].Value.TrimEnd() : name;
    }

    [GeneratedRegex(@"^(?<base>.+?)\s+#?\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionOrdinalSuffixRegex();
}
