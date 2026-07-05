namespace Shisui.Core.Models;

/// <summary>
/// UI で選択可能な DNS プロバイダのプリセット (Cloudflare 各種 / Google / Quad9 / NextDNS / カスタム)。
/// </summary>
/// <param name="DohTemplate">
/// DNS over HTTPS のテンプレート URL (プロバイダの公式発表値)。null はこのプリセットで DoH 非対応
/// (カスタムプリセットはユーザーがテンプレートを指定する手段が無いため常に null)。
/// </param>
/// <param name="DotHost">
/// DNS over TLS のホスト名 (ポート番号を含まない。公式発表値)。null はこのプリセットで DoT 非対応。
/// Cloudflare は DoH と同様ティアごとにホスト名が異なる (security./family. サブドメイン)。
/// </param>
public sealed record DnsProviderPreset(
    string Id,
    string Name,
    string Description,
    DnsServerSet Servers,
    string? DohTemplate = null,
    string? DotHost = null);
