using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// RSC を有効・無効へ順に切り替え、TCP 受信負荷中の Ping を比較する Windows 専用サービス。
/// </summary>
public interface IRscBenchmarkService
{
    /// <summary>
    /// RSC 有効・無効を同じ条件で計測する。開始前の実効状態は完了・キャンセル・例外のいずれでも復元する。
    /// </summary>
    Task<IReadOnlyList<RscBenchmarkResult>> RunAsync(
        int testSizeBytes,
        IProgress<RscBenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
