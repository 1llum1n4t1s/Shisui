using System.Diagnostics;
using System.Text;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 外部プロセスをそのまま起動する既定の ICommandExecutor。
/// Windows ではアプリ自体が管理者権限で起動しているため、子プロセスもそのまま昇格状態を継承する。
/// </summary>
public class ProcessCommandExecutor : ICommandExecutor
{
    public async Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var commandLine = string.IsNullOrEmpty(arguments) ? fileName : $"{fileName} {arguments}";

        var psi = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);

            return new CommandExecutionResult(
                process.ExitCode == 0,
                commandLine,
                process.ExitCode,
                stdout.ToString().TrimEnd(),
                stderr.ToString().TrimEnd());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CommandExecutionResult(false, commandLine, -1, string.Empty, ex.Message);
        }
    }
}
