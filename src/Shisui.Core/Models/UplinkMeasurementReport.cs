namespace Shisui.Core.Models;

/// <summary>計測の終了理由。名前は <see cref="UplinkLimitProfile.Outcome"/> にそのまま保存する。</summary>
public enum UplinkMeasurementOutcome
{
    /// <summary>上限の提案あり (④ の検証に合格)。</summary>
    Recommended,

    /// <summary>1 回目の負荷でも遅延・損失が悪化しない。制限不要。</summary>
    NoChangeNeeded,

    /// <summary>どの上限でも合格しない、または ④ の検証に合格しない。適用しない。</summary>
    NoImprovement,

    /// <summary>無負荷の標本不足・損失 5% 超・ICMP 全滅。</summary>
    Unstable,

    /// <summary>無負荷の NIC 送信または受信が 1 Mbps 超 (他の通信がある)。</summary>
    LineBusy,

    /// <summary>HTTP 429・403・5xx・接続失敗など。測定先の問題で全体中止。</summary>
    TargetError,

    /// <summary>総量 2GB または 5 分に到達。適用しない。</summary>
    LimitReached,

    /// <summary>区間の無効 (標本不足・アプリ送信と NIC 送信の不一致) がやり直しでも解消しない。</summary>
    InvalidSegment,

    RejectedOtherPolicies,
    RejectedVirtualAdapter,
    RejectedLineUnknown,
    RejectedStateUnreadable,

    /// <summary>試験用ポリシーの適用・読み戻しに失敗し、計測を続けられない。</summary>
    PolicyFailed,

    Cancelled,

    /// <summary>想定外の例外 (NIC が見つからないなど)。内容は <see cref="UplinkMeasurementReport.Message"/> に含める。試験用の後始末は行っている。</summary>
    Failed,
}

/// <summary>計測の進行段階 (画面の進捗表示用)。</summary>
public enum UplinkMeasurementPhase
{
    Preparing,
    Idle,
    Capacity,
    Search,
    Verification,
    Cleanup,
}

public enum UplinkSegmentInvalidReason
{
    None,
    TooFewSamples,
    AppNicMismatch,
    AllPingLost,
}

/// <summary>計測の進捗通知。</summary>
public sealed record UplinkMeasurementProgress(UplinkMeasurementPhase Phase, string Message, TimeSpan Elapsed, long AppSentBytes);

public enum UplinkProbeStatus
{
    Completed,

    /// <summary>HTTP 429・403・5xx、または接続失敗。<see cref="UplinkProbeResult.Detail"/> に内容。</summary>
    TargetError,

    /// <summary>アプリ送信が <see cref="UplinkProbeRequest.MaxAppSentBytes"/> に到達して打ち切った。</summary>
    ByteLimitReached,
}

/// <summary>1 区間の負荷・観測の依頼。ConnectionCount が 0 なら負荷なし (無負荷の観測)。</summary>
public sealed record UplinkProbeRequest(
    TimeSpan Duration,
    int ConnectionCount,
    string AdapterGuid,
    long MaxAppSentBytes,
    string PingTarget = "1.1.1.1");

/// <summary>NIC 統計の 1 区間 (約 1 秒) の増分。bps は呼び出し側で ElapsedMilliseconds から計算する。</summary>
public sealed record UplinkProbeInterval(long ElapsedMilliseconds, long NicSentBytes, long NicReceivedBytes, long AppSentBytes);

/// <summary>ICMP 1 回分。RttMs が null なら損失 (タイムアウト・失敗)。</summary>
public sealed record UplinkPingSample(long OffsetMilliseconds, double? RttMs);

/// <summary>1 区間の生データ。判定・統計は含めない。</summary>
public sealed record UplinkProbeResult(
    UplinkProbeStatus Status,
    string? Detail,
    int? HttpStatusCode,
    IReadOnlyList<UplinkProbeInterval> Intervals,
    IReadOnlyList<UplinkPingSample> Pings,
    long AppSentBytes);

/// <summary>1 区間の統計と有効性。</summary>
public sealed record UplinkSegmentStats(
    int PingSampleCount,
    double LossPercent,
    double? RttP90Ms,
    double NicSendMeanBitsPerSecond,
    double NicReceiveMeanBitsPerSecond,
    long AppSentBytes,
    bool IsValid,
    UplinkSegmentInvalidReason InvalidReason);

/// <summary>二分探索の 1 試行。Percent は C に対する割合 (95〜70)。</summary>
public sealed record UplinkSearchStep(int Percent, long PolicyBitsPerSecond, bool Passed, UplinkSegmentStats Stats);

/// <summary>④ の「なし/あり」検証の 1 区間。</summary>
public sealed record UplinkVerificationStep(bool LimitApplied, UplinkSegmentStats Stats);

/// <summary>計測全体の結果。適用は含まない (利用者が結果表示後に操作する)。</summary>
public sealed record UplinkMeasurementReport(
    UplinkMeasurementOutcome Outcome,
    string Message,
    UplinkLineIdentity Line,
    DateTimeOffset MeasuredAtUtc,
    long? CapacityBitsPerSecond,
    long? RecommendedPolicyBitsPerSecond,
    UplinkSegmentStats? Idle,
    UplinkSegmentStats? Loaded,
    UplinkSegmentStats? Limited,
    IReadOnlyList<UplinkSearchStep> SearchSteps,
    IReadOnlyList<UplinkVerificationStep> VerificationSteps,
    long TotalAppSentBytes,
    TimeSpan Elapsed,
    IReadOnlyList<string> OtherPolicyNames,
    long? RestoredLimitBitsPerSecond,
    bool TrialPolicyRemoved,
    string? CleanupError)
{
    /// <summary>計測中に実行した変更コマンド (試験用の作成・削除、準備、後始末) の結果。実行ログへ流す。</summary>
    public IReadOnlyList<CommandExecutionResult> Commands { get; init; } = [];

    /// <summary>変更コマンドの終了を確認できず、以降の操作を止める必要があるか。</summary>
    public bool RequiresOperationAbort { get; init; }

    /// <summary>試験用ポリシーを削除し、0 件を読み戻せたか。false なら画面に明示する。</summary>
    public bool CleanupSucceeded => TrialPolicyRemoved && CleanupError is null;
}
