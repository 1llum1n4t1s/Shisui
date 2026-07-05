using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDohConfigurationService(ICommandExecutor executor) : IDohConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dohTemplate, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDohCommandBuilder.BuildEnable(servers, dohTemplate))
        {
            results.Add(await executor.RunAsync(WindowsDohCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsDohCommandBuilder.BuildDisable(servers))
        {
            results.Add(await executor.RunAsync(WindowsDohCommandBuilder.FileName, args, ct));
        }

        return results;
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
