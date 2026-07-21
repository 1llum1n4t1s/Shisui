using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>診断コマンドのロケール非依存出力を純粋関数で解析する。</summary>
public static class WindowsLegacyNetworkDiagnosticsParser
{
    public static LegacyNetworkAdapterSnapshot ParseAdapterSnapshot(string output)
    {
        var values = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        string? Text(string key) => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
        ulong Number(string key) => values.TryGetValue(key, out var value) &&
            ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;

        DateTime? driverDate = null;
        if (Text("DRIVER_DATE") is { } dateText &&
            DateTime.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            driverDate = parsedDate;
        }

        bool? taskOffloadDisabled = Text("TASK_OFFLOAD_DISABLED") switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };

        return new LegacyNetworkAdapterSnapshot(
            Text("DESCRIPTION"),
            Text("DRIVER_VERSION"),
            driverDate,
            Text("LINK_SPEED"),
            Number("RX_ERRORS"),
            Number("TX_ERRORS"),
            Number("RX_DISCARDS"),
            Number("TX_DISCARDS"),
            Number("RX_PACKETS"),
            Number("TX_PACKETS"),
            taskOffloadDisabled);
    }

    public static bool? ParseWinsockSendAutoTuning(string output)
    {
        if (output.Contains("disabled", StringComparison.OrdinalIgnoreCase)) return false;
        if (output.Contains("enabled", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    public static int ParseProblemDeviceCount(string xml)
    {
        return TryParseProblemDeviceCount(xml, out var count) ? count : 0;
    }

    public static bool TryParseProblemDeviceCount(string xml, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            count = XDocument.Parse(xml).Descendants("Device").Count();
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
