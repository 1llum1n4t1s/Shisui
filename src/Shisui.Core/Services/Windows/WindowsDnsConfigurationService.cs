using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsConfigurationService(ICommandExecutor executor) : IDnsConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(string adapterId, DnsServerSet servers, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDnsCommandBuilder.BuildApply(adapterId, servers))
        {
            results.Add(await executor.RunAsync(WindowsDnsCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> ResetToAutomaticAsync(string adapterId, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDnsCommandBuilder.BuildResetToAutomatic(adapterId))
        {
            results.Add(await executor.RunAsync(WindowsDnsCommandBuilder.FileName, args, ct));
        }

        return results;
    }
}
