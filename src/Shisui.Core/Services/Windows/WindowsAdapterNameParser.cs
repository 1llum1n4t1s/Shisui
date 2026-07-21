using System.Text;

namespace Shisui.Core.Services.Windows;

internal sealed record WindowsAdapterNameRecord(string Name);

internal static class WindowsAdapterNameParser
{
    public static IReadOnlyList<WindowsAdapterNameRecord> Parse(string output)
    {
        List<WindowsAdapterNameRecord> records = [];
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line == "BEGIN")
            {
                values.Clear();
                continue;
            }

            if (line == "END")
            {
                if (values.TryGetValue("NAME", out var encodedName)
                    && TryDecode(encodedName, out var name)
                    && !string.IsNullOrWhiteSpace(name))
                {
                    records.Add(new WindowsAdapterNameRecord(name));
                }

                values.Clear();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator]] = line[(separator + 1)..];
            }
        }

        return records;
    }

    private static bool TryDecode(string? value, out string? decoded)
    {
        decoded = null;
        if (value is null)
        {
            return false;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
