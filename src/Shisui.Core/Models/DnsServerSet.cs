using System.Net;
using System.Net.Sockets;

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

    /// <summary>
    /// 入力済みの値が宣言されたアドレスファミリと一致し、セカンダリだけが単独指定されていないか。
    /// コマンド文字列へ埋め込む前の共通検証に使う。
    /// </summary>
    public bool HasValidAddressFamilies =>
        IsAddressOrEmpty(Ipv4Primary, AddressFamily.InterNetwork)
        && IsAddressOrEmpty(Ipv4Secondary, AddressFamily.InterNetwork)
        && IsAddressOrEmpty(Ipv6Primary, AddressFamily.InterNetworkV6)
        && IsAddressOrEmpty(Ipv6Secondary, AddressFamily.InterNetworkV6)
        && (string.IsNullOrWhiteSpace(Ipv4Secondary) || HasIpv4)
        && (string.IsNullOrWhiteSpace(Ipv6Secondary) || HasIpv6);

    private static bool IsAddressOrEmpty(string? value, AddressFamily expectedFamily) =>
        string.IsNullOrWhiteSpace(value)
        || IPAddress.TryParse(value.Trim(), out var address) && address.AddressFamily == expectedFamily;
}
