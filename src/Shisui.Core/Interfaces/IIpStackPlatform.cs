using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface IIpStackPlatform
{
    Task<string> GetBootIdAsync(CancellationToken ct = default);
    Task<IpStackSnapshot> CaptureAsync(CancellationToken ct = default);
    Task<CommandExecutionResult> ResetIpv4Async(CancellationToken ct = default);
    Task<CommandExecutionResult> ResetIpv6Async(CancellationToken ct = default);
    Task<CommandExecutionResult> RestoreAsync(IpStackSnapshot snapshot, CancellationToken ct = default);
    bool Matches(IpStackSnapshot expected, IpStackSnapshot actual, bool afterReboot = false);
}
