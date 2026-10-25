namespace Shisui.Core.Services.Windows;

/// <summary>既知の標準仕様と確認済みの媒体・ベンダーに限定して、変更可能な NIC プロパティを決定する。</summary>
public static class WindowsGamingNetworkProfilePolicy
{
    public const int EthernetInterfaceType = 6;
    public const int WifiInterfaceType = 71;
    public const string InterruptModerationKeyword = "*InterruptModeration";
    public const string EnergyEfficientEthernetKeyword = "*EEE";
    public const string MediaTekLowPowerKeyword = "LowPowerEnable";
    public const string MediaTekUapsdKeyword = "UAPSDSupport";
    public const string MediaTekAmsduTxKeyword = "AMSDUTx";
    public const string RealtekGreenEthernetKeyword = "EnableGreenEthernet";
    public const string RealtekGigaLiteKeyword = "GigaLite";
    public const string RealtekPowerSavingModeKeyword = "PowerSavingMode";
    public const string FlowControlKeyword = "*FlowControl";
    public const string RealtekAdvancedEeeKeyword = "AdvancedEEE";
    public const string UdpRscKeyword = "*UdpRsc";
    public const string RssKeyword = "*RSS";
    public const string LsoV2IPv4Keyword = "*LsoV2IPv4";
    public const string LsoV2IPv6Keyword = "*LsoV2IPv6";
    public const string IpChecksumOffloadIPv4Keyword = "*IPChecksumOffloadIPv4";
    public const string TcpChecksumOffloadIPv4Keyword = "*TCPChecksumOffloadIPv4";
    public const string TcpChecksumOffloadIPv6Keyword = "*TCPChecksumOffloadIPv6";
    public const string UdpChecksumOffloadIPv4Keyword = "*UDPChecksumOffloadIPv4";
    public const string UdpChecksumOffloadIPv6Keyword = "*UDPChecksumOffloadIPv6";
    public const string MediaTekDriverProvider = "MediaTek, Inc.";
    public const string MediaTekPnpPrefix = "PCI\\VEN_14C3&";
    public const string MediaTekRz616PnpPrefix = "PCI\\VEN_14C3&DEV_0616";
    public const string RealtekDriverProviderToken = "Realtek";
    public const string RealtekPciPnpPrefix = "PCI\\VEN_10EC&";
    public const string RealtekUsbPnpPrefix = "USB\\VID_0BDA&";

    private sealed record PropertyDefinition(string TargetValue, IReadOnlyList<string> KnownValues, bool StandardOffload = false);

    private static readonly IReadOnlyList<string> BinaryValues = Array.AsReadOnly(new[] { "0", "1" });
    private static readonly IReadOnlyList<string> ChecksumValues = Array.AsReadOnly(new[] { "0", "1", "2", "3" });
    private static readonly IReadOnlyDictionary<string, PropertyDefinition> PropertyDefinitions =
        new Dictionary<string, PropertyDefinition>(StringComparer.Ordinal)
        {
            [InterruptModerationKeyword] = new("0", BinaryValues),
            [EnergyEfficientEthernetKeyword] = new("0", BinaryValues),
            [MediaTekLowPowerKeyword] = new("0", BinaryValues),
            [MediaTekUapsdKeyword] = new("0", BinaryValues),
            [MediaTekAmsduTxKeyword] = new("0", BinaryValues),
            [RealtekGreenEthernetKeyword] = new("0", BinaryValues),
            [RealtekGigaLiteKeyword] = new("0", BinaryValues),
            [RealtekPowerSavingModeKeyword] = new("0", BinaryValues),
            [FlowControlKeyword] = new("0", ChecksumValues),
            [RealtekAdvancedEeeKeyword] = new("0", BinaryValues),
            [UdpRscKeyword] = new("0", BinaryValues),
            [RssKeyword] = new("1", BinaryValues, true),
            [LsoV2IPv4Keyword] = new("1", BinaryValues, true),
            [LsoV2IPv6Keyword] = new("1", BinaryValues, true),
            [IpChecksumOffloadIPv4Keyword] = new("3", ChecksumValues, true),
            [TcpChecksumOffloadIPv4Keyword] = new("3", ChecksumValues, true),
            [TcpChecksumOffloadIPv6Keyword] = new("3", ChecksumValues, true),
            [UdpChecksumOffloadIPv4Keyword] = new("3", ChecksumValues, true),
            [UdpChecksumOffloadIPv6Keyword] = new("3", ChecksumValues, true),
        };

    public static IReadOnlyList<string> AllKnownKeywords { get; } =
        Array.AsReadOnly(PropertyDefinitions.Keys.ToArray());

    public static IReadOnlyList<string> StandardOffloadKeywords { get; } =
        Array.AsReadOnly(PropertyDefinitions.Where(pair => pair.Value.StandardOffload).Select(pair => pair.Key).ToArray());

    public static string GetTargetValue(string registryKeyword) => GetDefinition(registryKeyword).TargetValue;

    public static bool IsKnownValue(string registryKeyword, string value) =>
        PropertyDefinitions.TryGetValue(registryKeyword, out var definition) &&
        definition.KnownValues.Contains(value, StringComparer.Ordinal);

    /// <summary>二値項目は従来の両値確認を維持し、列挙値項目は現在値と書込先の対応を必須にする。</summary>
    public static IReadOnlyList<string> GetRequiredSupportedValues(string registryKeyword, string currentValue, string targetValue)
    {
        var definition = GetDefinition(registryKeyword);
        if (!IsKnownValue(registryKeyword, currentValue) || !IsKnownValue(registryKeyword, targetValue))
        {
            throw new ArgumentOutOfRangeException(nameof(currentValue), "NIC プロパティの現在値または書込先が未知の値です");
        }

        return definition.KnownValues.Count == 2
            ? definition.KnownValues
            : new[] { currentValue, targetValue }.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static PropertyDefinition GetDefinition(string registryKeyword) =>
        PropertyDefinitions.TryGetValue(registryKeyword, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(registryKeyword), registryKeyword, "変更対象外の NIC プロパティです");

    public static bool IsSupportedPhysicalAdapter(bool hardwareInterface, int interfaceType) =>
        hardwareInterface && interfaceType is EthernetInterfaceType or WifiInterfaceType;

    public static IReadOnlyList<string> GetApplicableKeywords(
        bool hardwareInterface,
        int interfaceType,
        string driverProvider,
        string pnpDeviceId)
    {
        if (!IsSupportedPhysicalAdapter(hardwareInterface, interfaceType))
        {
            return [];
        }

        if (interfaceType == EthernetInterfaceType)
        {
            return StandardOffloadKeywords.Concat(new[] { UdpRscKeyword }).Concat(IsRealtekEthernet(driverProvider, pnpDeviceId)
                ?
                new[]
                {
                    InterruptModerationKeyword,
                    EnergyEfficientEthernetKeyword,
                    RealtekGreenEthernetKeyword,
                    RealtekGigaLiteKeyword,
                    RealtekPowerSavingModeKeyword,
                }
                : new[] { InterruptModerationKeyword, EnergyEfficientEthernetKeyword })
                .Concat(IsRealtekPciEthernet(pnpDeviceId)
                    ? new[] { FlowControlKeyword, RealtekAdvancedEeeKeyword }
                    : Array.Empty<string>()).ToArray();
        }

        return StandardOffloadKeywords.Concat(new[] { UdpRscKeyword }).Concat(IsVerifiedMediaTekWifi(driverProvider, pnpDeviceId)
            ? new[] { InterruptModerationKeyword, MediaTekLowPowerKeyword, MediaTekUapsdKeyword }
            : new[] { InterruptModerationKeyword })
            .Concat(IsVerifiedMediaTekRz616(driverProvider, pnpDeviceId)
                ? new[] { MediaTekAmsduTxKeyword }
                : Array.Empty<string>()).ToArray();
    }

    public static bool IsKeywordAllowed(
        bool hardwareInterface,
        int interfaceType,
        string driverProvider,
        string pnpDeviceId,
        string registryKeyword) =>
        GetApplicableKeywords(hardwareInterface, interfaceType, driverProvider, pnpDeviceId)
            .Contains(registryKeyword, StringComparer.Ordinal);

    public static bool IsVerifiedMediaTekWifi(string driverProvider, string pnpDeviceId) =>
        string.Equals(driverProvider, MediaTekDriverProvider, StringComparison.Ordinal) &&
        pnpDeviceId.StartsWith(MediaTekPnpPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A-MSDU 送信設定の意味を確認した RZ616 だけを、デバイス ID の境界まで照合する。</summary>
    public static bool IsVerifiedMediaTekRz616(string driverProvider, string pnpDeviceId) =>
        string.Equals(driverProvider, MediaTekDriverProvider, StringComparison.Ordinal) &&
        pnpDeviceId.StartsWith(MediaTekRz616PnpPrefix, StringComparison.OrdinalIgnoreCase) &&
        (pnpDeviceId.Length == MediaTekRz616PnpPrefix.Length ||
         pnpDeviceId[MediaTekRz616PnpPrefix.Length] is '&' or '\\');

    /// <summary>追加のRealtek項目はPCIベンダーIDが確認できる場合に限る。媒体と物理NICは呼出元で確認する。</summary>
    public static bool IsRealtekPciEthernet(string pnpDeviceId) =>
        pnpDeviceId.StartsWith(RealtekPciPnpPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>プロバイダー名またはPCI/USBのベンダーIDでRealtek製と確認できる物理Ethernetを受け入れる。</summary>
    public static bool IsRealtekEthernet(string driverProvider, string pnpDeviceId) =>
        driverProvider.StartsWith(RealtekDriverProviderToken, StringComparison.OrdinalIgnoreCase) ||
        pnpDeviceId.StartsWith(RealtekPciPnpPrefix, StringComparison.OrdinalIgnoreCase) ||
        pnpDeviceId.StartsWith(RealtekUsbPnpPrefix, StringComparison.OrdinalIgnoreCase);
}
