using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>対応する物理 Ethernet / Wi-Fi NIC のゲーム向け遅延設定を、安全に適用・復元する。</summary>
public interface IGamingNetworkProfileService
{
    Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(
        string adapterName,
        CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> RestoreAsync(
        string adapterName,
        CancellationToken ct = default);
}
