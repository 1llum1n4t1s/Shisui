using System.Diagnostics;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>画面への結果通知とは独立して、実行時点の診断情報を記録する。</summary>
internal sealed class CommandExecutionTrace
{
    private readonly Action<string, bool> _write;
    private readonly string _commandLine;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private readonly string _id = Guid.NewGuid().ToString("N");

    public CommandExecutionTrace(string commandLine, Action<string, bool>? write = null)
    {
        _write = write ?? Write;
        _commandLine = commandLine;
        _write($"CommandStart id={_id} at={DateTimeOffset.Now:O} operation={LoggerBootstrap.OperationId}\ncommand={commandLine}", false);
    }

    public void Started(Process process) =>
        _write($"CommandProcess id={_id} pid={process.Id} executable={process.StartInfo.FileName}", false);

    public CommandExecutionResult Complete(CommandExecutionResult result)
    {
        _write($"CommandEnd id={_id} at={DateTimeOffset.Now:O} elapsedMs={_timer.Elapsed.TotalMilliseconds:F1}\n" +
               FormatResult(result with { CommandLine = _commandLine }), !result.Success);
        return result with { DiagnosticId = _id };
    }

    public void Interrupted(Exception exception, string stdout = "", string stderr = "") =>
        _write($"CommandInterrupted id={_id} at={DateTimeOffset.Now:O} elapsedMs={_timer.Elapsed.TotalMilliseconds:F1} " +
               $"status={(exception is OperationCanceledException ? "Canceled" : "Exception")}\n" +
               $"stdout (partial):\n{stdout}\nstderr (partial):\n{stderr}\nexception:\n{exception}", true);

    internal static string FormatResult(CommandExecutionResult result) =>
        $"command={result.CommandLine}\nsuccess={result.Success} exit={result.ExitCode}\n" +
        $"stdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}";

    internal static void Write(string message, bool error)
    {
        if (error) LoggerBootstrap.Log.Error(message);
        else LoggerBootstrap.Log.Info(message);
    }
}
