using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public sealed class ProcessCommandExecutorTests
{
    [TestMethod]
    public async Task RunAsync_CanceledWait_TerminatesStartedProcess()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"shisui-process-{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource();
        Task? runTask = null;
        var processId = 0;

        try
        {
            var executor = new ProcessCommandExecutor();
            var (fileName, arguments) = BuildLongRunningCommand(markerPath);
            runTask = executor.RunAsync(fileName, arguments, cancellation.Token);

            processId = await WaitForProcessIdAsync(markerPath);
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await runTask);
            Assert.IsFalse(IsProcessRunning(processId), "キャンセル後も外部プロセスが残っています。");
        }
        finally
        {
            cancellation.Cancel();
            if (runTask is not null)
            {
                try
                {
                    await runTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            TryTerminate(processId);
            File.Delete(markerPath);
        }
    }

    private static (string FileName, string Arguments) BuildLongRunningCommand(string markerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var escapedPath = markerPath.Replace("'", "''");
            return ("pwsh", $"-NoLogo -NoProfile -NonInteractive -Command \"$PID | Set-Content -LiteralPath '{escapedPath}' -NoNewline; Start-Sleep -Seconds 30\"");
        }

        var shellEscapedPath = markerPath.Replace("'", "'\\''");
        return ("/bin/sh", $"-c \"echo $$ > '{shellEscapedPath}'; sleep 30\"");
    }

    private static async Task<int> WaitForProcessIdAsync(string markerPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(markerPath))
        {
            await Task.Delay(25, timeout.Token);
        }

        var text = await File.ReadAllTextAsync(markerPath, timeout.Token);
        return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryTerminate(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
        }
    }
}
