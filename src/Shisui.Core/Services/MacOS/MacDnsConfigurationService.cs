using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacDnsConfigurationService(ICommandExecutor executor) : IDnsConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(string adapterId, DnsServerSet servers, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(MacDnsCommandBuilder.FileName, MacDnsCommandBuilder.BuildApply(adapterId, servers), ct);
        return [result];
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> ResetToAutomaticAsync(string adapterId, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(MacDnsCommandBuilder.FileName, MacDnsCommandBuilder.BuildResetToAutomatic(adapterId), ct);
        return [result];
    }
}
