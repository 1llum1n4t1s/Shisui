namespace Shisui.Core.Models;

/// <summary>
/// 現在設定されている DNS サーバー群に対する DNS over HTTPS (DoH) の有効状況。
/// </summary>
public enum DohStatus
{
    /// <summary>設定済みの全サーバーで DoH テンプレートが登録され、自動昇格が有効。</summary>
    Enabled,

    /// <summary>どのサーバーにも DoH テンプレートが登録されていない。</summary>
    Disabled,

    /// <summary>一部のサーバーのみ DoH テンプレートが登録されている (中途半端な状態)。</summary>
    Partial,

    /// <summary>取得できなかった (Windows バージョンが対応するコマンドレットを持たない等)。</summary>
    Unknown,
}
