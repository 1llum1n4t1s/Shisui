using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// DNS over HTTPS (DoH) の有効化/無効化を扱う抽象。Windows (netsh dnsclient) 専用機能のため
/// macOS では登録されない (DnsSettingsViewModel は null 許容で受け取り、非対応時は UI ごと隠す)。
/// </summary>
public interface IDohConfigurationService
{
    Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dohTemplate, CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default);

    Task<DohStatus> GetStatusAsync(DnsServerSet servers, CancellationToken ct = default);
}
