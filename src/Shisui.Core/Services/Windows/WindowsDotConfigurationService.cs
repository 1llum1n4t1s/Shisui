using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDotConfigurationService(ICommandExecutor executor) : IDotConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dotHost, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDotCommandBuilder.BuildEnable(servers, dotHost))
        {
            results.Add(await executor.RunAsync(WindowsDotCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDotCommandBuilder.BuildDisable(servers))
        {
            results.Add(await executor.RunAsync(WindowsDotCommandBuilder.FileName, args, ct));
        }

        return results;
    }
}
