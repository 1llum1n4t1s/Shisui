using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 簡易疎通テスト (ping) / トレースルートを行う抽象。Windows/macOS 両対応
/// (DNS 設定タブの疎通テストボタンと「ネットワーク診断」タブの両方から使われる)。
/// </summary>
public interface INetworkDiagnosticsService
{
    Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default);

    Task<TraceRouteResult> TraceRouteAsync(string host, int maxHops, CancellationToken ct = default);
}
