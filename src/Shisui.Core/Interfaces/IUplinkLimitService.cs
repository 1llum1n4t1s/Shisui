using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 上り速度制限 (ActiveStore の QoS ポリシー) の読み取りと変更。Windows 専用で、DI では省略可能依存。
/// 変更ゲートは呼び出し側 (ViewModel) が取得済みとし、ここでは取得しない。
/// 不変条件: 適用用 (ShisuiUplinkLimit) と試験用 (ShisuiUplinkTrial) を同時に存在させない。
/// Shisui 以外のポリシーが ActiveStore または既定ストアにある間は、書き込みを拒否する。
/// 書き込みは読み戻しで確認する。名前の比較は大小文字を無視する。
/// </summary>
public interface IUplinkLimitService
{
    /// <summary>回線識別とポリシーの現在値を読む (変更しない)。</summary>
    Task<UplinkLimitState> GetStateAsync(CancellationToken ct = default);

    /// <summary>適用用を作成 (既存なら置換) し、名前・値・Template=Default を読み戻す。試験用があれば拒否。</summary>
    Task<UplinkLimitOperationResult> ApplyLimitAsync(long bitsPerSecond, CancellationToken ct = default);

    /// <summary>適用用だけを削除し、0 件を読み戻す。無ければ成功。</summary>
    Task<UplinkLimitOperationResult> RemoveLimitAsync(CancellationToken ct = default);

    /// <summary>試験用を作成 (既存なら置換) し読み戻す。適用用があれば拒否 (<see cref="BeginMeasurementAsync"/> を先に呼ぶ)。</summary>
    Task<UplinkLimitOperationResult> ApplyTrialAsync(long bitsPerSecond, CancellationToken ct = default);

    /// <summary>試験用だけを削除し、0 件を読み戻す。起動時の残骸削除も兼ねる (無ければ成功)。</summary>
    Task<UplinkLimitOperationResult> RemoveTrialAsync(CancellationToken ct = default);

    /// <summary>
    /// 計測の開始。試験用の残骸と適用用を削除し、適用用があった値を
    /// <see cref="UplinkLimitOperationResult.PreviousLimitBitsPerSecond"/> に返す (範囲外や Template が Default でなければ null)。
    /// 他ポリシーがあれば何も変更せず拒否する。
    /// </summary>
    Task<UplinkLimitOperationResult> BeginMeasurementAsync(CancellationToken ct = default);

    /// <summary>
    /// 計測の終了 (finally で CancellationToken.None を渡して呼ぶ)。試験用を削除して 0 件を確認し、
    /// <paramref name="restoreLimitBitsPerSecond"/> があれば適用用をその値で復元する。
    /// 計測結果を適用する場合は null を渡し、後で <see cref="ApplyLimitAsync"/> を呼ぶ。
    /// </summary>
    Task<UplinkLimitOperationResult> EndMeasurementAsync(long? restoreLimitBitsPerSecond, CancellationToken ct = default);
}
