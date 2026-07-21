namespace Shisui.Core.Models;

public enum LegacyNetworkRepairPath
{
    None,
    QuickOptimization,
    GhostAdapters,
    DriverUpdate,
    NicAdvancedPropertiesReset,
}

public sealed record LegacyNetworkDiagnosticFinding(
    string Title,
    string Detail,
    bool IsWarning,
    LegacyNetworkRepairPath RepairPath = LegacyNetworkRepairPath.None)
{
    public string Icon => IsWarning ? "⚠" : "✅";

    public bool HasAction => RepairPath != LegacyNetworkRepairPath.None;

    public string ActionText => RepairPath switch
    {
        LegacyNetworkRepairPath.QuickOptimization =>
            "「おまかせ高速化設定」で修復できます",
        LegacyNetworkRepairPath.GhostAdapters =>
            "DNS設定タブの「切断済みネットワークデバイス」で確認・削除できます",
        LegacyNetworkRepairPath.DriverUpdate =>
            "PCまたはNICメーカーのサポートページで最新版を確認してください",
        LegacyNetworkRepairPath.NicAdvancedPropertiesReset =>
            "必要なら下のボタンで、選択中NICの詳細設定を工場出荷値へ戻せます",
        _ => string.Empty,
    };
}

public sealed record LegacyNetworkAdapterSnapshot(
    string? Description,
    string? DriverVersion,
    DateTime? DriverDate,
    string? LinkSpeed,
    ulong ReceivedPacketErrors,
    ulong OutboundPacketErrors,
    ulong ReceivedDiscardedPackets,
    ulong OutboundDiscardedPackets,
    ulong ReceivedPackets,
    ulong SentPackets,
    bool? TaskOffloadDisabled);

public sealed record LegacyNetworkDiagnosticsReport(
    string AdapterName,
    LegacyNetworkAdapterSnapshot? Adapter,
    bool? WinsockSendAutoTuningEnabled,
    int ProblemDeviceCount,
    int GhostAdapterCount,
    IReadOnlyList<LegacyNetworkDiagnosticFinding> Findings)
{
    public bool HasWarnings => Findings.Any(f => f.IsWarning);

    public bool RecommendNicReset => Findings.Any(
        f => f.RepairPath == LegacyNetworkRepairPath.NicAdvancedPropertiesReset);
}
