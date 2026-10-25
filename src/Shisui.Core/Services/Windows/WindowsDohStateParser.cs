using System.Net;
using System.Text.Json;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>対象 IP・期待テンプレート・元の bool 型をすべて確認する。</summary>
public static class WindowsDohStateParser
{
    public static DohStatus Parse(string stdout, IReadOnlyList<string> expectedAddresses, string expectedTemplate)
    {
        if (expectedAddresses.Count == 0 || string.IsNullOrWhiteSpace(expectedTemplate))
            return DohStatus.Unknown;
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return DohStatus.Unknown;
            var rows = document.RootElement.EnumerateArray().ToArray();
            if (rows.Length != expectedAddresses.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                return DohStatus.Unknown;
            var enabled = 0;
            foreach (var address in expectedAddresses.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var target = IPAddress.Parse(address);
                var matches = rows.Where(row => row.ValueKind == JsonValueKind.Object &&
                    row.TryGetProperty("ServerAddress", out var server) && server.ValueKind == JsonValueKind.String &&
                    IPAddress.TryParse(server.GetString(), out var parsed) && parsed.Equals(target)).ToArray();
                if (matches.Length != 1)
                    return DohStatus.Unknown;
                var fields = matches[0].EnumerateObject().ToArray();
                if (fields.Length != 4 || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != 4 ||
                    !matches[0].TryGetProperty("DohTemplate", out var template) || template.ValueKind != JsonValueKind.String ||
                    !matches[0].TryGetProperty("AutoUpgrade", out var auto) || auto.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                    !matches[0].TryGetProperty("AllowFallbackToUdp", out var fallback) || fallback.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    return DohStatus.Unknown;
                if (auto.GetBoolean() && string.Equals(template.GetString(), expectedTemplate, StringComparison.Ordinal))
                    enabled++;
            }
            var count = expectedAddresses.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return enabled == count ? DohStatus.Enabled : enabled == 0 ? DohStatus.Disabled : DohStatus.Partial;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return DohStatus.Unknown;
        }
    }
}
