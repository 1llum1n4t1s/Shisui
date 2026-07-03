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
    bool RequiresReboot);
