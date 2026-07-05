namespace Shisui.Core.Models;

/// <summary>
/// UI で選択可能な DNS プロバイダのプリセット (Cloudflare 各種 / Google / カスタム)。
/// </summary>
/// <param name="DohTemplate">
/// DNS over HTTPS のテンプレート URL (プロバイダの公式発表値)。null はこのプリセットで DoH 非対応
/// (カスタムプリセットはユーザーがテンプレートを指定する手段が無いため常に null)。
/// </param>
public sealed record DnsProviderPreset(
    string Id,
    string Name,
    string Description,
    DnsServerSet Servers,
    string? DohTemplate = null);
