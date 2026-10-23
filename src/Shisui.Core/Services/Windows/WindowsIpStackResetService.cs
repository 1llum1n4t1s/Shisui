using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>IPv4/IPv6 を初期化し、終了を確認できたら呼び出し元のおまかせ再設定へ進む。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsIpStackResetService(ICommandExecutor executor) : IIpStackResetService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    public async Task<IReadOnlyList<CommandExecutionResult>> ResetAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var ipv6 in new[] { false, true })
        {
            var commands = new[]
            {
                (FileName: "powershell", Arguments: WindowsIpStackResetAccessCommandBuilder.BuildArguments(ipv6)),
                (FileName: "netsh", Arguments: ipv6 ? "interface ipv6 reset" : "interface ipv4 reset"),
            };
            foreach (var command in commands)
            {
                ct.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(CommandTimeout);
                CommandExecutionResult result;
                try
                {
                    result = await executor.RunAsync(command.FileName, command.Arguments, timeout.Token);
                    if (result.ExitCode < 0)
                        result = result with { RequiresOperationAbort = true };
                }
                catch (OperationCanceledException)
                {
                    result = new(false, ProcessCommandExecutor.FormatDiagnosticCommandLine(command.FileName, command.Arguments), -1, string.Empty,
                        "IP 初期化を中断しました。PC を再起動してから、おまかせ高速化設定をやり直してください。")
                    { RequiresOperationAbort = true };
                }
                catch (Exception ex)
                {
                    result = new(false, ProcessCommandExecutor.FormatDiagnosticCommandLine(command.FileName, command.Arguments), -1, string.Empty,
                        "IP 初期化の終了を確認できませんでした。PC を再起動してから設定をやり直してください: " + ex.Message)
                    { RequiresOperationAbort = true };
                }

                if (command.FileName == "netsh" && WindowsIpStackResetOutput.IsSuccessfulReset(result))
                {
                    result = result with { Success = true };
                    LoggerBootstrap.Log.Info($"IP 初期化の 34 項目の成功表示を OS 資源と照合して完了と判定しました。" +
                        $"元の終了コード={result.ExitCode} id={result.DiagnosticId ?? "none"} " +
                        $"protocol={(ipv6 ? "IPv6" : "IPv4")} 反映には再起動が必要です。");
                }

                results.Add(result);
                if (result.RequiresOperationAbort)
                    return results;
                // 権限補完や一部のリセットに失敗しても、終了済みなら結果を残して再設定を続ける。
            }
        }
        return results;
    }
}
