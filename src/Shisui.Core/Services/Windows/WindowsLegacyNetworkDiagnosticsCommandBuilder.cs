namespace Shisui.Core.Services.Windows;

/// <summary>使い込まれた PC 向けネットワーク診断・NIC詳細設定初期化コマンドを組み立てる。</summary>
public static class WindowsLegacyNetworkDiagnosticsCommandBuilder
{
    public const string PowerShellFileName = "powershell";
    public const string WinsockFileName = "netsh";
    public const string WinsockArguments = "winsock show autotuning";
    public const string ProblemDevicesFileName = "pnputil";
    public const string ProblemDevicesArguments = "/enum-devices /problem /class Net /format xml";

    public static string BuildAdapterSnapshotArguments(string adapterName)
    {
        var script =
            "$ErrorActionPreference='Stop';" +
            WindowsPowerShellAdapterSelection.BuildExactLookup(adapterName) +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
            "$s=@(Get-NetAdapterStatistics -Name $escaped -ErrorAction Stop | " +
            "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase)});" +
            "if($s.Count -ne 1){throw 'Adapter statistics resolution was not unique'};" +
            "$rx=[uint64]$s[0].ReceivedUnicastPackets+[uint64]$s[0].ReceivedMulticastPackets+[uint64]$s[0].ReceivedBroadcastPackets;" +
            "$tx=[uint64]$s[0].SentUnicastPackets+[uint64]$s[0].SentMulticastPackets+[uint64]$s[0].SentBroadcastPackets;" +
            "$task=(Get-ItemProperty -LiteralPath 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters' -Name DisableTaskOffload -ErrorAction SilentlyContinue).DisableTaskOffload;" +
            "'DESCRIPTION='+$a[0].InterfaceDescription;" +
            "'DRIVER_VERSION='+$a[0].DriverVersion;" +
            "'DRIVER_DATE='+$(if($null -eq $a[0].DriverDate){''}else{[string]$a[0].DriverDate});" +
            "'LINK_SPEED='+$a[0].LinkSpeed;" +
            "'RX_ERRORS='+[uint64]$s[0].ReceivedPacketErrors;" +
            "'TX_ERRORS='+[uint64]$s[0].OutboundPacketErrors;" +
            "'RX_DISCARDS='+[uint64]$s[0].ReceivedDiscardedPackets;" +
            "'TX_DISCARDS='+[uint64]$s[0].OutboundDiscardedPackets;" +
            "'RX_PACKETS='+$rx;" +
            "'TX_PACKETS='+$tx;" +
            "'TASK_OFFLOAD_DISABLED='+$(if($null -eq $task){''}else{[int]$task})";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    public static string BuildResetAdapterAdvancedPropertiesArguments(string adapterName)
    {
        var script =
            "$ErrorActionPreference='Stop';" +
            WindowsPowerShellAdapterSelection.BuildExactLookup(adapterName) +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
            "Reset-NetAdapterAdvancedProperty -Name $escaped -DisplayName '*' -Confirm:$false -ErrorAction Stop;" +
            "'RESET='+[string]$a[0].Name";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }
}
