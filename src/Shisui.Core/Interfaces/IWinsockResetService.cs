using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>LSP 登録を保護する、おまかせ設定専用の Winsock リセット。</summary>
public interface IWinsockResetService
{
    /// <summary>直前の検査で全登録が基底プロバイダーと確認できた場合だけリセットする。</summary>
    /// <remarks>
    /// 呼び出し元がネットワーク変更ゲートを保持すること。このサービス内では再取得しない。
    /// LSP 検出時は成功扱いで省略し、検査不能時はリセット未実施の失敗を返す。
    /// </remarks>
    Task<CommandExecutionResult> ResetIfNoLayeredProvidersAsync(CancellationToken ct = default);
}
