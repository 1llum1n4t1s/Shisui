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
        var name = QuotePowerShellLiteral(adapterName);
        return
            "-NoProfile -NonInteractive -Command \"" +
            "$ErrorActionPreference='Stop';" +
            $"$s=Get-NetAdapterStatistics -Name '{name}';" +
            $"$a=Get-NetAdapter -Name '{name}';" +
            "$rx=[uint64]$s.ReceivedUnicastPackets+[uint64]$s.ReceivedMulticastPackets+[uint64]$s.ReceivedBroadcastPackets;" +
            "$tx=[uint64]$s.SentUnicastPackets+[uint64]$s.SentMulticastPackets+[uint64]$s.SentBroadcastPackets;" +
            "$task=(Get-ItemProperty -LiteralPath 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters' -Name DisableTaskOffload -ErrorAction SilentlyContinue).DisableTaskOffload;" +
            "'DESCRIPTION='+$a.InterfaceDescription;" +
            "'DRIVER_VERSION='+$a.DriverVersion;" +
            "'DRIVER_DATE='+$(if($null -eq $a.DriverDate){''}else{$a.DriverDate.ToString('yyyy-MM-dd')});" +
            "'LINK_SPEED='+$a.LinkSpeed;" +
            "'RX_ERRORS='+[uint64]$s.ReceivedPacketErrors;" +
            "'TX_ERRORS='+[uint64]$s.OutboundPacketErrors;" +
            "'RX_DISCARDS='+[uint64]$s.ReceivedDiscardedPackets;" +
            "'TX_DISCARDS='+[uint64]$s.OutboundDiscardedPackets;" +
            "'RX_PACKETS='+$rx;" +
            "'TX_PACKETS='+$tx;" +
            "'TASK_OFFLOAD_DISABLED='+$(if($null -eq $task){''}else{[int]$task})" +
            "\"";
    }

    public static string BuildResetAdapterAdvancedPropertiesArguments(string adapterName)
    {
        var name = QuotePowerShellLiteral(adapterName);
        return
            "-NoProfile -NonInteractive -Command \"" +
            "$ErrorActionPreference='Stop';" +
            $"Reset-NetAdapterAdvancedProperty -Name '{name}' -DisplayName '*' -Confirm:$false;" +
            $"'RESET={name}'" +
            "\"";
    }

    private static string QuotePowerShellLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("アダプター名が必要です", nameof(value));
        }

        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
