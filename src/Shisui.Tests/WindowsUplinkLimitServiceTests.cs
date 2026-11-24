using System.Runtime.Versioning;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsUplinkLimitServiceTests
{
    private const string Limit = UplinkLimitPolicy.LimitPolicyName;
    private const string Trial = UplinkLimitPolicy.TrialPolicyName;

    private static readonly string Query = WindowsUplinkLimitCommandBuilder.BuildQueryArguments();

    private static string B(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Remove(string name) => WindowsUplinkLimitCommandBuilder.BuildRemoveArguments(name);

    private static string Apply(string name, long rate) => WindowsUplinkLimitCommandBuilder.BuildApplyArguments(name, rate);

    private static string Policy(string name, long rate) => $"POLICY={B(name.ToLowerInvariant())},{rate},Default\nCOUNT=1\n";

    [TestMethod]
    public async Task OtherPolicies_RejectBothWriteEntryPoints_WithoutWriting()
    {
        var executor = new ScriptedExecutor(_ => $"LINE=UNKNOWN\nQOSACTIVE={B("Other")},-1,None\nDONE=1\n");
        var service = new WindowsUplinkLimitService(executor);

        var apply = await service.ApplyLimitAsync(20_000_000);
        var begin = await service.BeginMeasurementAsync();

        Assert.AreEqual(UplinkOperationOutcome.RejectedOtherPolicies, apply.Outcome);
        Assert.AreEqual(UplinkOperationOutcome.RejectedOtherPolicies, begin.Outcome);
        CollectionAssert.AreEqual(new[] { "Other" }, begin.OtherPolicyNames.ToArray());
        CollectionAssert.AreEqual(new[] { Query, Query }, executor.Arguments);
    }

    [TestMethod]
    public async Task ApplyLimit_ReadbackMismatch_FailsAndRemovesOwnPolicy()
    {
        var executor = new ScriptedExecutor(arguments =>
            arguments == Apply(Limit, 20_000_000) ? Policy(Limit, 19_000_000)
            : arguments == Remove(Limit) ? "COUNT=0\n"
            : "LINE=UNKNOWN\nDONE=1\n");

        var result = await new WindowsUplinkLimitService(executor).ApplyLimitAsync(20_000_000);

        Assert.AreEqual(UplinkOperationOutcome.VerificationFailed, result.Outcome);
        StringAssert.Contains(result.Message, "削除しました");
        CollectionAssert.AreEqual(new[] { Query, Apply(Limit, 20_000_000), Remove(Limit) }, executor.Arguments);
    }

    [TestMethod]
    public async Task BeginMeasurement_RemovesTrialThenLimit_AndReturnsPreviousLimit()
    {
        var executor = new ScriptedExecutor(arguments =>
            arguments == Query
                ? $"LINE=UNKNOWN\nQOSACTIVE={B(Limit.ToLowerInvariant())},28000000,Default\nQOSACTIVE={B(Trial.ToLowerInvariant())},5000000,Default\nDONE=1\n"
                : "COUNT=0\n");

        var result = await new WindowsUplinkLimitService(executor).BeginMeasurementAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(28_000_000L, result.PreviousLimitBitsPerSecond);
        CollectionAssert.AreEqual(new[] { Query, Remove(Trial), Remove(Limit) }, executor.Arguments);
    }

    [TestMethod]
    public async Task EndMeasurement_RestoresLimitOnlyWhenRequested()
    {
        var executor = new ScriptedExecutor(arguments =>
            arguments == Apply(Limit, 28_000_000) ? Policy(Limit, 28_000_000)
            : arguments == Query ? "LINE=UNKNOWN\nDONE=1\n"
            : "COUNT=0\n");
        var service = new WindowsUplinkLimitService(executor);

        var restored = await service.EndMeasurementAsync(28_000_000);
        var notRestored = await service.EndMeasurementAsync(null);

        Assert.IsTrue(restored.Success);
        Assert.IsTrue(notRestored.Success);
        CollectionAssert.AreEqual(
            new[] { Remove(Trial), Query, Apply(Limit, 28_000_000), Remove(Trial) }, executor.Arguments);
    }

    [TestMethod]
    public async Task Remove_NegativeExitCode_RequiresOperationAbort()
    {
        var executor = new ScriptedExecutor(_ => string.Empty) { ExitCode = -1 };

        var result = await new WindowsUplinkLimitService(executor).RemoveLimitAsync();

        Assert.AreEqual(UplinkOperationOutcome.Failed, result.Outcome);
        Assert.IsTrue(result.RequiresOperationAbort);
    }

    [TestMethod]
    public async Task Remove_Cancelled_RequiresOperationAbort()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await new WindowsUplinkLimitService(new ScriptedExecutor(_ => "COUNT=0\n")).RemoveLimitAsync(cts.Token);

        Assert.AreEqual(UplinkOperationOutcome.Failed, result.Outcome);
        Assert.IsTrue(result.RequiresOperationAbort);
    }

    private sealed class ScriptedExecutor(Func<string, string> output) : ICommandExecutor
    {
        public List<string> Arguments { get; } = [];
        public int ExitCode { get; init; }

        public Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Arguments.Add(arguments);
            return Task.FromResult(new CommandExecutionResult(ExitCode == 0, arguments, ExitCode, output(arguments), string.Empty));
        }
    }
}
