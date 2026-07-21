using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>長期間使われた Windows PC に残りやすいネットワーク状態を読み取り専用で診断する。</summary>
public interface ILegacyNetworkDiagnosticsService
{
    Task<LegacyNetworkDiagnosticsReport> DiagnoseAsync(
        string adapterName,
        CancellationToken ct = default);

    /// <summary>選択した NIC のドライバー詳細設定だけを工場出荷値へ戻す。</summary>
    Task<CommandExecutionResult> ResetAdapterAdvancedPropertiesAsync(
        string adapterName,
        CancellationToken ct = default);
}
