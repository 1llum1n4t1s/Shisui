namespace Shisui.Core.Models;

/// <summary>
/// UI で選択可能な DNS プロバイダのプリセット (Cloudflare 各種 / Google / カスタム)。
/// </summary>
public sealed record DnsProviderPreset(
    string Id,
    string Name,
    string Description,
    DnsServerSet Servers);
