using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public class UplinkMeasurementServiceTests
{
    private const string Guid = "{11111111-2222-3333-4444-555555555555}";
    private const string Mac = "AA-BB-CC-DD-EE-FF";
    private const long Capacity = 100_000_000;

    // ---- 偽物 ----

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
        public void Advance(TimeSpan span) => _ticks += span.Ticks;
    }

    private static UplinkLimitOperationResult Ok(long? previous = null) =>
        new(UplinkOperationOutcome.Succeeded, "ok", [], [], previous);

    private static UplinkLimitOperationResult Fail(UplinkOperationOutcome outcome, params string[] others) =>
        new(outcome, "ng", [], others);

    private static UplinkLimitState Healthy(long? existingLimit = null) => new(
        UplinkStateReadStatus.Ok,
        new UplinkLineIdentity(UplinkLineStatus.Identified, "Ethernet", Guid, UplinkAdapterKind.Physical, Mac),
        existingLimit is { } v ? new UplinkPolicyInfo("shisuiuplinklimit", v, true) : null,
        null, [], []);

    private sealed class FakeLimit : IUplinkLimitService
    {
        public UplinkLimitState State = Healthy();
        public long? Trial;
        public int BeginCalls;
        public int ApplyLimitCalls;
        public int StateCalls;
        public List<long> AppliedTrials = [];
        public List<(long? Restore, bool TokenCanBeCanceled)> Ends = [];
        public UplinkLimitOperationResult? BeginResult;
        public UplinkLimitOperationResult? ApplyTrialResult;
        public UplinkLimitOperationResult? EndResult;
        public bool EndThrows;
        public bool EndClearsTrialEvenWhenFailing = true;

        public Task<UplinkLimitState> GetStateAsync(CancellationToken ct = default)
        {
            StateCalls++;
            var trial = Trial is { } t ? new UplinkPolicyInfo("shisuiuplinktrial", t, true) : null;
            return Task.FromResult(State with { TrialPolicy = trial });
        }

        public Task<UplinkLimitOperationResult> ApplyLimitAsync(long bitsPerSecond, CancellationToken ct = default)
        {
            ApplyLimitCalls++;
            return Task.FromResult(Ok());
        }

        public Task<UplinkLimitOperationResult> RemoveLimitAsync(CancellationToken ct = default) => Task.FromResult(Ok());

        public Action? OnApplyTrial;
        public Action? OnBegin;

        /// <summary>計測中の変更コマンドに取り消し可能なトークンが渡ったか (渡ると途中で止まり、後始末できなくなる)。</summary>
        public bool MutationTokenCanBeCanceled;

        public Task<UplinkLimitOperationResult> ApplyTrialAsync(long bitsPerSecond, CancellationToken ct = default)
        {
            MutationTokenCanBeCanceled |= ct.CanBeCanceled;
            OnApplyTrial?.Invoke();
            if (ApplyTrialResult is { Success: false } failure)
            {
                return Task.FromResult(failure);
            }

            Trial = bitsPerSecond;
            AppliedTrials.Add(bitsPerSecond);
            return Task.FromResult(Ok());
        }

        public Task<UplinkLimitOperationResult> RemoveTrialAsync(CancellationToken ct = default)
        {
            MutationTokenCanBeCanceled |= ct.CanBeCanceled;
            Trial = null;
            return Task.FromResult(Ok());
        }

        public Task<UplinkLimitOperationResult> BeginMeasurementAsync(CancellationToken ct = default)
        {
            BeginCalls++;
            MutationTokenCanBeCanceled |= ct.CanBeCanceled;
            OnBegin?.Invoke();
            return Task.FromResult(BeginResult ?? Ok(State.LimitPolicy?.ThrottleBitsPerSecond));
        }

        public Task<UplinkLimitOperationResult> EndMeasurementAsync(long? restoreLimitBitsPerSecond, CancellationToken ct = default)
        {
            Ends.Add((restoreLimitBitsPerSecond, ct.CanBeCanceled));
            if (EndThrows)
            {
                throw new InvalidOperationException("end boom");
            }

            var result = EndResult ?? Ok();
            if (result.Success || EndClearsTrialEvenWhenFailing)
            {
                Trial = null;
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>試験用ポリシーの値から送信速度と遅延を決める偽の測定。</summary>
    private sealed class FakeProbe(FakeLimit limit, FakeClock clock) : IUplinkLoadProbe
    {
        public long GoodLimit = 80_000_000;
        public int Calls;
        public Func<int, UplinkProbeRequest, UplinkProbeResult?>? Override;
        public Action<int>? OnCall;
        public TimeSpan ExtraTime;

        public Task<UplinkProbeResult> RunAsync(UplinkProbeRequest request, CancellationToken ct = default)
        {
            var index = Calls++;
            ct.ThrowIfCancellationRequested();
            OnCall?.Invoke(index);
            clock.Advance(request.Duration + ExtraTime);
            return Task.FromResult(Override?.Invoke(index, request) ?? Model(request));
        }

        private UplinkProbeResult Model(UplinkProbeRequest request)
        {
            var good = limit.Trial is { } trial && trial <= GoodLimit;
            var rate = request.ConnectionCount == 0 ? 0 : Math.Min(Capacity, (long)((limit.Trial ?? long.MaxValue / 2) * 1.03));
            return Result(request, rate, rtt: request.ConnectionCount == 0 ? 10 : good ? 12 : 60);
        }
    }

    private static UplinkProbeResult Result(
        UplinkProbeRequest request, long nicBitsPerSecond, double rtt, double lossFraction = 0, int? pingCount = null, long receiveBytesPerSecond = 0)
    {
        var seconds = (int)request.Duration.TotalSeconds;
        var bytesPerSecond = nicBitsPerSecond / 8;
        var intervals = Enumerable.Range(0, seconds)
            .Select(_ => new UplinkProbeInterval(1000, bytesPerSecond, receiveBytesPerSecond, bytesPerSecond)).ToArray();
        var count = pingCount ?? seconds * 10;
        var lost = (int)(count * lossFraction);
        var pings = Enumerable.Range(0, count)
            .Select(i => new UplinkPingSample(i * 100, i < lost ? null : rtt)).ToArray();
        return new UplinkProbeResult(UplinkProbeStatus.Completed, null, null, intervals, pings, bytesPerSecond * seconds);
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly FakeLimit Limit = new();
        public readonly FakeProbe Probe;
        public readonly UplinkMeasurementService Service;

        public Rig()
        {
            Probe = new FakeProbe(Limit, Clock);
            Service = new UplinkMeasurementService(Limit, Probe, Clock, (span, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Clock.Advance(span);
                return Task.CompletedTask;
            });
        }

        public Task<UplinkMeasurementReport> MeasureAsync(CancellationToken ct = default) => Service.MeasureAsync(null, ct);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private static void AssertCleanedUp(Rig rig, UplinkMeasurementReport report, long? expectedRestore)
    {
        Assert.AreEqual(1, rig.Limit.Ends.Count, "終了処理は 1 回");
        Assert.IsFalse(rig.Limit.Ends[0].TokenCanBeCanceled, "終了処理は CancellationToken.None");
        Assert.AreEqual(expectedRestore, rig.Limit.Ends[0].Restore);
        Assert.IsNull(rig.Limit.Trial, "試験用ポリシーが残っていない");
        Assert.AreEqual(0, rig.Limit.ApplyLimitCalls, "適用用は適用しない");
        Assert.IsTrue(report.CleanupSucceeded);
    }

    // ---- 拒否 (何も変更しないので終了処理は呼ばない) ----

    [TestMethod]
    public async Task Measure_RejectedByPrecheck_DoesNotTouchPolicies()
    {
        var identified = Healthy().Line;
        var cases = new (UplinkLimitState State, UplinkMeasurementOutcome Expected)[]
        {
            (UplinkLimitState.Unreadable("denied"), UplinkMeasurementOutcome.RejectedStateUnreadable),
            (Healthy() with { Line = UplinkLineIdentity.Unknown }, UplinkMeasurementOutcome.RejectedLineUnknown),
            (Healthy() with { Line = identified with { Kind = UplinkAdapterKind.Virtual } }, UplinkMeasurementOutcome.RejectedVirtualAdapter),
            (Healthy() with { OtherActiveStorePolicyNames = ["Corp"] }, UplinkMeasurementOutcome.RejectedOtherPolicies),
            (Healthy() with { OtherDefaultStorePolicyNames = ["Legacy"] }, UplinkMeasurementOutcome.RejectedOtherPolicies),
        };

        foreach (var (state, expected) in cases)
        {
            var rig = new Rig { Limit = { State = state } };

            var report = await rig.MeasureAsync();

            Assert.AreEqual(expected, report.Outcome);
            Assert.AreEqual(0, rig.Limit.BeginCalls);
            Assert.AreEqual(0, rig.Limit.Ends.Count);
            Assert.AreEqual(0, rig.Probe.Calls);
        }
    }

    [TestMethod]
    public async Task Measure_BeginRejected_ReportsOtherPoliciesWithoutCleanup()
    {
        var rig = new Rig { Limit = { BeginResult = Fail(UplinkOperationOutcome.RejectedOtherPolicies, "Corp") } };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.RejectedOtherPolicies, report.Outcome);
        CollectionAssert.AreEqual(new[] { "Corp" }, report.OtherPolicyNames.ToArray());
        Assert.AreEqual(0, rig.Limit.Ends.Count);
    }

    [TestMethod]
    public async Task Measure_BeginFailed_CleansUpAndRestoresTheValueReadBeforehand()
    {
        var rig = new Rig { Limit = { State = Healthy(30_000_000), BeginResult = Fail(UplinkOperationOutcome.Failed) } };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.PolicyFailed, report.Outcome);
        AssertCleanedUp(rig, report, 30_000_000);
    }

    // ---- 結果 ----

    [TestMethod]
    public async Task Measure_FirstLoadPasses_NoChangeNeededAndRestoresPrevious()
    {
        var rig = new Rig { Limit = { State = Healthy(30_000_000) } };
        rig.Probe.Override = (index, request) => index == 1 ? Result(request, Capacity, rtt: 12) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.NoChangeNeeded, report.Outcome);
        Assert.AreEqual(2, rig.Probe.Calls, "無負荷 + 1 回目の負荷で終了");
        Assert.IsNull(report.RecommendedPolicyBitsPerSecond);
        AssertCleanedUp(rig, report, 30_000_000);
        Assert.AreEqual(30_000_000, report.RestoredLimitBitsPerSecond);
    }

    [TestMethod]
    public async Task Measure_Reproducible_RecommendsFastestPassingAndDoesNotRestore()
    {
        var rig = new Rig { Limit = { State = Healthy(30_000_000) } };
        var phases = new List<UplinkMeasurementPhase>();
        var progress = new SyncProgress<UplinkMeasurementProgress>(p => phases.Add(p.Phase));

        var report = await rig.Service.MeasureAsync(progress, CancellationToken.None);

        Assert.AreEqual(UplinkMeasurementOutcome.Recommended, report.Outcome);
        Assert.AreEqual(Capacity, report.CapacityBitsPerSecond);
        Assert.AreEqual(3, report.SearchSteps.Count);
        Assert.AreEqual(80, report.SearchSteps.Where(s => s.Passed).Max(s => s.Percent));
        Assert.AreEqual(report.SearchSteps.Single(s => s.Percent == 80).PolicyBitsPerSecond, report.RecommendedPolicyBitsPerSecond);
        CollectionAssert.AreEqual(new[] { false, true, false, true }, report.VerificationSteps.Select(s => s.LimitApplied).ToArray());
        AssertCleanedUp(rig, report, null);
        Assert.IsNull(report.RestoredLimitBitsPerSecond);
        CollectionAssert.IsSubsetOf(
            new[] { UplinkMeasurementPhase.Idle, UplinkMeasurementPhase.Capacity, UplinkMeasurementPhase.Search, UplinkMeasurementPhase.Verification, UplinkMeasurementPhase.Cleanup },
            phases);
    }

    [TestMethod]
    public async Task Measure_NothingPasses_NoImprovementAndRestoresPrevious()
    {
        var rig = new Rig { Limit = { State = Healthy(30_000_000) } };
        rig.Probe.GoodLimit = 0;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.NoImprovement, report.Outcome);
        Assert.IsTrue(report.SearchSteps.All(s => !s.Passed));
        Assert.IsNull(report.RecommendedPolicyBitsPerSecond);
        AssertCleanedUp(rig, report, 30_000_000);
    }

    [TestMethod]
    public async Task Measure_NotReproducedInVerification_NoImprovement()
    {
        var rig = new Rig();
        // 呼び出し 0:無負荷 1-3:容量 4-6:探索 7-10:検証。最後の「あり」だけ悪化させる。
        rig.Probe.Override = (index, request) => index == 10 ? Result(request, Capacity, rtt: 60) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.NoImprovement, report.Outcome);
        Assert.AreEqual(4, report.VerificationSteps.Count);
        AssertCleanedUp(rig, report, null);
    }

    // ---- 無負荷の判定 ----

    [TestMethod]
    public async Task Measure_IdleTrafficPresent_LineBusy()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 0 ? Result(request, 0, rtt: 10, receiveBytesPerSecond: 500_000) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LineBusy, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_IdleLossOverFivePercent_Unstable()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 0 ? Result(request, 0, rtt: 10, lossFraction: 0.1) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.Unstable, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_IdleTooFewSamples_Unstable()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 0 ? Result(request, 0, rtt: 10, pingCount: 30) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.Unstable, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    // ---- 区間の無効 ----

    [TestMethod]
    public async Task Measure_InvalidSegmentOnce_IsRetriedAndContinues()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index switch
        {
            1 => Result(request, Capacity, rtt: 12, pingCount: 10),
            2 => Result(request, Capacity, rtt: 12),
            _ => null,
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.NoChangeNeeded, report.Outcome);
        Assert.AreEqual(3, rig.Probe.Calls);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_InvalidSegmentTwice_Aborts()
    {
        var rig = new Rig();
        // アプリ送信が NIC 送信の半分しかない = 他の通信がある。
        rig.Probe.Override = (index, request) =>
        {
            if (index == 0)
            {
                return null;
            }

            var r = Result(request, Capacity, rtt: 60);
            return r with { AppSentBytes = r.AppSentBytes / 2 };
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.InvalidSegment, report.Outcome);
        Assert.AreEqual(3, rig.Probe.Calls);
        AssertCleanedUp(rig, report, null);
    }

    // ---- 打ち切り・測定先 ----

    [TestMethod]
    public async Task Measure_TargetError_AbortsWithoutRetry()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 1
            ? new UplinkProbeResult(UplinkProbeStatus.TargetError, "HTTP 429", 429, [], [], 0)
            : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.TargetError, report.Outcome);
        StringAssert.Contains(report.Message, "429");
        Assert.AreEqual(2, rig.Probe.Calls);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_ByteLimitReachedByProbe_LimitReached()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 2
            ? new UplinkProbeResult(UplinkProbeStatus.ByteLimitReached, "limit", null, [], [], 1_000)
            : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LimitReached, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_TotalBytesExhausted_LimitReachedBeforeNextSegment()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => index == 1
            ? Result(request, Capacity, rtt: 60) with { AppSentBytes = UplinkMeasurementService.TotalByteLimit }
            : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LimitReached, report.Outcome);
        Assert.AreEqual(2, rig.Probe.Calls);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_TimeLimit_LimitReached()
    {
        var rig = new Rig();
        rig.Probe.ExtraTime = TimeSpan.FromMinutes(2);

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LimitReached, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    // ---- 中断・例外・回線変更 ----

    [TestMethod]
    public async Task Measure_Cancelled_ReportsCancelledAndCleansUp()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig { Limit = { State = Healthy(30_000_000) } };
        rig.Probe.OnCall = index =>
        {
            if (index == 2)
            {
                cts.Cancel();
            }
        };

        var report = await rig.MeasureAsync(cts.Token);

        Assert.AreEqual(UplinkMeasurementOutcome.Cancelled, report.Outcome);
        AssertCleanedUp(rig, report, 30_000_000);
    }

    [TestMethod]
    public async Task Measure_ProbeThrows_ReportsFailedWithMessageAndCleansUp()
    {
        var rig = new Rig();
        rig.Probe.OnCall = index =>
        {
            if (index == 1)
            {
                throw new InvalidOperationException("NIC gone");
            }
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.Failed, report.Outcome);
        StringAssert.Contains(report.Message, "NIC gone");
        AssertCleanedUp(rig, report, null);
    }

    private static UplinkLimitOperationResult FailedWithCommand(bool requiresAbort) =>
        new(UplinkOperationOutcome.Failed, "ng", [new CommandExecutionResult(false, "apply", -1, string.Empty, "cancelled") { RequiresOperationAbort = requiresAbort }], []);

    [TestMethod]
    public async Task Measure_CancelledWhileTrialCommandRuns_ReportsCancelledNotPolicyFailed()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig { Limit = { State = Healthy(30_000_000), ApplyTrialResult = FailedWithCommand(requiresAbort: false) } };
        rig.Limit.OnApplyTrial = cts.Cancel;

        var report = await rig.MeasureAsync(cts.Token);

        Assert.AreEqual(UplinkMeasurementOutcome.Cancelled, report.Outcome);
        Assert.IsFalse(report.RequiresOperationAbort);
        Assert.AreEqual(1, report.Commands.Count);
        Assert.IsFalse(rig.Limit.MutationTokenCanBeCanceled, "変更コマンドは途中で止めない");
        AssertCleanedUp(rig, report, 30_000_000);
    }

    [TestMethod]
    public async Task Measure_CancelledWithUnconfirmedCommand_KeepsAbortFlagAndSkipsFurtherChanges()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig { Limit = { State = Healthy(30_000_000), ApplyTrialResult = FailedWithCommand(requiresAbort: true) } };
        rig.Limit.OnApplyTrial = cts.Cancel;

        var report = await rig.MeasureAsync(cts.Token);

        Assert.AreEqual(UplinkMeasurementOutcome.Cancelled, report.Outcome);
        Assert.IsTrue(report.RequiresOperationAbort);
        Assert.AreEqual(0, rig.Limit.Ends.Count, "終了を確認できない変更コマンドの後は、後始末の変更コマンドを実行しない");
        Assert.IsFalse(report.TrialPolicyRemoved);
        Assert.IsNotNull(report.CleanupError);
    }

    [TestMethod]
    public async Task Measure_CancelledWhileBeginRuns_ReportsCancelledAndCleansUp()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig { Limit = { BeginResult = FailedWithCommand(requiresAbort: false) } };
        rig.Limit.OnBegin = cts.Cancel;

        var report = await rig.MeasureAsync(cts.Token);

        Assert.AreEqual(UplinkMeasurementOutcome.Cancelled, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_TimeLeftShorterThanSegment_StopsBeforeStartingIt()
    {
        var rig = new Rig();
        rig.Probe.OnCall = index =>
        {
            if (index == 2)
            {
                rig.Clock.Advance(TimeSpan.FromSeconds(245));
            }
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LimitReached, report.Outcome);
        Assert.AreEqual(3, rig.Probe.Calls, "残り 9 秒 < 区間 10 秒なので 3 回目の区間は始めない");
        Assert.IsTrue(report.Elapsed <= UplinkMeasurementService.TotalTimeLimit);
    }

    [TestMethod]
    public async Task Measure_RemainingBytesCannotCoverSearchAndVerify_StopsBeforeSearch()
    {
        var rig = new Rig();
        rig.Probe.Override = (index, request) => (index is >= 1 and <= 3) ? Result(request, 400_000_000, rtt: 60) : null;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.LimitReached, report.Outcome);
        StringAssert.Contains(report.Message, "2GB 以内では計測を完了できません");
        Assert.AreEqual(4, rig.Probe.Calls);
        Assert.AreEqual(0, rig.Limit.AppliedTrials.Count);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_LineChangesMidway_DoesNotRestorePreviousLimit()
    {
        var rig = new Rig { Limit = { State = Healthy(30_000_000) } };
        rig.Probe.OnCall = index =>
        {
            if (index == 1)
            {
                rig.Limit.State = rig.Limit.State with { Line = rig.Limit.State.Line with { GatewayMac = "00-00-00-00-00-01" } };
            }
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.RejectedLineUnknown, report.Outcome);
        StringAssert.Contains(report.Message, "復元していません");
        Assert.IsNull(report.RestoredLimitBitsPerSecond);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_LineChangesMidway_Aborts()
    {
        var rig = new Rig();
        rig.Probe.OnCall = index =>
        {
            if (index == 1)
            {
                rig.Limit.State = rig.Limit.State with { Line = rig.Limit.State.Line with { GatewayMac = "00-00-00-00-00-01" } };
            }
        };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.RejectedLineUnknown, report.Outcome);
        Assert.AreEqual(2, rig.Probe.Calls);
        AssertCleanedUp(rig, report, null);
    }

    [TestMethod]
    public async Task Measure_TrialApplyFails_PolicyFailedAndCleansUp()
    {
        var rig = new Rig { Limit = { ApplyTrialResult = Fail(UplinkOperationOutcome.VerificationFailed) } };

        var report = await rig.MeasureAsync();

        Assert.AreEqual(UplinkMeasurementOutcome.PolicyFailed, report.Outcome);
        AssertCleanedUp(rig, report, null);
    }

    // ---- 後始末の失敗 ----

    [TestMethod]
    public async Task Measure_EndFailsButTrialGone_ReportsCleanupError()
    {
        var rig = new Rig { Limit = { EndResult = Fail(UplinkOperationOutcome.VerificationFailed) } };
        rig.Probe.GoodLimit = 0;

        var report = await rig.MeasureAsync();

        Assert.AreEqual(1, rig.Limit.Ends.Count);
        Assert.IsNotNull(report.CleanupError);
        Assert.IsTrue(report.TrialPolicyRemoved);
        Assert.IsFalse(report.CleanupSucceeded);
        Assert.IsNull(report.RestoredLimitBitsPerSecond);
    }

    [TestMethod]
    public async Task Measure_EndThrowsAndTrialRemains_ReportsTrialNotRemoved()
    {
        var rig = new Rig { Limit = { EndThrows = true } };
        rig.Probe.GoodLimit = 0;

        var report = await rig.MeasureAsync();

        Assert.IsFalse(report.TrialPolicyRemoved);
        StringAssert.Contains(report.CleanupError!, "end boom");
        Assert.IsFalse(report.CleanupSucceeded);
    }
}
