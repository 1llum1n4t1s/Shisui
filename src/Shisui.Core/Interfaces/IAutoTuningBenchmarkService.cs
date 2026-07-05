using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルを実際に切り替えながらダウンロード速度を計測し、
/// 現在の回線環境にどのレベルが合っているかを判断できるようにする (Windows 専用機能)。
/// macOS では DI 登録されず、UI 側がボタンごと非表示にする。
/// </summary>
public interface IAutoTuningBenchmarkService
{
    /// <summary>
    /// 全レベルを順に切り替えながら <paramref name="testSizeBytes"/> バイトのダウンロードを
    /// レベルごとに <paramref name="samplesPerLevel"/> 回計測し、平均値を返す (単発計測は回線の
    /// 瞬間的なブレの影響を受けやすいため)。計測開始前のレベルは完了・キャンセル・例外いずれの
    /// 場合も自動的に復元される。
    /// </summary>
    Task<IReadOnlyList<AutoTuningBenchmarkResult>> RunAsync(
        int testSizeBytes,
        int samplesPerLevel = 5,
        IProgress<AutoTuningBenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
