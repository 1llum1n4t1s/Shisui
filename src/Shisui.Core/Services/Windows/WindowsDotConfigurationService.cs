using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDotConfigurationService(ICommandExecutor executor) : IDotConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dotHost, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDotCommandBuilder.FileName, WindowsDotCommandBuilder.BuildEnable(servers, dotHost), ct);
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDotCommandBuilder.FileName, WindowsDotCommandBuilder.BuildDisable(servers), ct);
    }
}
