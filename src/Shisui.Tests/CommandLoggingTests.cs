using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public sealed class CommandLoggingTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task RunAsync_RecordsBothStreamsRegardlessOfExitCode(int exitCode)
    {
        var executor = new RecordingExecutor();
        var result = OperatingSystem.IsWindows()
            ? await executor.RunAsync("powershell", $"-NoProfile -NonInteractive -Command \"[Console]::Out.WriteLine('stdout-detail'); [Console]::Error.WriteLine('stderr-detail'); exit {exitCode}\"")
            : await executor.RunAsync("/bin/sh", $"-c \"echo stdout-detail; echo stderr-detail >&2; exit {exitCode}\"");

        Assert.AreEqual(exitCode, result.ExitCode);
        Assert.AreEqual("stdout-detail", result.StandardOutput);
        Assert.AreEqual("stderr-detail", result.StandardError);
        Assert.AreEqual(3, executor.Entries.Count);
        StringAssert.Contains(executor.Entries[0].Message, "CommandStart id=");
        StringAssert.Contains(executor.Entries[1].Message, "pid=");
        var end = executor.Entries[2];
        StringAssert.Contains(end.Message, "elapsedMs=");
        StringAssert.Contains(end.Message, $"exit={exitCode}");
        StringAssert.Contains(end.Message, "stdout:\nstdout-detail\nstderr:\nstderr-detail");
        Assert.AreEqual(exitCode != 0, end.Error);
        var id = executor.Entries[0].Message.Split("id=")[1].Split(' ')[0];
        Assert.IsTrue(executor.Entries.All(entry => entry.Message.Contains($"id={id}")));
    }

    [TestMethod]
    public async Task RunAsync_FailureWithOnlyStdout_PreservesReasonInDiagnosticLog()
    {
        var executor = new RecordingExecutor();
        var result = OperatingSystem.IsWindows()
            ? await executor.RunAsync("powershell", "-NoProfile -NonInteractive -Command \"[Console]::Out.WriteLine('unsupported setting'); exit 1\"")
            : await executor.RunAsync("/bin/sh", "-c \"echo 'unsupported setting'; exit 1\"");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.StandardError);
        StringAssert.Contains(executor.Entries.Last().Message, "stdout:\nunsupported setting");
    }

    [TestMethod]
    public async Task RunAsync_StartFailure_RecordsExceptionAndFailedResult()
    {
        var executor = new RecordingExecutor();
        var result = await executor.RunAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"), "");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(executor.Entries.Any(entry => entry.Message.Contains("status=Exception") &&
                                                  entry.Message.Contains("System.ComponentModel.Win32Exception")));
        StringAssert.Contains(executor.Entries.Last().Message, "exit=-1");
    }

    [TestMethod]
    public void ReportedVerification_PreservesActualValuesEvenOnFailure()
    {
        var result = new CommandExecutionResult(false, "適用後確認", -1, "Effective=Disabled", "Expected=Normal");
        var text = CommandExecutionTrace.FormatResult(result);
        StringAssert.Contains(text, "Effective=Disabled");
        StringAssert.Contains(text, "Expected=Normal");
    }

    private sealed class RecordingExecutor : ProcessCommandExecutor
    {
        public List<(string Message, bool Error)> Entries { get; } = [];
        protected override void WriteDiagnostic(string message, bool error) => Entries.Add((message, error));
    }
}
