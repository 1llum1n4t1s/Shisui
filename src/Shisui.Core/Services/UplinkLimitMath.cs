using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 上り速度計測の統計・判定・換算 (純関数。OS・通信・時刻に触れない)。
/// </summary>
public static class UplinkLimitMath
{
    /// <summary>負荷区間で捨てる立ち上がり (ミリ秒)。</summary>
    public const long WarmupMilliseconds = 3000;

    /// <summary>無負荷との比較で許す p90 の悪化 (ms)。</summary>
    public const double AllowedRttIncreaseMs = 15;

    /// <summary>無負荷との比較で許す損失率の悪化 (ポイント)。</summary>
    public const double AllowedLossIncreasePoints = 2;

    /// <summary>
    /// ④ の再現確認で許す p90 の悪化 (ms)。③ で選んだ値は合格線 (+15ms) のすぐ内側になりやすく、
    /// 10 秒区間の揺れで落ち得るため余裕を持たせる (記録できた区間の実測: 無負荷 10〜15ms・制限中 21〜25ms・制限なし 124〜188ms)。
    /// 代わりに、増えた遅延を <see cref="RequiredExcessRatio"/> 以下へ減らしたことを必須にする。
    /// </summary>
    public const double VerificationRttIncreaseMs = 25;

    /// <summary>④ で「あり」の遅延の増分 (無負荷比) が、直前の「なし」の増分に対して収まるべき割合。</summary>
    public const double RequiredExcessRatio = 0.5;

    /// <summary>無負荷でこの損失率を超えたら回線が不安定とみなす (%)。</summary>
    public const double MaxIdleLossPercent = 5;

    /// <summary>無負荷でこれを超える NIC 送受信は他の通信とみなす (bps)。</summary>
    public const double IdleBusyBitsPerSecond = 1_000_000;

    /// <summary>区間として有効な ICMP 標本数の下限。</summary>
    public const int MinPingSamples = 50;

    /// <summary>NIC 送信とアプリ送信の差がこの割合を超えたら区間を無効とする (再送・見出し分を含め 15%)。</summary>
    public const double MaxAppNicMismatchRatio = 0.15;

    /// <summary>NIC 送信 ÷ ポリシー値の既定比 (実測 28.0Mbps → 28.8〜28.9Mbps)。</summary>
    public const double DefaultNicToPolicyRatio = 1.03;

    /// <summary>
    /// ポリシー値の刻み (bps)。Windows は QoS ポリシーの上限を 8bps 単位へ切り捨てて保存するため (実機確認)、
    /// 端数があると読み戻しが一致しない。8 の倍数かつ表示しやすい 1kbps へ切り捨てて渡す。
    /// </summary>
    public const long PolicyStepBitsPerSecond = 1000;

    private const double MinRatio = 1.0;
    private const double MaxRatio = 1.10;

    /// <summary>二分探索の候補 (C に対する %。大きい順)。</summary>
    public static IReadOnlyList<int> SearchPercents { get; } = [95, 90, 85, 80, 75, 70];

    /// <summary>二分探索の最大試行回数。</summary>
    public const int MaxSearchTrials = 3;

    /// <summary>nearest-rank 法の p90。空なら null。</summary>
    public static double? Percentile90(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(static v => v).ToArray();
        var rank = (int)Math.Ceiling(0.9 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    /// <summary>損失率 (%)。標本が 0 件なら 100。</summary>
    public static double LossPercent(IReadOnlyList<UplinkPingSample> pings) =>
        pings.Count == 0 ? 100 : 100.0 * pings.Count(static p => p.RttMs is null) / pings.Count;

    /// <summary>区間 (約 1 秒) の bps。経過が 0 以下なら 0。</summary>
    public static double IntervalBitsPerSecond(long bytes, long elapsedMilliseconds) =>
        elapsedMilliseconds <= 0 ? 0 : bytes * 8.0 * 1000 / elapsedMilliseconds;

    /// <summary>
    /// 立ち上がり (<paramref name="warmupMilliseconds"/>) を捨てた後の区間ごとの NIC 送信 bps。
    /// 区間の開始が立ち上がり内のものを捨てる。
    /// </summary>
    public static IReadOnlyList<double> NicSendRatesAfterWarmup(IReadOnlyList<UplinkProbeInterval> intervals, long warmupMilliseconds)
    {
        var rates = new List<double>();
        long start = 0;
        foreach (var interval in intervals)
        {
            if (start >= warmupMilliseconds)
            {
                rates.Add(IntervalBitsPerSecond(interval.NicSentBytes, interval.ElapsedMilliseconds));
            }

            start += interval.ElapsedMilliseconds;
        }

        return rates;
    }

    /// <summary>立ち上がりを捨てた NIC 送信の平均 bps (総バイト ÷ 総時間)。区間が残らなければ 0。</summary>
    public static double NicSendMeanAfterWarmup(IReadOnlyList<UplinkProbeInterval> intervals, long warmupMilliseconds)
    {
        long start = 0;
        long bytes = 0;
        long elapsed = 0;
        foreach (var interval in intervals)
        {
            if (start >= warmupMilliseconds)
            {
                bytes += interval.NicSentBytes;
                elapsed += interval.ElapsedMilliseconds;
            }

            start += interval.ElapsedMilliseconds;
        }

        return IntervalBitsPerSecond(bytes, elapsed);
    }

    /// <summary>全区間の NIC 受信の平均 bps。</summary>
    public static double NicReceiveMean(IReadOnlyList<UplinkProbeInterval> intervals) =>
        IntervalBitsPerSecond(intervals.Sum(static i => i.NicReceivedBytes), intervals.Sum(static i => i.ElapsedMilliseconds));

    /// <summary>
    /// 1 区間の生データから統計と有効性を作る。無効の判定順は 標本不足 → ICMP 全滅 → アプリと NIC の不一致。
    /// </summary>
    /// <param name="checkAppNicMismatch">負荷区間で true。無負荷では送信がほぼ無いので判定しない。</param>
    public static UplinkSegmentStats Analyze(UplinkProbeResult result, long warmupMilliseconds, bool checkAppNicMismatch)
    {
        var rtts = result.Pings.Where(static p => p.RttMs is not null).Select(static p => p.RttMs!.Value).ToArray();
        var nicSentTotal = result.Intervals.Sum(static i => i.NicSentBytes);
        var invalid = DetermineInvalidReason(result.Pings.Count, rtts.Length, nicSentTotal, result.AppSentBytes, checkAppNicMismatch);

        return new UplinkSegmentStats(
            PingSampleCount: result.Pings.Count,
            LossPercent: LossPercent(result.Pings),
            RttP90Ms: Percentile90(rtts),
            NicSendMeanBitsPerSecond: NicSendMeanAfterWarmup(result.Intervals, warmupMilliseconds),
            NicReceiveMeanBitsPerSecond: NicReceiveMean(result.Intervals),
            AppSentBytes: result.AppSentBytes,
            IsValid: invalid == UplinkSegmentInvalidReason.None,
            InvalidReason: invalid);
    }

    private static UplinkSegmentInvalidReason DetermineInvalidReason(
        int pingCount, int successCount, long nicSentTotal, long appSentTotal, bool checkAppNicMismatch)
    {
        if (pingCount < MinPingSamples)
        {
            return UplinkSegmentInvalidReason.TooFewSamples;
        }

        if (successCount == 0)
        {
            return UplinkSegmentInvalidReason.AllPingLost;
        }

        return checkAppNicMismatch && IsAppNicMismatch(nicSentTotal, appSentTotal)
            ? UplinkSegmentInvalidReason.AppNicMismatch
            : UplinkSegmentInvalidReason.None;
    }

    /// <summary>(NIC 送信 − アプリ送信) が NIC 送信の 15% を超えるか。アプリ送信が NIC を上回る側 (バッファ計上) は不一致としない。</summary>
    public static bool IsAppNicMismatch(long nicSentBytes, long appSentBytes) =>
        nicSentBytes <= 0 || (nicSentBytes - appSentBytes) > nicSentBytes * MaxAppNicMismatchRatio;

    /// <summary>無負荷が「他の通信あり」か (NIC 送信または受信の平均が 1 Mbps 超)。</summary>
    public static bool IsLineBusy(UplinkSegmentStats idle) =>
        idle.NicSendMeanBitsPerSecond > IdleBusyBitsPerSecond || idle.NicReceiveMeanBitsPerSecond > IdleBusyBitsPerSecond;

    /// <summary>無負荷が「回線が不安定」か (標本不足・損失 5% 超・ICMP 全滅)。</summary>
    public static bool IsIdleUnstable(UplinkSegmentStats idle) =>
        !idle.IsValid || idle.LossPercent > MaxIdleLossPercent || idle.RttP90Ms is null;

    /// <summary>合格 = p90 ≤ 無負荷 + 15ms かつ 損失 ≤ 無負荷 + 2 ポイント。p90 が取れなければ不合格。</summary>
    public static bool Passes(UplinkSegmentStats idle, UplinkSegmentStats loaded) =>
        IsWithin(idle, loaded, AllowedRttIncreaseMs);

    private static bool IsWithin(UplinkSegmentStats idle, UplinkSegmentStats loaded, double allowedRttIncreaseMs)
    {
        if (idle.RttP90Ms is not { } idleP90 || loaded.RttP90Ms is not { } loadedP90)
        {
            return false;
        }

        return loadedP90 <= idleP90 + allowedRttIncreaseMs
            && loaded.LossPercent <= idle.LossPercent + AllowedLossIncreasePoints;
    }

    /// <summary>
    /// 二分探索の次の候補 (<see cref="SearchPercents"/> の添字)。大きい % ほど速い。
    /// 合格なら より大きい % (小さい添字) を、不合格なら より小さい % を探す。終了なら null。
    /// </summary>
    public static int? NextSearchIndex(IReadOnlyList<(int Index, bool Passed)> history)
    {
        var low = 0;
        var high = SearchPercents.Count - 1;
        foreach (var (index, passed) in history)
        {
            if (passed)
            {
                high = index - 1;
            }
            else
            {
                low = index + 1;
            }
        }

        return history.Count >= MaxSearchTrials || low > high ? null : (low + high) / 2;
    }

    /// <summary>合格した試行のうち最速 (最大 %) の添字。合格なしなら null。</summary>
    public static int? BestPassedIndex(IReadOnlyList<(int Index, bool Passed)> history)
    {
        int? best = null;
        foreach (var (index, passed) in history)
        {
            if (passed && (best is null || index < best))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// ④ の再現判定。並びは なし→あり→なし→あり。「あり」の 2 回が無負荷 +25ms・損失 +2 ポイント以内で、
    /// 各「あり」の遅延の増分が直前の「なし」の半分以下のときだけ true。
    /// 「なし」で ICMP が全滅 (p90 なし) したのは悪化の観測として有効で、「あり」が合格なら再現とみなす。
    /// 無効扱いは標本不足と NIC/アプリの不一致だけ (どの区間でも不可)。
    /// </summary>
    public static bool IsReproduced(UplinkSegmentStats idle, IReadOnlyList<UplinkVerificationStep> steps)
    {
        if (steps.Count != 4
            || steps.Select(static s => s.LimitApplied).SequenceEqual([false, true, false, true]) is false
            || steps.Any(static s => s.Stats.InvalidReason is UplinkSegmentInvalidReason.TooFewSamples or UplinkSegmentInvalidReason.AppNicMismatch))
        {
            return false;
        }

        return IsBetter(idle, steps[0].Stats, steps[1].Stats) && IsBetter(idle, steps[2].Stats, steps[3].Stats);
    }

    private static bool IsBetter(UplinkSegmentStats idle, UplinkSegmentStats without, UplinkSegmentStats with)
    {
        if (!IsWithin(idle, with, VerificationRttIncreaseMs)
            || with.RttP90Ms is not { } withP90 || idle.RttP90Ms is not { } idleP90)
        {
            return false;
        }

        return without.RttP90Ms is not { } withoutP90
            || (withP90 < withoutP90 && withP90 - idleP90 <= (withoutP90 - idleP90) * RequiredExcessRatio);
    }

    /// <summary>
    /// 試験用ポリシーで得た NIC 送信と設定値の比を、1.0〜1.10 に収めて返す。
    /// 取れない (0 以下) ときは既定の 1.03。
    /// </summary>
    public static double CalibrateRatio(double measuredNicBitsPerSecond, long policyBitsPerSecond)
    {
        if (measuredNicBitsPerSecond <= 0 || policyBitsPerSecond <= 0)
        {
            return DefaultNicToPolicyRatio;
        }

        return Math.Clamp(measuredNicBitsPerSecond / policyBitsPerSecond, MinRatio, MaxRatio);
    }

    /// <summary>NIC ベースの目標 bps を、比で割ってポリシー値にし、範囲 (1Mbps〜10Gbps) と刻み (1kbps) へ丸める。</summary>
    public static long ToPolicyBitsPerSecond(double nicBitsPerSecond, double ratio = DefaultNicToPolicyRatio)
    {
        var safeRatio = ratio > 0 ? ratio : DefaultNicToPolicyRatio;
        var clamped = ClampToRange(nicBitsPerSecond / safeRatio);
        return clamped - clamped % PolicyStepBitsPerSecond;
    }

    /// <summary>範囲 (1Mbps〜10Gbps) へ丸める。NaN・負値は下限。</summary>
    public static long ClampToRange(double bitsPerSecond)
    {
        if (double.IsNaN(bitsPerSecond) || bitsPerSecond <= UplinkLimitPolicy.MinBitsPerSecond)
        {
            return UplinkLimitPolicy.MinBitsPerSecond;
        }

        return bitsPerSecond >= UplinkLimitPolicy.MaxBitsPerSecond
            ? UplinkLimitPolicy.MaxBitsPerSecond
            : (long)Math.Round(bitsPerSecond);
    }
}
