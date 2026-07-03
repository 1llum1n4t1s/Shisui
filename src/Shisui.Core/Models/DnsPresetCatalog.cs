namespace Shisui.Core.Models;

/// <summary>
/// 既定で選択できる DNS プロバイダのプリセット一覧。
/// IP アドレスは各プロバイダの公式発表値 (Cloudflare 1.1.1.1 for Families / Google Public DNS)。
/// </summary>
public static class DnsPresetCatalog
{
    public static readonly DnsProviderPreset CloudflareStandard = new(
        "cloudflare-standard",
        "Cloudflare (標準)",
        "フィルタリングなし。最速志向のパブリック DNS",
        new DnsServerSet("1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"));

    public static readonly DnsProviderPreset CloudflareMalwareBlock = new(
        "cloudflare-malware",
        "Cloudflare (マルウェアブロック)",
        "既知のマルウェア配布サイトへの通信をブロック",
        new DnsServerSet("1.1.1.2", "1.0.0.2", "2606:4700:4700::1112", "2606:4700:4700::1002"));

    public static readonly DnsProviderPreset CloudflareMalwareAdultBlock = new(
        "cloudflare-malware-adult",
        "Cloudflare (マルウェア + アダルトブロック)",
        "マルウェアに加えてアダルトコンテンツもブロック",
        new DnsServerSet("1.1.1.3", "1.0.0.3", "2606:4700:4700::1113", "2606:4700:4700::1003"));

    public static readonly DnsProviderPreset GooglePublicDns = new(
        "google-public-dns",
        "Google Public DNS",
        "Google が提供するパブリック DNS",
        new DnsServerSet("8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"));

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
        Custom,
    ];
}
