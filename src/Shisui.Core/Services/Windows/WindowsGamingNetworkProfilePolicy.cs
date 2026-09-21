namespace Shisui.Core.Services.Windows;

/// <summary>実機で確認済みの媒体・ベンダーに限定して、変更可能な NIC プロパティを決定する。</summary>
public static class WindowsGamingNetworkProfilePolicy
{
    public const int EthernetInterfaceType = 6;
    public const int WifiInterfaceType = 71;
    public const string InterruptModerationKeyword = "*InterruptModeration";
    public const string EnergyEfficientEthernetKeyword = "*EEE";
    public const string MediaTekLowPowerKeyword = "LowPowerEnable";
    public const string MediaTekUapsdKeyword = "UAPSDSupport";
    public const string RealtekGreenEthernetKeyword = "EnableGreenEthernet";
    public const string RealtekGigaLiteKeyword = "GigaLite";
    public const string RealtekPowerSavingModeKeyword = "PowerSavingMode";
    public const string MediaTekDriverProvider = "MediaTek, Inc.";
    public const string MediaTekPnpPrefix = "PCI\\VEN_14C3&";
    public const string RealtekDriverProviderToken = "Realtek";
    public const string RealtekPciPnpPrefix = "PCI\\VEN_10EC&";
    public const string RealtekUsbPnpPrefix = "USB\\VID_0BDA&";

    public static IReadOnlyList<string> AllKnownKeywords { get; } =
    [
        InterruptModerationKeyword,
        EnergyEfficientEthernetKeyword,
        MediaTekLowPowerKeyword,
        MediaTekUapsdKeyword,
        RealtekGreenEthernetKeyword,
        RealtekGigaLiteKeyword,
        RealtekPowerSavingModeKeyword,
    ];

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
            return IsRealtekEthernet(driverProvider, pnpDeviceId)
                ?
                [
                    InterruptModerationKeyword,
                    EnergyEfficientEthernetKeyword,
                    RealtekGreenEthernetKeyword,
                    RealtekGigaLiteKeyword,
                    RealtekPowerSavingModeKeyword,
                ]
                : [InterruptModerationKeyword, EnergyEfficientEthernetKeyword];
        }

        return IsVerifiedMediaTekWifi(driverProvider, pnpDeviceId)
            ? [InterruptModerationKeyword, MediaTekLowPowerKeyword, MediaTekUapsdKeyword]
            : [InterruptModerationKeyword];
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

    /// <summary>プロバイダー名またはPCI/USBのベンダーIDでRealtek製と確認できる物理Ethernetを受け入れる。</summary>
    public static bool IsRealtekEthernet(string driverProvider, string pnpDeviceId) =>
        driverProvider.StartsWith(RealtekDriverProviderToken, StringComparison.OrdinalIgnoreCase) ||
        pnpDeviceId.StartsWith(RealtekPciPnpPrefix, StringComparison.OrdinalIgnoreCase) ||
        pnpDeviceId.StartsWith(RealtekUsbPnpPrefix, StringComparison.OrdinalIgnoreCase);
}
