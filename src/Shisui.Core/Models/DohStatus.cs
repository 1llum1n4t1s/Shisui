namespace Shisui.Core.Models;

/// <summary>
/// 現在設定されている DNS サーバー群に対する DNS over HTTPS (DoH) の有効状況。
/// </summary>
public enum DohStatus
{
    /// <summary>全対象に期待した DoH テンプレートが一意に登録され、自動昇格が有効。fallback は型を確認済み。</summary>
    Enabled,

    /// <summary>読取可能な全対象で、期待テンプレートまたは自動昇格が一致しない。</summary>
    Disabled,

    /// <summary>一部のサーバーのみ DoH テンプレートが登録されている (中途半端な状態)。</summary>
    Partial,

    /// <summary>取得できなかった (Windows バージョンが対応するコマンドレットを持たない等)。</summary>
    Unknown,
}
