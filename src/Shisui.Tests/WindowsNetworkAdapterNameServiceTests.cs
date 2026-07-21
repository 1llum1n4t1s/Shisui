using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsNetworkAdapterNameServiceTests
{
    [TestMethod]
    public async Task CleanupAsync_RemovesAllDisconnectedNetworkDevicesThenRenamesCurrentAdapter()
    {
        var executor = new FakeExecutor
        {
            QueryOutput = WindowsAdapterNameParserTests.Record("Ethernet 3"),
        };
        var ghosts = new FakeGhostAdapterService([
            Ghost("OLD\\BASE", "Old Controller"),
            Ghost("OLD\\TWO", "Old Controller #2"),
            Ghost("OLD\\VPN", "VPN Adapter"),
        ]);
        var service = new WindowsNetworkAdapterNameService(executor, ghosts);

        var result = await service.CleanupAsync("Ethernet 3");

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("Ethernet", result.TargetName);
        Assert.AreEqual(3, result.RemovedGhostCount);
        Assert.IsTrue(result.WasRenamed);
        CollectionAssert.AreEquivalent(
            new[] { "OLD\\BASE", "OLD\\TWO", "OLD\\VPN" },
            ghosts.RemovedInstanceIds.ToArray());
        Assert.HasCount(2, executor.Calls);
        Assert.HasCount(4, result.CommandResults);
    }

    [TestMethod]
    public async Task CleanupAsync_TargetNameOwnedByPresentAdapter_StillRemovesAllGhostsButDoesNotRename()
    {
        var executor = new FakeExecutor
        {
            QueryOutput =
                WindowsAdapterNameParserTests.Record("Ethernet")
                + WindowsAdapterNameParserTests.Record("Ethernet 3"),
        };
        var ghosts = new FakeGhostAdapterService([Ghost("OLD\\TWO", "Controller #2")]);
        var service = new WindowsNetworkAdapterNameService(executor, ghosts);

        var result = await service.CleanupAsync("Ethernet 3");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "別の現役アダプタ");
        CollectionAssert.AreEqual(new[] { "OLD\\TWO" }, ghosts.RemovedInstanceIds);
        Assert.HasCount(1, executor.Calls);
        Assert.HasCount(1, result.CommandResults);
    }

    [TestMethod]
    public async Task CleanupAsync_NoSelectedAdapter_RemovesAllGhostsWithoutNameQuery()
    {
        var executor = new FakeExecutor();
        var ghosts = new FakeGhostAdapterService([
            Ghost("OLD\\LAN", "Old LAN"),
            Ghost("OLD\\VPN", "Old VPN"),
        ]);
        var service = new WindowsNetworkAdapterNameService(executor, ghosts);

        var result = await service.CleanupAsync(null);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(2, result.RemovedGhostCount);
        Assert.IsNull(result.TargetName);
        CollectionAssert.AreEquivalent(
            new[] { "OLD\\LAN", "OLD\\VPN" },
            ghosts.RemovedInstanceIds.ToArray());
        Assert.IsEmpty(executor.Calls);
        Assert.HasCount(2, result.CommandResults);
    }

    private static GhostAdapterInfo Ghost(string instanceId, string description) =>
        new(instanceId, description, "Vendor", "oem1.inf", false);

    private sealed class FakeExecutor : ICommandExecutor
    {
        public string QueryOutput { get; init; } = string.Empty;

        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public Task<CommandExecutionResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken ct = default)
        {
            Calls.Add((fileName, arguments));
            var output = arguments == WindowsAdapterNameCommandBuilder.QueryArguments
                ? QueryOutput
                : "RENAMED=Ethernet";
            return Task.FromResult(new CommandExecutionResult(
                true,
                $"{fileName} {arguments}",
                0,
                output,
                string.Empty));
        }
    }

    private sealed class FakeGhostAdapterService(IReadOnlyList<GhostAdapterInfo> ghosts) : IGhostAdapterService
    {
        public List<string> RemovedInstanceIds { get; } = [];

        public Task<IReadOnlyList<GhostAdapterInfo>> GetGhostAdaptersAsync(CancellationToken ct = default) =>
            Task.FromResult(ghosts);

        public Task<CommandExecutionResult> RemoveGhostAdapterAsync(
            string instanceId,
            CancellationToken ct = default)
        {
            RemovedInstanceIds.Add(instanceId);
            return Task.FromResult(new CommandExecutionResult(
                true,
                $"pnputil /remove-device \"{instanceId}\"",
                0,
                string.Empty,
                string.Empty));
        }
    }
}
