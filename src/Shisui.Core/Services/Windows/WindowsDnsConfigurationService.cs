using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsConfigurationService(ICommandExecutor executor) : IDnsConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(string adapterId, DnsServerSet servers, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDnsCommandBuilder.FileName, WindowsDnsCommandBuilder.BuildApply(adapterId, servers), ct);
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> ResetToAutomaticAsync(string adapterId, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDnsCommandBuilder.FileName, WindowsDnsCommandBuilder.BuildResetToAutomatic(adapterId), ct);
    }
}
