using System.Globalization;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 上り回線の計測 (① 無負荷 → ② 容量 → ③ 二分探索 → ④ 検証)。適用はしない。
/// 開始後は必ず finally で <see cref="IUplinkLimitService.EndMeasurementAsync"/> (CancellationToken.None) を呼ぶ。
/// 拒否 (事前確認での他ポリシー・仮想 NIC など) は何も変更していないので呼ばない。
/// 時刻は <see cref="TimeProvider"/>、待機は delay で注入できる (テストで実時間を待たない)。
/// </summary>
public sealed class UplinkMeasurementService : IUplinkMeasurementService
{
    /// <summary>アプリ送信の総量上限 (2GB)。</summary>
    public const long TotalByteLimit = 2_000_000_000L;

    /// <summary>計測全体の時間上限 (5 分)。</summary>
    public static readonly TimeSpan TotalTimeLimit = TimeSpan.FromMinutes(5);

    private const int LoadConnections = 4;
    private static readonly TimeSpan IdleDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CapacityDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SearchDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan VerifyDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettleDuration = TimeSpan.FromSeconds(3);
    private const int CapacityRuns = 3;

    private readonly IUplinkLimitService _limit;
    private readonly IUplinkLoadProbe _probe;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public UplinkMeasurementService(
        IUplinkLimitService limit,
        IUplinkLoadProbe probe,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _limit = limit;
        _probe = probe;
        _time = timeProvider ?? TimeProvider.System;
        _delay = delay ?? Task.Delay;
    }

    public async Task<UplinkMeasurementReport> MeasureAsync(
        IProgress<UplinkMeasurementProgress>? progress = null, CancellationToken ct = default)
    {
        var run = new MeasurementRun(progress, _time.GetTimestamp());
        try
        {
            await PrepareAsync(run, ct).ConfigureAwait(false);
            await RunStagesAsync(run, ct).ConfigureAwait(false);
        }
        catch (MeasurementAbortException abort)
        {
            run.Outcome = abort.Outcome;
            run.Message = abort.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Outcome = UplinkMeasurementOutcome.Cancelled;
            run.Message = "計測をキャンセルしました。";
        }
        catch (Exception ex)
        {
            run.Outcome = UplinkMeasurementOutcome.Failed;
            run.Message = $"計測中に想定外の失敗が起きました ({ex.GetType().Name}: {ex.Message})。";
        }
        finally
        {
            await CleanupAsync(run).ConfigureAwait(false);
        }

        return BuildReport(run);
    }

    // ---- 準備 ----

    private async Task PrepareAsync(MeasurementRun run, CancellationToken ct)
    {
        run.Report(UplinkMeasurementPhase.Preparing, "回線と QoS ポリシーを確認しています。", _time);
        var state = await _limit.GetStateAsync(ct).ConfigureAwait(false);
        if (!state.IsReadable)
        {
            ct.ThrowIfCancellationRequested(); // 実行中のキャンセルで読み取りに失敗した場合は、失敗ではなくキャンセルとして扱う。
            throw Abort(UplinkMeasurementOutcome.RejectedStateUnreadable,
                $"QoS ポリシーの状態を読み取れません。{state.Detail}");
        }

        run.Line = state.Line;
        if (!state.Line.IsIdentified)
        {
            throw Abort(UplinkMeasurementOutcome.RejectedLineUnknown,
                "既定経路またはゲートウェイの MAC を特定できないため、計測できません。");
        }

        if (state.Line.Kind == UplinkAdapterKind.Virtual)
        {
            throw Abort(UplinkMeasurementOutcome.RejectedVirtualAdapter,
                $"既定経路が仮想 NIC ({state.Line.AdapterName}) です。VPN などを切断してから計測してください。");
        }

        run.OtherPolicyNames = state.OtherPolicyNames;
        if (run.OtherPolicyNames.Count > 0)
        {
            throw Abort(UplinkMeasurementOutcome.RejectedOtherPolicies,
                $"Shisui 以外の QoS ポリシーがあるため計測できません: {string.Join(", ", run.OtherPolicyNames)}");
        }

        // Begin が途中で失敗・中断しても元の値へ戻せるよう、読み取り済みの値を控えておく。
        run.Previous = state.LimitPolicy is { IsDefaultTemplate: true } p && UplinkLimitPolicy.IsValidRate(p.ThrottleBitsPerSecond)
            ? p.ThrottleBitsPerSecond
            : null;

        // 変更コマンドを途中で止めると「終了を確認できない」扱いになり後始末もできなくなるため、
        // キャンセルは変更コマンドの前後で判定し、実行中には渡さない (サービス側の 30 秒の期限は有効)。
        ct.ThrowIfCancellationRequested();
        run.NeedsCleanup = true;
        var begin = await _limit.BeginMeasurementAsync(CancellationToken.None).ConfigureAwait(false);
        run.Collect(begin);
        if (begin.Success)
        {
            run.Previous = begin.PreviousLimitBitsPerSecond;
            ct.ThrowIfCancellationRequested();
            return;
        }

        var (outcome, changed) = begin.Outcome switch
        {
            UplinkOperationOutcome.RejectedOtherPolicies => (UplinkMeasurementOutcome.RejectedOtherPolicies, false),
            UplinkOperationOutcome.StateUnreadable => (UplinkMeasurementOutcome.RejectedStateUnreadable, false),
            _ => (UplinkMeasurementOutcome.PolicyFailed, true),
        };
        run.NeedsCleanup = changed;
        if (begin.OtherPolicyNames.Count > 0)
        {
            run.OtherPolicyNames = begin.OtherPolicyNames;
        }

        ct.ThrowIfCancellationRequested(); // 準備中のキャンセル。後始末の要否 (NeedsCleanup) は上で決めた値のまま。
        throw Abort(outcome, begin.Message);
    }

    // ---- 段階 ----

    private async Task RunStagesAsync(MeasurementRun run, CancellationToken ct)
    {
        await MeasureIdleAsync(run, ct).ConfigureAwait(false);
        if (await MeasureCapacityAsync(run, ct).ConfigureAwait(false))
        {
            return;
        }

        EnsureByteBudgetForRemainingStages(run);
        var best = await SearchAsync(run, ct).ConfigureAwait(false);
        await VerifyAsync(run, best, ct).ConfigureAwait(false);
    }

    private async Task MeasureIdleAsync(MeasurementRun run, CancellationToken ct)
    {
        var idle = await RunSegmentAsync(run, new SegmentPlan(
            UplinkMeasurementPhase.Idle, "① 無負荷の回線を観測しています (20 秒)。",
            IdleDuration, 0, null, 0, IsIdle: true), ct).ConfigureAwait(false);
        run.Idle = idle;

        if (UplinkLimitMath.IsLineBusy(idle))
        {
            throw Abort(UplinkMeasurementOutcome.LineBusy,
                $"他の通信があります (送信 {Mbps(idle.NicSendMeanBitsPerSecond)} / 受信 {Mbps(idle.NicReceiveMeanBitsPerSecond)})。通信を止めてから計測してください。");
        }

        if (UplinkLimitMath.IsIdleUnstable(idle))
        {
            throw Abort(UplinkMeasurementOutcome.Unstable,
                $"回線が不安定です (ICMP 標本 {idle.PingSampleCount} 件・損失 {idle.LossPercent:0.#}%)。時間をおいて再度計測してください。");
        }
    }

    /// <summary>② 容量。1 回目で合格なら変更不要として true。</summary>
    private async Task<bool> MeasureCapacityAsync(MeasurementRun run, CancellationToken ct)
    {
        var idle = run.Idle!;
        var means = new List<double>();
        for (var i = 1; i <= CapacityRuns; i++)
        {
            var stats = await RunSegmentAsync(run, new SegmentPlan(
                UplinkMeasurementPhase.Capacity, $"② 4 接続で上りを測定しています ({i}/{CapacityRuns})。",
                CapacityDuration, LoadConnections, null, UplinkLimitMath.WarmupMilliseconds, IsIdle: false), ct).ConfigureAwait(false);
            means.Add(stats.NicSendMeanBitsPerSecond);

            if (i == 1)
            {
                run.Loaded = stats;
                if (UplinkLimitMath.Passes(idle, stats))
                {
                    run.Capacity = (long)stats.NicSendMeanBitsPerSecond;
                    run.Outcome = UplinkMeasurementOutcome.NoChangeNeeded;
                    run.Message = "上り負荷をかけても遅延・損失が悪化しないため、制限は不要です。";
                    return true;
                }
            }
        }

        var capacity = means.Min();
        if (capacity <= 0)
        {
            throw Abort(UplinkMeasurementOutcome.InvalidSegment, "NIC の送信速度を取得できませんでした。");
        }

        run.Capacity = (long)capacity;
        return false;
    }

    /// <summary>③ 二分探索。最速の合格を返す。無ければ NoImprovement で中止。</summary>
    private async Task<UplinkSearchStep> SearchAsync(MeasurementRun run, CancellationToken ct)
    {
        var capacity = run.Capacity!.Value;
        var history = new List<(int Index, bool Passed)>();
        var ratio = UplinkLimitMath.DefaultNicToPolicyRatio;

        while (UplinkLimitMath.NextSearchIndex(history) is { } index)
        {
            var percent = UplinkLimitMath.SearchPercents[index];
            var policy = UplinkLimitMath.ToPolicyBitsPerSecond(capacity * percent / 100.0, ratio);
            var stats = await RunSegmentAsync(run, new SegmentPlan(
                UplinkMeasurementPhase.Search, $"③ 上限 {percent}% ({Mbps(policy)}) を試しています。",
                SearchDuration, LoadConnections, policy, UplinkLimitMath.WarmupMilliseconds, IsIdle: false), ct).ConfigureAwait(false);

            var passed = UplinkLimitMath.Passes(run.Idle!, stats);
            if (history.Count == 0)
            {
                // 最初の試験で、設定値と NIC 実測の比を得て、以降の換算へ反映する。
                ratio = UplinkLimitMath.CalibrateRatio(stats.NicSendMeanBitsPerSecond, policy);
            }

            run.SearchSteps.Add(new UplinkSearchStep(percent, policy, passed, stats));
            history.Add((index, passed));
        }

        if (UplinkLimitMath.BestPassedIndex(history) is not { } best)
        {
            throw Abort(UplinkMeasurementOutcome.NoImprovement, "制限しても遅延・損失が改善しませんでした。制限は適用しません。");
        }

        var step = run.SearchSteps[history.FindIndex(h => h.Index == best)];
        run.Limited = step.Stats;
        return step;
    }

    /// <summary>④ なし→あり→なし→あり。再現したときだけ Recommended。</summary>
    private async Task VerifyAsync(MeasurementRun run, UplinkSearchStep best, CancellationToken ct)
    {
        for (var i = 0; i < 4; i++)
        {
            var applied = i % 2 == 1;
            var stats = await RunSegmentAsync(run, new SegmentPlan(
                UplinkMeasurementPhase.Verification,
                $"④ 上限{(applied ? "あり" : "なし")}で比べています ({i + 1}/4)。",
                VerifyDuration, LoadConnections, applied ? best.PolicyBitsPerSecond : null,
                UplinkLimitMath.WarmupMilliseconds, IsIdle: false), ct).ConfigureAwait(false);
            run.VerificationSteps.Add(new UplinkVerificationStep(applied, stats));
        }

        if (!UplinkLimitMath.IsReproduced(run.Idle!, run.VerificationSteps))
        {
            throw Abort(UplinkMeasurementOutcome.NoImprovement,
                $"上限ありの効果を再現できませんでした: {string.Join(" / ", run.VerificationSteps.Select(DescribeVerification))}");
        }

        run.Limited = run.VerificationSteps[3].Stats;
        run.Recommended = best.PolicyBitsPerSecond;
        run.Outcome = UplinkMeasurementOutcome.Recommended;
        run.Message = $"上限 {Mbps(best.PolicyBitsPerSecond)} を提案します (検証で再現を確認)。";
    }

    // ---- 1 区間 ----

    private sealed record SegmentPlan(
        UplinkMeasurementPhase Phase,
        string Message,
        TimeSpan Duration,
        int Connections,
        long? TrialBitsPerSecond,
        long WarmupMilliseconds,
        bool IsIdle);

    /// <summary>1 区間を実行する。標本不足・アプリ/NIC 不一致は 1 回だけやり直し、再び無効なら中止する。</summary>
    private async Task<UplinkSegmentStats> RunSegmentAsync(MeasurementRun run, SegmentPlan plan, CancellationToken ct)
    {
        UplinkSegmentStats? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            CheckBudget(run, plan.Duration);
            if (run.SegmentCount > 0)
            {
                await EnsureLineUnchangedAsync(run, ct).ConfigureAwait(false);
            }

            await ApplyTrialPolicyAsync(run, plan.TrialBitsPerSecond, ct).ConfigureAwait(false);
            if (run.SegmentCount > 0)
            {
                await _delay(SettleDuration, ct).ConfigureAwait(false);
            }

            run.SegmentCount++;
            run.Report(plan.Phase, attempt == 1 ? plan.Message : plan.Message + " (やり直し)", _time);
            var request = new UplinkProbeRequest(plan.Duration, plan.Connections, run.Line.AdapterGuid, TotalByteLimit - run.TotalAppSentBytes);
            var result = await _probe.RunAsync(request, ct).ConfigureAwait(false);
            run.TotalAppSentBytes += result.AppSentBytes;
            ThrowIfProbeStopped(run, result);

            last = UplinkLimitMath.Analyze(result, plan.WarmupMilliseconds, checkAppNicMismatch: plan.Connections > 0);
            if (!NeedsRetry(last, plan.IsIdle))
            {
                return last;
            }
        }

        throw Abort(UplinkMeasurementOutcome.InvalidSegment,
            $"区間の測定が 2 回続けて無効でした ({DescribeInvalid(last!.InvalidReason)})。他の通信がないか確認して再度計測してください。");
    }

    /// <summary>無負荷は結果そのもの (不安定・他通信) として扱う。負荷区間で ICMP が全滅したのは「遅延が悪化した」観測として有効。</summary>
    private static bool NeedsRetry(UplinkSegmentStats stats, bool isIdle) =>
        !isIdle && stats.InvalidReason is UplinkSegmentInvalidReason.TooFewSamples or UplinkSegmentInvalidReason.AppNicMismatch;

    private void ThrowIfProbeStopped(MeasurementRun run, UplinkProbeResult result)
    {
        switch (result.Status)
        {
            case UplinkProbeStatus.TargetError:
                var phase = run.Idle is null ? "無負荷の観測中" : "無負荷の観測は完了済みで、負荷の送信中";
                throw Abort(UplinkMeasurementOutcome.TargetError,
                    $"測定先 (speed.cloudflare.com) がエラーを返したか接続できませんでした ({result.Detail})。{phase}のため、"
                    + "回線ではなく測定先側の問題の可能性があります。自動の再試行はしません。");
            case UplinkProbeStatus.ByteLimitReached:
                throw Abort(UplinkMeasurementOutcome.LimitReached, "送信量の上限 (2GB) に達したため未完了です。制限は適用しません。");
        }
    }

    /// <summary>
    /// ③④ に最大で必要な送信量 (③ 20 秒 × 3 + ④ 10 秒 × 4 を ② の容量で送ると見積もる) が、残りの総量に収まらなければ
    /// ③ の前に止める (途中で上限に当たって無駄な通信をしないため)。
    /// </summary>
    private void EnsureByteBudgetForRemainingStages(MeasurementRun run)
    {
        var seconds = SearchDuration.TotalSeconds * UplinkLimitMath.MaxSearchTrials + VerifyDuration.TotalSeconds * 4;
        var needed = (long)Math.Ceiling(run.Capacity!.Value / 8.0 * seconds);
        var remaining = TotalByteLimit - run.TotalAppSentBytes;
        if (needed > remaining)
        {
            throw Abort(UplinkMeasurementOutcome.LimitReached,
                $"回線が速く、上限の 2GB 以内では計測を完了できません (残りの検証に最大 約 {needed / 1_000_000} MB が必要で、残りは {Math.Max(0, remaining) / 1_000_000} MB)。制限は適用しません。");
        }
    }

    /// <summary>時間は、区間を終えるまでに上限を超えるとき (残り &lt; 区間長) は開始前に止める。</summary>
    private void CheckBudget(MeasurementRun run, TimeSpan segmentDuration)
    {
        if (TotalTimeLimit - _time.GetElapsedTime(run.StartTimestamp) < segmentDuration)
        {
            throw Abort(UplinkMeasurementOutcome.LimitReached, "計測時間の上限 (5 分) に達したため未完了です。制限は適用しません。");
        }

        if (TotalByteLimit - run.TotalAppSentBytes <= 0)
        {
            throw Abort(UplinkMeasurementOutcome.LimitReached, "送信量の上限 (2GB) に達したため未完了です。制限は適用しません。");
        }
    }

    private async Task EnsureLineUnchangedAsync(MeasurementRun run, CancellationToken ct)
    {
        var state = await _limit.GetStateAsync(ct).ConfigureAwait(false);
        if (!state.IsReadable)
        {
            ct.ThrowIfCancellationRequested();
            throw Abort(UplinkMeasurementOutcome.RejectedStateUnreadable, $"計測中に QoS ポリシーの状態を読み取れなくなりました。{state.Detail}");
        }

        if (!state.Line.Matches(run.Line.AdapterGuid, run.Line.GatewayMac))
        {
            // 新しい回線に古い回線の上限をかけないよう、後始末で元の上限を復元しない。
            run.LineChanged = true;
            throw Abort(UplinkMeasurementOutcome.RejectedLineUnknown,
                "計測中に回線 (NIC またはゲートウェイ) が変わったため中止しました。元の上限は新しい回線に合わないため復元していません (必要なら計測し直してください)。");
        }

        if (state.OtherPolicyNames.Count > 0)
        {
            run.OtherPolicyNames = state.OtherPolicyNames;
            throw Abort(UplinkMeasurementOutcome.RejectedOtherPolicies,
                $"計測中に Shisui 以外の QoS ポリシーが現れたため中止しました: {string.Join(", ", state.OtherPolicyNames)}");
        }
    }

    /// <summary>区間に必要な試験用ポリシーの状態にする (値が同じなら何もしない)。</summary>
    private async Task ApplyTrialPolicyAsync(MeasurementRun run, long? wanted, CancellationToken ct)
    {
        if (run.TrialBitsPerSecond == wanted)
        {
            return;
        }

        // 実行中の変更コマンドは止めない (PrepareAsync のコメント参照)。
        ct.ThrowIfCancellationRequested();
        var result = wanted is { } bps
            ? await _limit.ApplyTrialAsync(bps, CancellationToken.None).ConfigureAwait(false)
            : await _limit.RemoveTrialAsync(CancellationToken.None).ConfigureAwait(false);
        run.Collect(result);
        if (!result.Success)
        {
            ct.ThrowIfCancellationRequested(); // 実行中のキャンセルは失敗にしない (中断フラグは Collect 済みで報告に残る)。
            throw Abort(UplinkMeasurementOutcome.PolicyFailed, $"試験用ポリシーの{(wanted is null ? "削除" : "適用")}に失敗しました。{result.Message}");
        }

        run.TrialBitsPerSecond = wanted;
    }

    // ---- 後始末・報告 ----

    private async Task CleanupAsync(MeasurementRun run)
    {
        if (!run.NeedsCleanup)
        {
            run.TrialPolicyRemoved = true;
            return;
        }

        if (run.RequiresAbort)
        {
            // 既存規約: 変更コマンドの終了を確認できない間は、以降の変更コマンドを実行しない。
            run.TrialPolicyRemoved = false;
            run.CleanupError = "変更コマンドの終了を確認できないため、試験用ポリシーの後始末を行っていません。試験用ポリシーが残っている可能性があります (再起動で消えます)。";
            return;
        }

        run.Report(UplinkMeasurementPhase.Cleanup, "試験用ポリシーを削除しています。", _time);
        // Recommended は適用用を復元せず、利用者が適用するときに新しい値を入れる。
        // 回線が変わって中止したときも、新しい回線へ古い上限をかけないため復元しない。それ以外は元の値へ戻す。
        var restore = run.Outcome == UplinkMeasurementOutcome.Recommended || run.LineChanged ? null : run.Previous;
        try
        {
            var end = await _limit.EndMeasurementAsync(restore, CancellationToken.None).ConfigureAwait(false);
            run.Collect(end);
            if (end.Success)
            {
                run.TrialPolicyRemoved = true;
                run.Restored = restore;
                return;
            }

            run.CleanupError = end.Message;
            if (run.RequiresAbort)
            {
                run.TrialPolicyRemoved = false;
                return;
            }
        }
        catch (Exception ex)
        {
            run.CleanupError = $"{ex.GetType().Name}: {ex.Message}";
        }

        run.TrialPolicyRemoved = await IsTrialAbsentAsync().ConfigureAwait(false);
    }

    private async Task<bool> IsTrialAbsentAsync()
    {
        try
        {
            var state = await _limit.GetStateAsync(CancellationToken.None).ConfigureAwait(false);
            return state.IsReadable && state.TrialPolicy is null;
        }
        catch (Exception)
        {
            return false; // 確認できない = 削除を確認できていない。
        }
    }

    private UplinkMeasurementReport BuildReport(MeasurementRun run) => new(
        run.Outcome,
        run.Message,
        run.Line,
        _time.GetUtcNow(),
        run.Capacity,
        run.Recommended,
        run.Idle,
        run.Loaded,
        run.Limited,
        run.SearchSteps,
        run.VerificationSteps,
        run.TotalAppSentBytes,
        _time.GetElapsedTime(run.StartTimestamp),
        run.OtherPolicyNames,
        run.Restored,
        run.TrialPolicyRemoved,
        run.CleanupError)
    {
        Commands = run.Commands.ToArray(),
        RequiresOperationAbort = run.RequiresAbort,
    };

    private static string Mbps(double bitsPerSecond) =>
        string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1_000_000:0.#} Mbps");

    private static string DescribeVerification(UplinkVerificationStep step) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(step.LimitApplied ? "あり" : "なし")} p90 {step.Stats.RttP90Ms:0.#} ms・損失 {step.Stats.LossPercent:0.#}%");

    private static string DescribeInvalid(UplinkSegmentInvalidReason reason) => reason switch
    {
        UplinkSegmentInvalidReason.TooFewSamples => "ICMP の標本が不足",
        UplinkSegmentInvalidReason.AppNicMismatch => "NIC の送信量がアプリの送信量より大幅に多い (他の通信の可能性)",
        UplinkSegmentInvalidReason.AllPingLost => "ICMP が全滅",
        _ => "不明",
    };

    private static MeasurementAbortException Abort(UplinkMeasurementOutcome outcome, string message) => new(outcome, message);

    private sealed class MeasurementAbortException(UplinkMeasurementOutcome outcome, string message) : Exception(message)
    {
        public UplinkMeasurementOutcome Outcome { get; } = outcome;
    }

    /// <summary>1 回の計測の可変状態。</summary>
    private sealed class MeasurementRun(IProgress<UplinkMeasurementProgress>? progress, long startTimestamp)
    {
        public long StartTimestamp { get; } = startTimestamp;
        public UplinkMeasurementOutcome Outcome { get; set; } = UplinkMeasurementOutcome.Failed;
        public string Message { get; set; } = "計測を完了できませんでした。";
        public UplinkLineIdentity Line { get; set; } = UplinkLineIdentity.Unknown;
        public IReadOnlyList<string> OtherPolicyNames { get; set; } = [];
        public long? Previous { get; set; }
        public bool NeedsCleanup { get; set; }
        public long? TrialBitsPerSecond { get; set; }
        public int SegmentCount { get; set; }
        public long TotalAppSentBytes { get; set; }
        public long? Capacity { get; set; }
        public long? Recommended { get; set; }
        public UplinkSegmentStats? Idle { get; set; }
        public UplinkSegmentStats? Loaded { get; set; }
        public UplinkSegmentStats? Limited { get; set; }
        public List<UplinkSearchStep> SearchSteps { get; } = [];
        public List<UplinkVerificationStep> VerificationSteps { get; } = [];
        public long? Restored { get; set; }
        public bool TrialPolicyRemoved { get; set; }
        public string? CleanupError { get; set; }
        public bool LineChanged { get; set; }
        public bool RequiresAbort { get; private set; }
        public List<CommandExecutionResult> Commands { get; } = [];

        /// <summary>変更コマンドの結果を集め、終了を確認できないものがあれば中断フラグを立てる。</summary>
        public void Collect(UplinkLimitOperationResult result)
        {
            Commands.AddRange(result.Commands);
            RequiresAbort |= result.RequiresOperationAbort;
        }

        public void Report(UplinkMeasurementPhase phase, string message, TimeProvider time) =>
            progress?.Report(new UplinkMeasurementProgress(phase, message, time.GetElapsedTime(StartTimestamp), TotalAppSentBytes));
    }
}
