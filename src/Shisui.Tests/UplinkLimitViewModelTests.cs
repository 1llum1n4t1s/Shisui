using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.UI.ViewModels;

namespace Shisui.Tests;

[TestClass]
public class UplinkLimitViewModelTests
{
    private const string Guid1 = "{AAAAAAAA-0000-0000-0000-000000000001}";
    private const string Mac1 = "AA-BB-CC-DD-EE-01";
    private const long LimitValue = 28_000_000;

    // ───────────── 起動時の照合 ─────────────

    [TestMethod]
    public async Task Initialize_TrialRemnant_RemovesItUnderGateOnce()
    {
        var env = new Env { State = State(line: Line(), trial: new("shisuiuplinktrial", LimitValue, true)) };

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, "RemoveTrial");
        Assert.AreEqual(1, env.Gate.EnterCount);
        Assert.AreEqual(0, env.Gate.Active);
        Assert.AreEqual(0, env.Settings.SaveCount);
    }

    [TestMethod]
    public async Task Initialize_NothingToDo_DoesNotTakeGateOrShowBusy()
    {
        var env = new Env { State = State(line: Line()) };
        var busyChanges = new List<bool>();
        env.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UplinkLimitViewModel.IsBusy))
            {
                busyChanges.Add(env.ViewModel.IsBusy);
            }
        };

        await env.ViewModel.InitializeAsync();

        Assert.AreEqual(0, env.Gate.EnterCount);
        Assert.IsEmpty(busyChanges);
        Assert.IsEmpty(env.Limit.Calls);
    }

    [TestMethod]
    public async Task NetworkChange_NoEnabledProfile_DoesNothingAtAll()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };

        await env.ViewModel.ReconcileAfterNetworkChangeAsync();

        Assert.AreEqual(0, env.Limit.StateReads);
        Assert.AreEqual(0, env.Gate.EnterCount);
    }

    [TestMethod]
    public async Task Initialize_LineTemporarilyUnidentified_RetriesWithoutHoldingGateThenKeepsLimit()
    {
        var limit = new UplinkPolicyInfo("shisuiuplinklimit", LimitValue, true);
        var env = new Env();
        env.Limit.StateFactory = call => call <= 2 ? State(UplinkLineIdentity.Unknown, limit) : State(Line(), limit);
        env.AddProfile(enabled: true);
        var gateActiveDuringWait = new List<int>();
        env.ViewModel.DelayAsync = (_, _) =>
        {
            gateActiveDuringWait.Add(env.Gate.Active);
            return Task.CompletedTask;
        };

        await env.ViewModel.InitializeAsync();

        Assert.HasCount(2, gateActiveDuringWait);
        Assert.IsTrue(gateActiveDuringWait.All(active => active == 0));
        CollectionAssert.DoesNotContain(env.Limit.Calls, "RemoveLimit");
        Assert.AreEqual(0, env.Gate.EnterCount);
    }

    [TestMethod]
    public async Task Initialize_LineStaysUnidentified_RemovesLimitAfterRetries()
    {
        var env = new Env { State = State(UplinkLineIdentity.Unknown, limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);
        var waits = 0;
        env.ViewModel.DelayAsync = (_, _) =>
        {
            waits++;
            return Task.CompletedTask;
        };

        await env.ViewModel.InitializeAsync();

        Assert.AreEqual(env.ViewModel.LineRetryCount, waits);
        CollectionAssert.Contains(env.Limit.Calls, "RemoveLimit");
    }

    [TestMethod]
    public async Task Initialize_LineLostInsideGate_DefersJudgmentAndAppliesAfterRecheck()
    {
        var env = new Env();
        // 1: 事前確認 (回線あり・上限なし) / 2: ゲート内の読み直し (回線が一時的に消えた) / 以降: 回線あり
        env.Limit.StateFactory = call => call == 2 ? State(UplinkLineIdentity.Unknown) : State(Line());
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        Assert.AreEqual(1, env.Limit.Calls.Count(call => call == $"ApplyLimit:{LimitValue}"));
        CollectionAssert.DoesNotContain(env.Limit.Calls, "RemoveLimit");
    }

    [TestMethod]
    public async Task Initialize_LimitValueDiffersFromSaved_CorrectsToSavedValue()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", 20_000_000, true)) };
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, $"ApplyLimit:{LimitValue}");
    }

    [TestMethod]
    public async Task Reconcile_OtherPolicies_LineMismatch_StillRemovesOwnLimit()
    {
        var env = new Env
        {
            State = new UplinkLimitState(
                UplinkStateReadStatus.Ok, Line(mac: "AA-BB-CC-DD-EE-99"),
                new("shisuiuplinklimit", LimitValue, true), null, ["VoipPolicy"], []),
        };
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, "RemoveLimit");
        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit")));
    }

    [TestMethod]
    public async Task Reconcile_OtherPolicies_MatchingSavedLimit_RejectsApplyOnly()
    {
        var env = new Env
        {
            State = new UplinkLimitState(UplinkStateReadStatus.Ok, Line(), null, null, ["VoipPolicy"], []),
        };
        env.AddProfile(enabled: true);
        var notices = env.CollectLog();

        await env.ViewModel.InitializeAsync();

        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit") || call == "RemoveLimit"));
        Assert.IsTrue(notices.Any(result => !result.Success && result.StandardError.Contains("VoipPolicy")));
    }

    [TestMethod]
    public async Task Initialize_SavedEnabledMatchingLine_KeepsExistingLimit()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        CollectionAssert.DoesNotContain(env.Limit.Calls, "RemoveLimit");
        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit")));
    }

    [TestMethod]
    public async Task Initialize_SavedEnabledMatchingLineWithoutLimit_ReappliesSavedValue()
    {
        var env = new Env { State = State(line: Line()) };
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, $"ApplyLimit:{LimitValue}");
        Assert.AreEqual(0, env.Settings.SaveCount);
    }

    [TestMethod]
    public async Task Initialize_LineMismatch_RemovesLimitAndNotifies()
    {
        var env = new Env { State = State(line: Line(mac: "AA-BB-CC-DD-EE-99"), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);
        var notices = env.CollectLog();

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, "RemoveLimit");
        Assert.IsTrue(notices.Any(result => result.CommandLine == "上り速度制限の解除" && result.Success));
        Assert.AreEqual(0, env.Settings.SaveCount);
    }

    [TestMethod]
    public async Task Initialize_NoSavedProfile_RemovesLimitWithoutSaving()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };

        await env.ViewModel.InitializeAsync();

        CollectionAssert.Contains(env.Limit.Calls, "RemoveLimit");
        Assert.AreEqual(0, env.Settings.SaveCount);
    }

    [TestMethod]
    public async Task Initialize_StateUnreadable_DoesNotChangeOs()
    {
        var env = new Env { State = UplinkLimitState.Unreadable("denied") };
        env.AddProfile(enabled: true);

        await env.ViewModel.InitializeAsync();

        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit") || call == "RemoveLimit"));
        StringAssert.Contains(env.ViewModel.LimitStateText, "取得不可");
    }

    [TestMethod]
    public async Task NetworkChange_LineMismatch_RemovesAndReappliesWhenBackOnSameLine()
    {
        var env = new Env { State = State(line: Line(mac: "AA-BB-CC-DD-EE-99"), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);

        await env.ViewModel.ReconcileAfterNetworkChangeAsync();
        Assert.AreEqual(1, env.Limit.Calls.Count(call => call == "RemoveLimit"));

        env.Limit.State = State(line: Line());
        await env.ViewModel.ReconcileAfterNetworkChangeAsync();
        Assert.AreEqual(1, env.Limit.Calls.Count(call => call == $"ApplyLimit:{LimitValue}"));
        Assert.IsFalse(env.Limit.Calls.Contains("RemoveTrial"));
        Assert.AreEqual(0, env.Settings.SaveCount);
    }

    [TestMethod]
    public async Task NetworkChange_DebouncedWhileMeasuring_ReconcilesAfterMeasurement()
    {
        var env = new Env { State = State(line: Line()) };
        env.AddProfile(enabled: true);
        env.Measurement.WaitForCancellation = true;
        env.ViewModel.CostAcknowledged = true;
        var running = env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);
        await env.Measurement.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        env.ViewModel.NotifyNetworkChanged(); // 待機を即時にしてあるため、保留フラグまで同期的に進む
        var readsDuringMeasurement = env.Limit.StateReads;

        env.ViewModel.CancelMeasurementCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsGreaterThan(readsDuringMeasurement + 1, env.Limit.StateReads, "計測後に回線の照合が行われていません。");
        CollectionAssert.Contains(env.Limit.Calls, $"ApplyLimit:{LimitValue}");
    }

    [TestMethod]
    public async Task NetworkChange_SameLine_KeepsLimit()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);

        await env.ViewModel.ReconcileAfterNetworkChangeAsync();

        CollectionAssert.DoesNotContain(env.Limit.Calls, "RemoveLimit");
    }

    // ───────────── 計測 ─────────────

    [TestMethod]
    public async Task Measurement_Cancelled_ReleasesGateAndProgress()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.WaitForCancellation = true;
        env.ViewModel.CostAcknowledged = true;

        var running = env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);
        await env.Measurement.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(env.ViewModel.IsMeasuring);
        Assert.IsTrue(env.ViewModel.IsBusy);

        env.ViewModel.CancelMeasurementCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsFalse(env.ViewModel.IsBusy);
        Assert.IsFalse(env.ViewModel.IsMeasuring);
        Assert.AreEqual(string.Empty, env.ViewModel.ProgressText);
        Assert.AreEqual(0, env.Gate.Active);
        Assert.IsFalse(env.ViewModel.CostAcknowledged);
        StringAssert.Contains(env.ViewModel.StatusText, "中止しました");
        Assert.DoesNotContain("終わりました", env.ViewModel.StatusText);
    }

    [TestMethod]
    public async Task Measurement_NotCompleted_KeepsSavedMeasurement()
    {
        var env = new Env { State = State(line: Line()) };
        env.AddProfile(enabled: false);
        var measuredAt = env.Settings.Current.UplinkLimitProfiles[0].MeasuredAtUtc;
        env.Measurement.Report = Report(UplinkMeasurementOutcome.Cancelled, null) with { CapacityBitsPerSecond = 5_000_000 };
        env.ViewModel.CostAcknowledged = true;

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        var profile = env.Settings.Current.UplinkLimitProfiles[0];
        Assert.AreEqual(measuredAt, profile.MeasuredAtUtc);
        Assert.AreEqual(29_000_000, profile.CapacityBitsPerSecond);
        Assert.AreEqual(nameof(UplinkMeasurementOutcome.Recommended), profile.Outcome);
    }

    [TestMethod]
    public async Task Measurement_ReportCommands_AreForwardedToLog()
    {
        var env = new Env { State = State(line: Line()) };
        var command = new CommandExecutionResult(true, "上り速度制限: 適用", 0, "ok", string.Empty);
        env.Measurement.Report = Report(UplinkMeasurementOutcome.NoChangeNeeded, null) with { Commands = [command] };
        env.ViewModel.CostAcknowledged = true;
        var log = env.CollectLog();

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        CollectionAssert.Contains(log, command);
        Assert.IsFalse(env.ViewModel.IsOperationBlocked);
    }

    [TestMethod]
    public async Task Measurement_RequiresOperationAbort_BlocksAndStopsFurtherWork()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.Report = Report(UplinkMeasurementOutcome.Recommended, LimitValue) with { RequiresOperationAbort = true };
        env.ViewModel.CostAcknowledged = true;

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        Assert.IsTrue(env.ViewModel.IsOperationBlocked);
        StringAssert.Contains(env.ViewModel.StatusText, "再起動");
        Assert.AreEqual(0, env.Settings.SaveCount);
        Assert.AreEqual(1, env.Limit.StateReads, "中断後に PowerShell を起動してはいけません (計測前の 1 回だけ)。");
    }

    [TestMethod]
    public async Task Measurement_Failed_ShowsDescription()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.Report = Report(UplinkMeasurementOutcome.Failed, null);
        env.ViewModel.CostAcknowledged = true;

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        StringAssert.Contains(env.ViewModel.VerdictText, "想定外");
    }

    [TestMethod]
    public async Task Measurement_RecommendedWithAppliedLimit_ShowsPreviousValueAndCanRestoreIt()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", 20_000_000, true)) };
        env.Measurement.Report = Report(UplinkMeasurementOutcome.Recommended, LimitValue);
        env.ViewModel.CostAcknowledged = true;

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        Assert.IsTrue(env.ViewModel.HasPreviousLimit);
        StringAssert.Contains(env.ViewModel.ProposalText, "20.0 Mbps");
        StringAssert.Contains(env.ViewModel.ProposalText, "外したまま");

        await env.ViewModel.RestorePreviousLimitCommand.ExecuteAsync(null);

        CollectionAssert.Contains(env.Limit.Calls, "ApplyLimit:20000000");
        var profile = env.Settings.Current.UplinkLimitProfiles.Single();
        Assert.IsTrue(profile.IsEnabled);
        Assert.AreEqual(20_000_000, profile.LimitPolicyBitsPerSecond);
        Assert.IsFalse(env.ViewModel.HasPreviousLimit);
    }

    [TestMethod]
    public async Task Measurement_RecommendedWithoutAppliedLimit_HasNoPreviousLimit()
    {
        var env = await MeasuredEnvAsync();

        Assert.IsFalse(env.ViewModel.HasPreviousLimit);
        Assert.IsFalse(env.ViewModel.RestorePreviousLimitCommand.CanExecute(null));
    }

    // ───────────── アプリ終了 ─────────────

    [TestMethod]
    public async Task Shutdown_WhileMeasuring_CancelsAndWaitsForServiceCleanup()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.WaitForCancellation = true;
        env.Measurement.CleanupDelay = TimeSpan.FromMilliseconds(200);
        env.ViewModel.CostAcknowledged = true;
        var running = env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);
        await env.Measurement.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        env.ViewModel.Shutdown(TimeSpan.FromSeconds(10));

        Assert.IsTrue(env.Measurement.CleanedUp, "終了時に計測の後始末の完了を待っていません。");
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Shutdown_CleanupNeverFinishes_ReturnsAfterTimeout()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.NeverFinish = true;
        env.ViewModel.CostAcknowledged = true;
        _ = env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);
        await env.Measurement.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        env.ViewModel.Shutdown(TimeSpan.FromMilliseconds(300));

        Assert.IsLessThan(TimeSpan.FromSeconds(5), watch.Elapsed);
        env.Measurement.NeverFinishSignal.TrySetResult();
    }

    [TestMethod]
    public async Task Measurement_ServiceThrows_ReleasesGateAndReportsError()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.Throw = new InvalidOperationException("boom");
        env.ViewModel.CostAcknowledged = true;

        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);

        Assert.IsFalse(env.ViewModel.IsBusy);
        Assert.IsFalse(env.ViewModel.IsMeasuring);
        Assert.AreEqual(string.Empty, env.ViewModel.ProgressText);
        Assert.AreEqual(0, env.Gate.Active);
        StringAssert.Contains(env.ViewModel.StatusText, "boom");
    }

    [TestMethod]
    public async Task Measurement_NotAcknowledged_CannotStart()
    {
        var env = new Env { State = State(line: Line()) };

        Assert.IsFalse(env.ViewModel.StartMeasurementCommand.CanExecute(null));
        env.ViewModel.CostAcknowledged = true;
        Assert.IsTrue(env.ViewModel.StartMeasurementCommand.CanExecute(null));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task Measurement_Recommended_SavesProfileDisabledAndShowsProposal()
    {
        var env = await MeasuredEnvAsync();

        var profile = env.Settings.Current.UplinkLimitProfiles.Single();
        Assert.IsFalse(profile.IsEnabled);
        Assert.AreEqual(LimitValue, profile.LimitPolicyBitsPerSecond);
        Assert.IsTrue(env.ViewModel.HasProposal);
        Assert.HasCount(3, env.ViewModel.ResultRows);
        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit")));
    }

    // ───────────── 適用・解除 ─────────────

    [TestMethod]
    public async Task Apply_SaveFails_DoesNotTouchOs()
    {
        var env = await MeasuredEnvAsync();
        env.Settings.FailSave = true;

        await env.ViewModel.ApplyProposalCommand.ExecuteAsync(null);

        Assert.IsFalse(env.Limit.Calls.Any(call => call.StartsWith("ApplyLimit")));
        Assert.IsFalse(env.Settings.Current.UplinkLimitProfiles.Single().IsEnabled);
        StringAssert.Contains(env.ViewModel.StatusText, "保存できなかった");
    }

    [TestMethod]
    public async Task Apply_Succeeds_SavesBeforeApplyingAndEnablesProfile()
    {
        var env = await MeasuredEnvAsync();
        env.Order.Clear();

        await env.ViewModel.ApplyProposalCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "Save", $"ApplyLimit:{LimitValue}" }, env.Order.Where(item => item != "State").ToArray());
        Assert.IsTrue(env.Settings.Current.UplinkLimitProfiles.Single().IsEnabled);
        StringAssert.Contains(env.ViewModel.StatusText, "適用しました");
    }

    [TestMethod]
    public async Task Apply_OsFails_SavesDisabled()
    {
        var env = await MeasuredEnvAsync();
        env.Limit.ApplyResult = new(UplinkOperationOutcome.Failed, "denied", [], []);
        env.Settings.SavedEnabledValues.Clear();

        await env.ViewModel.ApplyProposalCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { true, false }, env.Settings.SavedEnabledValues);
        Assert.IsFalse(env.Settings.Current.UplinkLimitProfiles.Single().IsEnabled);
        StringAssert.Contains(env.ViewModel.StatusText, "適用できませんでした");
    }

    [TestMethod]
    public async Task Remove_SaveFailsAfterOsRemoval_ReportsSeparately()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);
        env.Settings.FailSave = true;

        await env.ViewModel.RemoveLimitCommand.ExecuteAsync(null);

        Assert.AreEqual(1, env.Limit.Calls.Count(call => call == "RemoveLimit"));
        StringAssert.Contains(env.ViewModel.StatusText, "設定を保存できませんでした");
        Assert.IsTrue(env.Settings.Current.UplinkLimitProfiles.Single().IsEnabled);
    }

    [TestMethod]
    public async Task Remove_Succeeds_DisablesProfileAfterOsRemoval()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };
        env.AddProfile(enabled: true);
        env.Order.Clear();

        await env.ViewModel.RemoveLimitCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "RemoveLimit", "Save" }, env.Order.Where(item => item != "State").ToArray());
        Assert.IsFalse(env.Settings.Current.UplinkLimitProfiles.Single().IsEnabled);
    }

    [TestMethod]
    public async Task Remove_RequiresOperationAbort_BlocksFurtherChanges()
    {
        var env = new Env { State = State(line: Line(), limit: new("shisuiuplinklimit", LimitValue, true)) };
        var aborted = new CommandExecutionResult(false, "remove", -1, string.Empty, "hung") { RequiresOperationAbort = true };
        env.Limit.RemoveResult = new(UplinkOperationOutcome.Failed, "hung", [aborted], []);

        await env.ViewModel.RemoveLimitCommand.ExecuteAsync(null);

        Assert.IsTrue(env.ViewModel.IsOperationBlocked);
        StringAssert.Contains(env.ViewModel.StatusText, "再起動");
        Assert.IsFalse(env.ViewModel.RemoveLimitCommand.CanExecute(null));
    }

    // ───────────── ヘルパー ─────────────

    private static async Task<Env> MeasuredEnvAsync()
    {
        var env = new Env { State = State(line: Line()) };
        env.Measurement.Report = Report(UplinkMeasurementOutcome.Recommended, LimitValue);
        env.ViewModel.CostAcknowledged = true;
        await env.ViewModel.StartMeasurementCommand.ExecuteAsync(null);
        return env;
    }

    private static UplinkLineIdentity Line(string mac = Mac1) =>
        new(UplinkLineStatus.Identified, "Ethernet", Guid1, UplinkAdapterKind.Physical, mac);

    private static UplinkLimitState State(UplinkLineIdentity line, UplinkPolicyInfo? limit = null, UplinkPolicyInfo? trial = null) =>
        new(UplinkStateReadStatus.Ok, line, limit, trial, [], []);

    private static UplinkSegmentStats Stats(double p90) =>
        new(200, 0, p90, 26_000_000, 100_000, 1_000_000, true, UplinkSegmentInvalidReason.None);

    private static UplinkMeasurementReport Report(UplinkMeasurementOutcome outcome, long? recommended) =>
        new(
            outcome,
            string.Empty,
            Line(),
            DateTimeOffset.UtcNow,
            recommended is null ? null : 29_000_000,
            recommended,
            Stats(10),
            Stats(80),
            recommended is null ? null : Stats(15),
            [],
            [],
            1_000_000,
            TimeSpan.FromSeconds(60),
            [],
            null,
            true,
            null);

    private sealed class Env
    {
        public Env()
        {
            Order = [];
            Gate = new FakeGate();
            Settings = new FakeSettings(Order);
            Limit = new FakeLimitService(Order);
            Measurement = new FakeMeasurementService { Report = Report(UplinkMeasurementOutcome.NoChangeNeeded, null) };
            ViewModel = new UplinkLimitViewModel(Limit, Measurement, Settings, Gate)
            {
                DelayAsync = (_, _) => Task.CompletedTask, // 実時間を待たない
            };
        }

        public List<string> Order { get; }
        public FakeGate Gate { get; }
        public FakeSettings Settings { get; }
        public FakeLimitService Limit { get; }
        public FakeMeasurementService Measurement { get; }
        public UplinkLimitViewModel ViewModel { get; }

        public UplinkLimitState State
        {
            set => Limit.State = value;
        }

        public void AddProfile(bool enabled) =>
            Settings.Current.UplinkLimitProfiles.Add(new UplinkLimitProfile
            {
                AdapterGuid = Guid1,
                GatewayMac = Mac1,
                MeasuredAtUtc = DateTimeOffset.UtcNow,
                CapacityBitsPerSecond = 29_000_000,
                LimitPolicyBitsPerSecond = LimitValue,
                Outcome = nameof(UplinkMeasurementOutcome.Recommended),
                IsEnabled = enabled,
            });

        public List<CommandExecutionResult> CollectLog()
        {
            var list = new List<CommandExecutionResult>();
            ViewModel.CommandExecuted += (_, result) => list.Add(result);
            return list;
        }
    }

    private sealed class FakeGate : INetworkMutationGate
    {
        public int EnterCount { get; private set; }
        public int Active { get; private set; }

        public Task<IDisposable> EnterAsync(CancellationToken ct = default)
        {
            EnterCount++;
            Active++;
            return Task.FromResult<IDisposable>(new Lease(this));
        }

        private sealed class Lease(FakeGate owner) : IDisposable
        {
            public void Dispose() => owner.Active--;
        }
    }

    private sealed class FakeSettings(List<string> order) : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool FailSave { get; set; }
        public int SaveCount { get; private set; }
        public List<bool> SavedEnabledValues { get; } = [];

        public Task SaveAsync(CancellationToken ct = default)
        {
            order.Add("Save");
            if (FailSave)
            {
                return Task.FromException(new IOException("disk full"));
            }

            SaveCount++;
            SavedEnabledValues.AddRange(Current.UplinkLimitProfiles.Select(profile => profile.IsEnabled));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLimitService(List<string> order) : IUplinkLimitService
    {
        public List<string> Calls { get; } = [];
        public UplinkLimitState State { get; set; } = UplinkLimitState.Unreadable("unset");

        /// <summary>状態の読み取り回数 (1 始まり) から返す状態を決める。null なら <see cref="State"/>。</summary>
        public Func<int, UplinkLimitState>? StateFactory { get; set; }

        public int StateReads { get; private set; }
        public UplinkLimitOperationResult ApplyResult { get; set; } = Ok();
        public UplinkLimitOperationResult RemoveResult { get; set; } = Ok();

        private static UplinkLimitOperationResult Ok() => new(UplinkOperationOutcome.Succeeded, "ok", [], []);

        private UplinkLimitOperationResult Record(string call, UplinkLimitOperationResult? result = null)
        {
            Calls.Add(call);
            order.Add(call);
            return result ?? Ok();
        }

        public Task<UplinkLimitState> GetStateAsync(CancellationToken ct = default)
        {
            order.Add("State");
            StateReads++;
            return Task.FromResult(StateFactory?.Invoke(StateReads) ?? State);
        }

        public Task<UplinkLimitOperationResult> ApplyLimitAsync(long bitsPerSecond, CancellationToken ct = default) =>
            Task.FromResult(Record($"ApplyLimit:{bitsPerSecond}", ApplyResult));

        public Task<UplinkLimitOperationResult> RemoveLimitAsync(CancellationToken ct = default) =>
            Task.FromResult(Record("RemoveLimit", RemoveResult));

        public Task<UplinkLimitOperationResult> ApplyTrialAsync(long bitsPerSecond, CancellationToken ct = default) =>
            Task.FromResult(Record($"ApplyTrial:{bitsPerSecond}"));

        public Task<UplinkLimitOperationResult> RemoveTrialAsync(CancellationToken ct = default) =>
            Task.FromResult(Record("RemoveTrial"));

        public Task<UplinkLimitOperationResult> BeginMeasurementAsync(CancellationToken ct = default) =>
            Task.FromResult(Record("BeginMeasurement"));

        public Task<UplinkLimitOperationResult> EndMeasurementAsync(long? restoreLimitBitsPerSecond, CancellationToken ct = default) =>
            Task.FromResult(Record("EndMeasurement"));
    }

    private sealed class FakeMeasurementService : IUplinkMeasurementService
    {
        public UplinkMeasurementReport Report { get; set; } = null!;
        public bool WaitForCancellation { get; set; }
        public Exception? Throw { get; set; }

        /// <summary>キャンセル後、後始末に時間がかかるサービスを模す。</summary>
        public TimeSpan CleanupDelay { get; set; }

        public bool CleanedUp { get; private set; }

        /// <summary>キャンセルに応じず、後始末も終わらないサービスを模す。</summary>
        public bool NeverFinish { get; set; }

        public TaskCompletionSource NeverFinishSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<UplinkMeasurementReport> MeasureAsync(
            IProgress<UplinkMeasurementProgress>? progress = null,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            if (Throw is not null)
            {
                throw Throw;
            }

            if (NeverFinish)
            {
                await NeverFinishSignal.Task;
            }

            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    await Task.Delay(CleanupDelay, CancellationToken.None);
                    CleanedUp = true;
                    return UplinkLimitViewModelTests.Report(UplinkMeasurementOutcome.Cancelled, null);
                }
            }

            return Report;
        }
    }
}
