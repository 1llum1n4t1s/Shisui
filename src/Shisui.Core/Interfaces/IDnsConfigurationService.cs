using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface IDnsConfigurationService
{
    Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(string adapterId, DnsServerSet servers, CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> ResetToAutomaticAsync(string adapterId, CancellationToken ct = default);
}
