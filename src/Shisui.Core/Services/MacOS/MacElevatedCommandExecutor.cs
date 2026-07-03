using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// macOS ではアプリ自体を管理者権限で起動しない (OS の作法に反するため)。
/// その代わり、特権が必要なコマンド 1 回ごとに AppleScript の
/// "with administrator privileges" で個別に昇格プロンプトを出す。
/// fileName / arguments は呼び出し側 (MacOS 配下の各 CommandBuilder) が
/// 既に sh 互換のクォートで組み立て済みの前提。ここでは AppleScript の
/// 文字列リテラルとして埋め込むためのエスケープだけを行う。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacElevatedCommandExecutor : ICommandExecutor
{
    public async Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var shellCommand = string.IsNullOrEmpty(arguments) ? fileName : $"{fileName} {arguments}";
        var appleScript = $"do shell script \"{EscapeForAppleScript(shellCommand)}\" with administrator privileges";

        var psi = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(appleScript);

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
                shellCommand,
                process.ExitCode,
                stdout.ToString().TrimEnd(),
                stderr.ToString().TrimEnd());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CommandExecutionResult(false, shellCommand, -1, string.Empty, ex.Message);
        }
    }

    private static string EscapeForAppleScript(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
