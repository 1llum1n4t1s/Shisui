using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>保存済みプロキシ設定を変更せず、自動プロキシのキャッシュだけを消去する。</summary>
public interface IWinHttpCacheService
{
    /// <summary>呼び出し元がネットワーク変更ゲートを保持する。成功は API の受付結果を示す。</summary>
    Task<CommandExecutionResult> ClearAutoProxyCacheAsync(CancellationToken ct = default);
}
