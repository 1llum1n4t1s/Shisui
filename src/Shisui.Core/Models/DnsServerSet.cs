namespace Shisui.Core.Models;

/// <summary>
/// 適用対象の DNS サーバー組。IPv4/IPv6 それぞれ未指定 (null) を許容し、
/// 指定されたアドレスファミリだけを設定する。
/// </summary>
public sealed record DnsServerSet(
    string? Ipv4Primary,
    string? Ipv4Secondary,
    string? Ipv6Primary,
    string? Ipv6Secondary)
{
    public static readonly DnsServerSet Empty = new(null, null, null, null);

    public bool HasIpv4 => !string.IsNullOrWhiteSpace(Ipv4Primary);
    public bool HasIpv6 => !string.IsNullOrWhiteSpace(Ipv6Primary);
    public bool IsEmpty => !HasIpv4 && !HasIpv6;
}
