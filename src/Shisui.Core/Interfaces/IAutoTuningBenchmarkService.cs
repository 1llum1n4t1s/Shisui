using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルを実際に切り替え、TCP ダウンロード速度を計測して、
/// 現在の回線環境にどのレベルが合っているかを判断できるようにする (Windows 専用機能)。
/// macOS では DI 登録されず、UI 側がボタンごと非表示にする。
/// </summary>
public interface IAutoTuningBenchmarkService
{
    /// <summary>
    /// 全レベルを順に切り替えながら <paramref name="testSizeBytes"/> バイトの TCP ダウンロード速度を
    /// レベルごとに固定3回計測して平均値を返す。
    /// 計測開始前のレベルは完了・キャンセル・例外いずれの場合も自動的に復元される。
    /// </summary>
    Task<IReadOnlyList<AutoTuningBenchmarkResult>> RunAsync(
        int testSizeBytes,
        IProgress<AutoTuningBenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
