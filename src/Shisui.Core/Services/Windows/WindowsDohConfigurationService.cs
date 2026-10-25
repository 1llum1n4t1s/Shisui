using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDohConfigurationService(ICommandExecutor executor) : IDohConfigurationService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dohTemplate, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsDohCommandBuilder.EnableFileName,
            WindowsDohCommandBuilder.BuildEnableArguments(servers, dohTemplate), ct);
        var output = result.StandardOutput.EndsWith("\r\n", StringComparison.Ordinal) ? result.StandardOutput[..^2]
            : result.StandardOutput.EndsWith('\n') ? result.StandardOutput[..^1] : result.StandardOutput;
        if (result.Success && (result.ExitCode != 0 || result.RequiresOperationAbort || output != "DOHREPAIR=VERIFIED"))
            result = result with { Success = false, StandardError = result.StandardError + Environment.NewLine + "DoH 登録の修復・読み戻しを確認できませんでした。" };
        return [result];
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsDohCommandBuilder.FileName, WindowsDohCommandBuilder.BuildDisable(servers), ct);
    }

    public async Task<DohStatus> GetStatusAsync(DnsServerSet servers, string expectedTemplate, CancellationToken ct = default)
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

        return result.Success && result.ExitCode == 0 && !result.RequiresOperationAbort
            ? WindowsDohStateParser.Parse(result.StandardOutput, addresses, expectedTemplate) : DohStatus.Unknown;
    }
}
