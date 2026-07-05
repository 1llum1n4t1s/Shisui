using VelopackUpdateDialog;

namespace Shisui.UI.Services;

/// <summary>
/// VelopackUpdateDialog.Avalonia が要求する文字列セット (<see cref="IUpdateDialogStrings"/>) の
/// 日本語実装。Shisui は日本語単一言語なので、Lhamiel のような Locale 動的解決・INotifyPropertyChanged は
/// 持たず、固定文字列を返すシングルトンにする。
/// </summary>
public sealed class ShisuiUpdateStrings : IUpdateDialogStrings
{
    public static ShisuiUpdateStrings Instance { get; } = new();

    private ShisuiUpdateStrings()
    {
    }

    public string Title => "アップデート";

    public string AvailableHeader => "新しいバージョンがあります";

    public string DownloadAndInstall => "ダウンロードしてインストール";

    public string IgnoreThisVersion => "このバージョンをスキップ";

    public string UpToDateMessage => "お使いのバージョンは最新です";

    public string ErrorHeader => "更新の確認に失敗しました";

    public string Close => "閉じる";

    public string CheckingMessage => "更新を確認しています…";
}
