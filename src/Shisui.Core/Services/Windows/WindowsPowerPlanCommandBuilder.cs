using System.Globalization;
using System.Text.RegularExpressions;

namespace Shisui.Core.Services.Windows;

/// <summary>現在の電源プランにある AC/DC 別の省電力設定を読み書きする。</summary>
internal static partial class WindowsPowerPlanCommandBuilder
{
    internal const string PciExpressSubgroupGuid = "501a4d13-42af-4429-9fd1-a8218c268e20";
    internal const string LinkStatePowerManagementGuid = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    internal const string WirelessAdapterSubgroupGuid = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    internal const string WirelessPowerSavingModeGuid = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    internal const string GetActiveSchemeArguments = "/getactivescheme";

    internal static string QueryLinkStateArguments(Guid schemeGuid) =>
        $"/query {schemeGuid:D} {PciExpressSubgroupGuid} {LinkStatePowerManagementGuid}";

    internal static string SetAcLinkStateArguments(Guid schemeGuid, uint value) =>
        $"/setacvalueindex {schemeGuid:D} {PciExpressSubgroupGuid} {LinkStatePowerManagementGuid} {value.ToString(CultureInfo.InvariantCulture)}";

    internal static string ActivateSchemeArguments(Guid schemeGuid) => $"/setactive {schemeGuid:D}";

    internal static string QueryWirelessPowerSavingArguments(Guid schemeGuid) =>
        $"/query {schemeGuid:D} {WirelessAdapterSubgroupGuid} {WirelessPowerSavingModeGuid}";

    internal static string SetAcWirelessPowerSavingArguments(Guid schemeGuid, uint value) =>
        $"/setacvalueindex {schemeGuid:D} {WirelessAdapterSubgroupGuid} {WirelessPowerSavingModeGuid} {value.ToString(CultureInfo.InvariantCulture)}";

    internal static bool TryParseActiveScheme(string output, out Guid schemeGuid)
    {
        schemeGuid = default;
        var matches = GuidRegex().Matches(output ?? string.Empty);
        return matches.Count == 1 && Guid.TryParseExact(matches[0].Value, "D", out schemeGuid);
    }

    internal static bool TryParseLinkStateValues(string output, Guid expectedSchemeGuid, out uint acValue, out uint dcValue)
        => TryParseSettingValues(output, expectedSchemeGuid, PciExpressSubgroupGuid, LinkStatePowerManagementGuid, 2, out acValue, out dcValue);

    internal static bool TryParseWirelessPowerSavingValues(string output, Guid expectedSchemeGuid, out uint acValue, out uint dcValue)
        => TryParseSettingValues(output, expectedSchemeGuid, WirelessAdapterSubgroupGuid, WirelessPowerSavingModeGuid, 3, out acValue, out dcValue);

    private static bool TryParseSettingValues(string output, Guid expectedSchemeGuid, string subgroupGuid,
        string settingGuid, uint maximumValue, out uint acValue, out uint dcValue)
    {
        acValue = 0;
        dcValue = 0;
        var text = output ?? string.Empty;
        var guidMatches = GuidRegex().Matches(text);
        if (guidMatches.Count != 3
            || !Guid.TryParseExact(guidMatches[0].Value, "D", out var schemeGuid)
            || schemeGuid != expectedSchemeGuid
            || !string.Equals(guidMatches[1].Value, subgroupGuid, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(guidMatches[2].Value, settingGuid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var valueMatches = CurrentValueRegex().Matches(text);
        if (valueMatches.Count != 2
            || !uint.TryParse(valueMatches[0].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out acValue)
            || !uint.TryParse(valueMatches[1].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dcValue))
        {
            return false;
        }

        // powercfg /query の対象設定出力は、現在の AC 値、現在の DC 値の順で末尾へ出す。
        // ラベルはローカライズされるため解析せず、上で固定した3 GUID・値の個数・順序を契約にする。
        // ASPM は 0..2、Wi-Fi は SDK の WLAN_POWER_SETTING と同じ 0..3 のみを取る。
        return acValue <= maximumValue && dcValue <= maximumValue;
    }

    [GeneratedRegex(@"(?i)(?<![0-9a-f])[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}(?![0-9a-f])", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"(?i)0x([0-9a-f]{8})(?![0-9a-f])", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentValueRegex();
}
