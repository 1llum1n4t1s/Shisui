using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 切断済み (現在接続されていない) ネットワークデバイスの一覧取得・削除。Windows 専用機能。
/// </summary>
public interface IGhostAdapterService
{
    Task<IReadOnlyList<GhostAdapterInfo>> GetGhostAdaptersAsync(CancellationToken ct = default);

    Task<CommandExecutionResult> RemoveGhostAdapterAsync(string instanceId, CancellationToken ct = default);
}
