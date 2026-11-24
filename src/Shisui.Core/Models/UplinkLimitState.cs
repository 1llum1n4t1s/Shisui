namespace Shisui.Core.Models;

/// <summary>上り速度制限 (QoS ポリシー) の固定名と値の範囲。</summary>
public static class UplinkLimitPolicy
{
    /// <summary>適用用ポリシー名 (ActiveStore)。</summary>
    public const string LimitPolicyName = "ShisuiUplinkLimit";

    /// <summary>計測用 (試験用) ポリシー名 (ActiveStore)。</summary>
    public const string TrialPolicyName = "ShisuiUplinkTrial";

    /// <summary>下限 1 Mbps。これ未満は実用にならず、設定ミスの値を OS へ渡さないための実務上の下限。</summary>
    public const long MinBitsPerSecond = 1_000_000;

    /// <summary>上限 10 Gbps。家庭・小規模の上り回線の上限として十分で、long に収まる。</summary>
    public const long MaxBitsPerSecond = 10_000_000_000;

    public static bool IsValidRate(long bitsPerSecond) =>
        bitsPerSecond is >= MinBitsPerSecond and <= MaxBitsPerSecond;

    public static bool IsKnownName(string name) =>
        name.Equals(LimitPolicyName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(TrialPolicyName, StringComparison.OrdinalIgnoreCase);
}

public enum UplinkStateReadStatus
{
    /// <summary>QoS ポリシーの一覧を読み取れた。</summary>
    Ok,

    /// <summary>権限不足・コマンド失敗・出力の解釈不能など。ポリシーの有無を確定できない。</summary>
    Unreadable,
}

public enum UplinkLineStatus
{
    /// <summary>既定経路・ゲートウェイ MAC・NIC を確定できた。</summary>
    Identified,

    /// <summary>既定経路なし・ゲートウェイ MAC 未取得 (リース直後など)・複数経路の曖昧さなど。</summary>
    Unknown,
}

public enum UplinkAdapterKind
{
    Physical,
    Virtual,
}

/// <summary>既定経路の回線識別。<see cref="Status"/> が Unknown のとき他の項目は空。</summary>
public sealed record UplinkLineIdentity(
    UplinkLineStatus Status,
    string AdapterName,
    string AdapterGuid,
    UplinkAdapterKind Kind,
    string GatewayMac)
{
    public static UplinkLineIdentity Unknown { get; } =
        new(UplinkLineStatus.Unknown, string.Empty, string.Empty, UplinkAdapterKind.Virtual, string.Empty);

    public bool IsIdentified => Status == UplinkLineStatus.Identified;

    public bool Matches(string adapterGuid, string gatewayMac) =>
        IsIdentified
        && AdapterGuid.Equals(adapterGuid, StringComparison.OrdinalIgnoreCase)
        && GatewayMac.Equals(gatewayMac, StringComparison.OrdinalIgnoreCase);
}

/// <summary>OS から読み戻した QoS ポリシー 1 件。Name は OS が返した値 (小文字化されうる)。</summary>
public sealed record UplinkPolicyInfo(string Name, long ThrottleBitsPerSecond, bool IsDefaultTemplate);

/// <summary>
/// 回線識別と QoS ポリシーの現在値。<see cref="ReadStatus"/> が Unreadable なら
/// ポリシー関連の項目は確定していない (「無い」とは扱わない)。
/// </summary>
public sealed record UplinkLimitState(
    UplinkStateReadStatus ReadStatus,
    UplinkLineIdentity Line,
    UplinkPolicyInfo? LimitPolicy,
    UplinkPolicyInfo? TrialPolicy,
    IReadOnlyList<string> OtherActiveStorePolicyNames,
    IReadOnlyList<string> OtherDefaultStorePolicyNames,
    string? Detail = null)
{
    public static UplinkLimitState Unreadable(string detail) =>
        new(UplinkStateReadStatus.Unreadable, UplinkLineIdentity.Unknown, null, null, [], [], detail);

    public bool IsReadable => ReadStatus == UplinkStateReadStatus.Ok;

    /// <summary>Shisui 以外の QoS ポリシー名 (重複なし)。1 件でもあれば計測・適用を拒否する。</summary>
    public IReadOnlyList<string> OtherPolicyNames =>
        OtherActiveStorePolicyNames.Concat(OtherDefaultStorePolicyNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

public enum UplinkOperationOutcome
{
    Succeeded,

    /// <summary>値が 1 Mbps〜10 Gbps の範囲外。OS は変更していない。</summary>
    RejectedOutOfRange,

    /// <summary>Shisui 以外の QoS ポリシーがある。OS は変更していない。</summary>
    RejectedOtherPolicies,

    /// <summary>試験用と適用用の同時存在になる。OS は変更していない。</summary>
    RejectedConflict,

    /// <summary>事前の状態を読めない。OS は変更していない。</summary>
    StateUnreadable,

    /// <summary>コマンドの失敗・タイムアウト・キャンセル。OS の状態は不定のため、状態を読み直すこと。</summary>
    Failed,

    /// <summary>実行は終了したが、読み戻しが期待と一致しない。</summary>
    VerificationFailed,
}

/// <summary>書き込み系操作の結果。</summary>
public sealed record UplinkLimitOperationResult(
    UplinkOperationOutcome Outcome,
    string Message,
    IReadOnlyList<CommandExecutionResult> Commands,
    IReadOnlyList<string> OtherPolicyNames,
    long? PreviousLimitBitsPerSecond = null)
{
    public bool Success => Outcome == UplinkOperationOutcome.Succeeded;

    /// <summary>子プロセスの終了を確認できない等、後続の変更操作を中断すべきか。</summary>
    public bool RequiresOperationAbort => Commands.Any(command => command.RequiresOperationAbort);
}
