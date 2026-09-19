using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsLossRecoveryTests
{
    [TestMethod]
    [DataRow("Enable RACK : enabled\nEnable Tail Loss Probe : enabled", true)]
    [DataRow("RACK の有効化 : enabled\r\nTail Loss Probe の有効化 : enabled", true)]
    [DataRow("Enable RACK : disabled\nEnable Tail Loss Probe : enabled", false)]
    [DataRow("Enable RACK : enabled", false)]
    [DataRow("Other RACK : enabled\nOther Tail Loss Probe : enabled", false)]
    [DataRow("Enable RACK : enabled\nEnable RACK : enabled\nEnable Tail Loss Probe : enabled", false)]
    [DataRow("Enable RACK : enabled\nEnable Tail Loss Probe : default", false)]
    public void Parse_OnlyExplicitCompleteEnabledStateIsAccepted(string text, bool expected) =>
        Assert.AreEqual(expected, WindowsLossRecoveryStateParser.AreBothEnabled(text));

    [TestMethod]
    public async Task AlreadyEnabled_DoesNotAttemptUnsupportedWrites()
    {
        var executor = new FakeExecutor("Enable RACK : enabled\nEnable Tail Loss Probe : enabled");
        var results = await new WindowsTcpTuningService(executor).EnableLossRecoveryAsync();
        Assert.HasCount(4, results);
        Assert.IsTrue(results.All(result => result.Success && result.CommandLine.Contains("変更不要")));
        Assert.HasCount(4, executor.Commands);
        Assert.IsTrue(executor.Commands.All(command => command.StartsWith("int tcp show supplemental")));
    }

    [TestMethod]
    [DataRow("Enable RACK : disabled\nEnable Tail Loss Probe : enabled", true)]
    [DataRow("unknown output", true)]
    [DataRow("Enable RACK : enabled\nEnable Tail Loss Probe : enabled", false)]
    public async Task DisabledUnknownOrFailedRead_PreservesWriteFailure(string output, bool readSuccess)
    {
        var executor = new FakeExecutor(output, readSuccess);
        var results = await new WindowsTcpTuningService(executor).EnableLossRecoveryAsync();
        Assert.HasCount(4, results);
        Assert.IsTrue(results.All(result => !result.Success && result.ExitCode == 1));
        Assert.AreEqual(4, executor.Commands.Count(command => command.Contains("rack=enabled")));
    }

    private sealed class FakeExecutor(string output, bool readSuccess = true) : ICommandExecutor
    {
        public List<string> Commands { get; } = [];
        public Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
        {
            Commands.Add(arguments);
            var read = arguments.Contains("show supplemental");
            var success = read && readSuccess;
            return Task.FromResult(new CommandExecutionResult(success, arguments, success ? 0 : 1,
                read ? output : "The request is not supported.", string.Empty));
        }
    }
}
