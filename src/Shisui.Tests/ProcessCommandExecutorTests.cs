using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public sealed class ProcessCommandExecutorTests
{
    [TestMethod]
    [DataRow("netsh", "netsh.exe")]
    [DataRow("ipconfig.exe", "ipconfig.exe")]
    [DataRow("pnputil", "pnputil.exe")]
    [DataRow("nbtstat", "nbtstat.exe")]
    [DataRow("route", "route.exe")]
    [DataRow("netcfg", "netcfg.exe")]
    public void ResolveWindowsExecutablePath_SystemCommand_UsesSystemDirectory(
        string fileName,
        string expectedFileName)
    {
        var systemDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "System32"));

        var result = ProcessCommandExecutor.ResolveWindowsExecutablePath(fileName, systemDirectory);

        Assert.AreEqual(Path.Combine(systemDirectory, expectedFileName), result);
    }

    [TestMethod]
    public void ResolveWindowsExecutablePath_PowerShell_UsesWindowsPowerShellDirectory()
    {
        var systemDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "System32"));

        var result = ProcessCommandExecutor.ResolveWindowsExecutablePath("powershell", systemDirectory);

        Assert.AreEqual(
            Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            result);
    }

    [TestMethod]
    public void ResolveWindowsExecutablePath_UnknownBareName_IsRejected()
    {
        var systemDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "System32"));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ProcessCommandExecutor.ResolveWindowsExecutablePath("shisui-probe", systemDirectory));
    }

    [TestMethod]
    public void FormatDiagnosticCommandLine_EncodedPowerShell_RecordsDecodedScript()
    {
        const string script = "Get-NetAdapter -Name 'Wi-\"Fi'";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var result = ProcessCommandExecutor.FormatDiagnosticCommandLine(
            "powershell",
            $"-NoProfile -NonInteractive -EncodedCommand {encoded}");

        StringAssert.Contains(result, "-Command <decoded>");
        StringAssert.Contains(result, script);
        Assert.IsFalse(result.Contains(encoded, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RunAsync_CanceledWait_TerminatesStartedProcess()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"shisui-process-{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource();
        Task? runTask = null;
        var processId = 0;

        try
        {
            var executor = new RecordingProcessCommandExecutor();
            var (fileName, arguments) = BuildLongRunningCommand(markerPath);
            runTask = executor.RunAsync(fileName, arguments, cancellation.Token);

            processId = await WaitForProcessIdAsync(markerPath);
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await runTask);
            Assert.IsFalse(IsProcessRunning(processId), "キャンセル後も外部プロセスが残っています。");
            Assert.IsTrue(executor.Messages.Any(message => message.Contains("status=Canceled")));
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

    [TestMethod]
    public async Task RunAsync_OutputReadFails_TerminatesStartedProcess()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"shisui-process-{Guid.NewGuid():N}.txt");
        var processId = 0;

        try
        {
            var executor = new ThrowingOutputProcessCommandExecutor(markerPath);
            var (fileName, arguments) = BuildLongRunningCommand(markerPath: null);

            var result = await executor.RunAsync(fileName, arguments);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(executor.Messages.Any(message => message.Contains("System.IO.IOException: simulated output failure")));
            processId = int.Parse(await File.ReadAllTextAsync(markerPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsFalse(IsProcessRunning(processId), "出力取得の失敗後も外部プロセスが残っています。");
        }
        finally
        {
            TryTerminate(processId);
            File.Delete(markerPath);
        }
    }

    private static (string FileName, string Arguments) BuildLongRunningCommand(string? markerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var powershell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (markerPath is null)
            {
                return (powershell, "-NoLogo -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"");
            }

            var escapedPath = markerPath.Replace("'", "''");
            return (powershell, $"-NoLogo -NoProfile -NonInteractive -Command \"$PID | Set-Content -LiteralPath '{escapedPath}' -NoNewline; Start-Sleep -Seconds 30\"");
        }

        if (markerPath is null)
        {
            return ("/bin/sh", "-c \"sleep 30\"");
        }

        var shellEscapedPath = markerPath.Replace("'", "'\\''");
        return ("/bin/sh", $"-c \"echo $$ > '{shellEscapedPath}'; sleep 30\"");
    }

    private static async Task<int> WaitForProcessIdAsync(string markerPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                var text = await File.ReadAllTextAsync(markerPath, timeout.Token);
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var processId))
                {
                    return processId;
                }
            }
            catch (IOException)
            {
                // ファイル作成と書き込み完了は別。書き込み中の共有違反も完了まで待つ。
            }
            await Task.Delay(25, timeout.Token);
        }
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

    private class RecordingProcessCommandExecutor : ProcessCommandExecutor
    {
        public List<string> Messages { get; } = [];
        protected override void WriteDiagnostic(string message, bool error) => Messages.Add(message);
    }

    private sealed class ThrowingOutputProcessCommandExecutor(string markerPath) : RecordingProcessCommandExecutor
    {
        protected override Task CopyOutputAsync(
            Process process,
            MemoryStream stdoutBuffer,
            MemoryStream stderrBuffer,
            CancellationToken ct)
        {
            File.WriteAllText(markerPath, process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromException(new IOException("simulated output failure"));
        }
    }
}
