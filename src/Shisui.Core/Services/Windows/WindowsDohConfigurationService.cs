using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDohConfigurationService(ICommandExecutor executor) : IDohConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dohTemplate, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDohCommandBuilder.FileName, WindowsDohCommandBuilder.BuildEnable(servers, dohTemplate), ct);
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDohCommandBuilder.FileName, WindowsDohCommandBuilder.BuildDisable(servers), ct);
    }

    public async Task<DohStatus> GetStatusAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        var addresses = WindowsDohCommandBuilder.CollectAddresses(servers);
        if (addresses.Count == 0)
        {
            return DohStatus.Unknown;
        }

        var result = await executor.RunAsync(
            WindowsDohStateCommandBuilder.FileName,
            WindowsDohStateCommandBuilder.BuildArguments(addresses),
            ct);

        return result.Success ? WindowsDohStateParser.Parse(result.StandardOutput, addresses) : DohStatus.Unknown;
    }
}
