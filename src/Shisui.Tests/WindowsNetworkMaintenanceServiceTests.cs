using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkMaintenanceServiceTests
{
    private static readonly Guid Scheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static string ActiveOutput => $"Power Scheme GUID: {Scheme:D} (Balanced)";

    [TestMethod]
    public async Task PcieAcOff_ValidState_WritesAcOnlyAndVerifiesDcUnchanged()
    {
        var executor = new ScriptedExecutor(
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 2, 1)),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 0, 1)));

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsTrue(result.Success, result.StandardError);
        StringAssert.Contains(result.StandardOutput, "AC_AFTER=0");
        StringAssert.Contains(result.StandardOutput, "DC=1");
        Assert.IsTrue(executor.Arguments.Contains(WindowsPowerPlanCommandBuilder.SetAcLinkStateArguments(Scheme, 0)));
        Assert.IsFalse(executor.Arguments.Any(value => value.Contains("/setdcvalueindex", StringComparison.Ordinal)));
        Assert.AreEqual(WindowsPowerPlanCommandBuilder.ActivateSchemeArguments(Scheme), executor.Arguments[5]);
    }

    [TestMethod]
    public async Task PcieAcOff_QueryFailure_DoesNotWrite()
    {
        var executor = new ScriptedExecutor(Success(ActiveOutput), Failure("query failed"));

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsFalse(result.Success);
        Assert.IsFalse(executor.Arguments.Any(value => value.StartsWith("/set", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PcieAcOff_AlreadyOff_StillVerifiesAndSucceeds()
    {
        var executor = new ScriptedExecutor(
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 0, 2)),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 0, 2)));

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsTrue(result.Success, result.StandardError);
        StringAssert.Contains(result.StandardOutput, "AC_BEFORE=0");
    }

    [TestMethod]
    public async Task PcieAcOff_DcChangedDuringApply_FailsAndAttemptsAcRollback()
    {
        var executor = new ScriptedExecutor(
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 2, 2)),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(),
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 0, 1)),
            Success(),
            Success(ActiveOutput),
            Success());

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.StandardError, "DC=変更前");
        Assert.AreEqual(2, executor.Arguments.Count(value => value.StartsWith("/setacvalueindex", StringComparison.Ordinal)));
        Assert.IsFalse(executor.Arguments.Any(value => value.Contains("/setdcvalueindex", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PcieAcOff_ActivePlanChangesAfterWrite_DoesNotActivateOldPlan()
    {
        var otherScheme = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var executor = new ScriptedExecutor(
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 2, 2)),
            Success(ActiveOutput),
            Success(),
            Success($"Power Scheme GUID: {otherScheme:D}"),
            Success(),
            Success($"Power Scheme GUID: {otherScheme:D}"));

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.StandardError, "電源プランが変わった");
        Assert.IsFalse(executor.Arguments.Any(value => value.StartsWith("/setactive", StringComparison.Ordinal)));
        Assert.AreEqual(2, executor.Arguments.Count(value => value.StartsWith("/setacvalueindex", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PcieAcOff_SetAcFails_DoesNotContinueOrSucceed()
    {
        var executor = new ScriptedExecutor(
            Success(ActiveOutput),
            Success(WindowsPowerPlanCommandBuilderTests.QueryOutput(Scheme, 2, 2)),
            Success(ActiveOutput),
            Failure("access denied"));

        var result = await new WindowsNetworkMaintenanceService(executor)
            .RunAsync("powercfg-pcie-link-state-ac-off");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(4, executor.Arguments.Count);
        Assert.IsFalse(executor.Arguments.Any(value => value.StartsWith("/setactive", StringComparison.Ordinal)));
    }

    private static CommandExecutionResult Success(string output = "") => new(true, "powercfg", 0, output, string.Empty);
    private static CommandExecutionResult Failure(string error) => new(false, "powercfg", 1, string.Empty, error);

    private sealed class ScriptedExecutor(params CommandExecutionResult[] results) : ICommandExecutor
    {
        private readonly Queue<CommandExecutionResult> _results = new(results);
        public List<string> Arguments { get; } = [];

        public Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
        {
            Assert.AreEqual("powercfg", fileName);
            Arguments.Add(arguments);
            Assert.IsGreaterThan(0, _results.Count, $"予期しない追加実行: {arguments}");
            return Task.FromResult(_results.Dequeue());
        }
    }
}
