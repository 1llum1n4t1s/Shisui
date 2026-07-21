namespace Shisui.Core.Models;

/// <summary>ネットワーク診断で選べる代表的な疎通確認先。</summary>
public sealed record NetworkDiagnosticTargetPreset(string Id, string Name, string Host);

public static class NetworkDiagnosticTargetCatalog
{
    public static IReadOnlyList<NetworkDiagnosticTargetPreset> All { get; } =
    [
        new("localhost", "このPC", "127.0.0.1"),
        new("cloudflare-dns", "Cloudflare DNS", RequireHost(DnsPresetCatalog.CloudflareStandard.Servers.Ipv4Primary)),
        new("google-dns", "Google Public DNS", RequireHost(DnsPresetCatalog.GooglePublicDns.Servers.Ipv4Primary)),
        new("quad9-dns", "Quad9 DNS", RequireHost(DnsPresetCatalog.Quad9.Servers.Ipv4Primary)),
        new("google", "Google", "google.com"),
        new("github", "GitHub", "github.com"),
    ];

    private static string RequireHost(string? host) =>
        host ?? throw new InvalidOperationException("診断対象のホストが設定されていません");
}
