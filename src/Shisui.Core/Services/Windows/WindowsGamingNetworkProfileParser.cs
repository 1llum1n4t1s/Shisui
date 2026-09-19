using System.Text.Json;

namespace Shisui.Core.Services.Windows;

/// <summary>ゲーム向け NIC 状態取得コマンドの JSON を、推測せず厳密に解析する。</summary>
public static class WindowsGamingNetworkProfileParser
{
    public static bool TryParse(string output, out GamingNetworkAdapterState? state, out string error)
    {
        state = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            error = "状態取得結果が空です。";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(root,
                    "AdapterName", "InterfaceGuid", "InterfaceDescription",
                    "HardwareInterface", "InterfaceType", "DriverProvider", "PnPDeviceId", "Properties"))
            {
                error = "NIC 状態 JSON の項目が不正または重複しています。";
                return false;
            }

            var adapterName = RequiredString(root, "AdapterName");
            var description = RequiredString(root, "InterfaceDescription");
            var guidText = RequiredString(root, "InterfaceGuid");
            var driverProvider = RequiredString(root, "DriverProvider");
            var pnpDeviceId = RequiredString(root, "PnPDeviceId");
            var interfaceTypeElement = root.GetProperty("InterfaceType");
            if (adapterName is null || description is null || guidText is null ||
                driverProvider is null || pnpDeviceId is null ||
                !Guid.TryParse(guidText, out var guid) || guid == Guid.Empty ||
                root.GetProperty("HardwareInterface").ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                interfaceTypeElement.ValueKind != JsonValueKind.Number ||
                !interfaceTypeElement.TryGetInt32(out var interfaceType) ||
                root.GetProperty("Properties").ValueKind != JsonValueKind.Array)
            {
                error = "NIC の識別情報が不正です。";
                return false;
            }

            var properties = new List<GamingNetworkAdapterPropertyState>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("Properties").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !HasExactProperties(item, "RegistryKeyword", "RegistryValues", "ValidRegistryValues"))
                {
                    error = "NIC 詳細プロパティ JSON の項目が不正または重複しています。";
                    return false;
                }

                var keyword = RequiredString(item, "RegistryKeyword");
                if (keyword is null ||
                    !WindowsGamingNetworkProfileCommandBuilder.AllowedKeywords.Contains(keyword, StringComparer.Ordinal) ||
                    !WindowsGamingNetworkProfilePolicy.IsKeywordAllowed(
                        root.GetProperty("HardwareInterface").GetBoolean(),
                        interfaceType,
                        driverProvider,
                        pnpDeviceId,
                        keyword) ||
                    !seen.Add(keyword))
                {
                    error = "NIC 詳細プロパティが対象外または重複しています。";
                    return false;
                }

                if (!TryGetStringArray(item, "RegistryValues", out var currentValues) || currentValues.Count != 1 ||
                    currentValues[0] is not ("0" or "1") ||
                    !TryGetStringArray(item, "ValidRegistryValues", out var validValues) ||
                    validValues.Count != validValues.Distinct(StringComparer.Ordinal).Count() ||
                    !validValues.Contains("0", StringComparer.Ordinal) ||
                    !validValues.Contains("1", StringComparer.Ordinal))
                {
                    error = $"{keyword} の現在値または対応値が不正です。";
                    return false;
                }

                properties.Add(new GamingNetworkAdapterPropertyState(keyword, currentValues[0]));
            }

            state = new GamingNetworkAdapterState(
                adapterName,
                guid,
                description,
                root.GetProperty("HardwareInterface").GetBoolean(),
                interfaceType,
                driverProvider,
                pnpDeviceId,
                properties);
            return true;
        }
        catch (JsonException)
        {
            error = "NIC 状態 JSON を解析できません。";
            return false;
        }
    }

    private static string? RequiredString(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }

    private static bool TryGetStringArray(JsonElement owner, string name, out List<string> values)
    {
        values = [];
        var element = owner.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } value)
            {
                return false;
            }

            values.Add(value);
        }

        return true;
    }

    private static bool HasExactProperties(JsonElement element, params string[] expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.SetEquals(expected);
    }
}

public sealed record GamingNetworkAdapterState(
    string AdapterName,
    Guid InterfaceGuid,
    string InterfaceDescription,
    bool HardwareInterface,
    int InterfaceType,
    string DriverProvider,
    string PnpDeviceId,
    IReadOnlyList<GamingNetworkAdapterPropertyState> Properties);

public sealed record GamingNetworkAdapterPropertyState(string RegistryKeyword, string CurrentValue);
