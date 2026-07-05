namespace Shisui.Core.Models;

/// <summary>
/// 既定で選択できる DNS プロバイダのプリセット一覧。
/// IP アドレスは各プロバイダの公式発表値 (Cloudflare 1.1.1.1 for Families / Google Public DNS / Quad9 /
/// NextDNS Linked IP)。DoH/DoT のホスト名も同様に公式発表値 (Cloudflare は標準/Security/Family でホスト名が
/// 異なる点に注意。security./family. サブドメインを標準の cloudflare-dns.com と取り違えるとフィルタが効かない
/// 接続になってしまう。https://developers.cloudflare.com/1.1.1.1/setup/ および
/// https://developers.cloudflare.com/1.1.1.1/encryption/dns-over-tls/ で確認)。
/// </summary>
public static class DnsPresetCatalog
{
    public static readonly DnsProviderPreset CloudflareStandard = new(
        "cloudflare-standard",
        "Cloudflare (標準)",
        "フィルタリングなし。最速志向のパブリック DNS",
        new DnsServerSet("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
        DohTemplate: "https://cloudflare-dns.com/dns-query",
        DotHost: "cloudflare-dns.com");

    public static readonly DnsProviderPreset CloudflareMalwareBlock = new(
        "cloudflare-malware",
        "Cloudflare (マルウェアブロック)",
        "既知のマルウェア配布サイトへの通信をブロック",
        new DnsServerSet("1.1.1.2", "1.0.0.2", "2606:4700:4700::1112", "2606:4700:4700::1002"),
        DohTemplate: "https://security.cloudflare-dns.com/dns-query",
        DotHost: "security.cloudflare-dns.com");

    public static readonly DnsProviderPreset CloudflareMalwareAdultBlock = new(
        "cloudflare-malware-adult",
        "Cloudflare (マルウェア + アダルトブロック)",
        "マルウェアに加えてアダルトコンテンツもブロック",
        new DnsServerSet("1.1.1.3", "1.0.0.3", "2606:4700:4700::1113", "2606:4700:4700::1003"),
        DohTemplate: "https://family.cloudflare-dns.com/dns-query",
        DotHost: "family.cloudflare-dns.com");

    public static readonly DnsProviderPreset GooglePublicDns = new(
        "google-public-dns",
        "Google Public DNS",
        "Google が提供するパブリック DNS",
        new DnsServerSet("8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
        DohTemplate: "https://dns.google/dns-query",
        DotHost: "dns.google");

    /// <summary>
    /// Quad9 (標準/セキュア構成)。マルウェア・フィッシングブロックとDNSSEC検証が既定で有効。
    /// https://quad9.net/service/service-addresses-and-features/ で確認済み。
    /// </summary>
    public static readonly DnsProviderPreset Quad9 = new(
        "quad9",
        "Quad9",
        "マルウェア・フィッシングサイトをブロック。DNSSEC 検証も既定で有効",
        new DnsServerSet("9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
        DohTemplate: "https://dns.quad9.net/dns-query",
        DotHost: "dns.quad9.net");

    /// <summary>
    /// NextDNS の「Linked IP」構成。IP アドレス自体は全ユーザー共通の固定値で、フィルタリング内容は
    /// NextDNS 側ダッシュボードで別途「現在の IP をこの設定にリンク」する必要がある (アカウント別の
    /// 設定 ID を DNS サーバー IP に埋め込む方式ではないため)。DoH/DoT はアカウント別サブドメインが
    /// 必要になり固定値で表現できないため非対応 (null)。
    /// https://help.nextdns.io (dns.nextdns.io IP addresses / Linked IP) で確認済み。
    /// </summary>
    public static readonly DnsProviderPreset NextDns = new(
        "nextdns",
        "NextDNS (Linked IP)",
        "カスタマイズ可能なフィルタリング DNS。要 NextDNS 側で現在の IP をアカウントにリンクする設定 (未リンクの場合は既定の無フィルタ状態で動作)",
        new DnsServerSet("45.90.28.0", "45.90.30.0", "2a07:a8c0::", "2a07:a8c1::"));

    /// <summary>
    /// カスタム入力を表すプレースホルダープリセット (Servers は空)。
    /// UI はこれが選択されたとき自由入力欄を表示する。
    /// </summary>
    public static readonly DnsProviderPreset Custom = new(
        "custom",
        "カスタム",
        "IPv4 / IPv6 のアドレスを自由に指定 (ここに挙げていない DNS も設定可能)",
        DnsServerSet.Empty);

    public static IReadOnlyList<DnsProviderPreset> BuiltIn { get; } =
    [
        CloudflareStandard,
        CloudflareMalwareBlock,
        CloudflareMalwareAdultBlock,
        GooglePublicDns,
        Quad9,
        NextDns,
        Custom,
    ];
}
