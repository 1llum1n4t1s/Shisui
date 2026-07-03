namespace Shisui.Core.Models;

/// <summary>
/// OS のネットワークアダプタ / ネットワークサービスを表す。
/// Windows では netsh の name= に渡す接続名、macOS では networksetup が扱うサービス名を Id に持つ。
/// </summary>
public sealed record NetworkAdapterInfo(
    string Id,
    string DisplayName,
    string? Description,
    bool IsUp,
    IReadOnlyList<string> CurrentIPv4Dns,
    IReadOnlyList<string> CurrentIPv6Dns);
