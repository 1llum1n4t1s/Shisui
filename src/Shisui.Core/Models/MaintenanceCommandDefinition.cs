namespace Shisui.Core.Models;

/// <summary>
/// 「任意実行」メンテナンスコマンド 1 件のメタ情報。実際の実行内容は
/// Services 側の Windows/MacOS カタログが Id をキーに保持する。
/// </summary>
public sealed record MaintenanceCommandDefinition(
    string Id,
    string Category,
    string Label,
    string Description,
    bool IsDestructive,
    bool RequiresReboot,
    bool IncludeInOneClickOptimization = false)
{
    /// <summary>おまかせ対象のうち、DHCP・DNS・TCP・NIC の設定前に実行する修復。</summary>
    public bool RunBeforeOneClickConfiguration { get; init; }
}
