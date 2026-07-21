using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public class NetworkMutationGateTests
{
    [TestMethod]
    public async Task EnterAsync_HoldsSecondCallerUntilFirstLeaseIsDisposed()
    {
        using var gate = new NetworkMutationGate();
        using var firstLease = await gate.EnterAsync();

        var secondEntry = gate.EnterAsync();
        await Task.Delay(50);
        Assert.IsFalse(secondEntry.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondEntry;
        Assert.IsTrue(secondEntry.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task EnterAsync_WhenWaitingIsCanceled_DoesNotConsumePermit()
    {
        using var gate = new NetworkMutationGate();
        using var firstLease = await gate.EnterAsync();
        using var cts = new CancellationTokenSource();

        var canceledEntry = gate.EnterAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await canceledEntry);

        firstLease.Dispose();
        using var nextLease = await gate.EnterAsync();
    }
}
