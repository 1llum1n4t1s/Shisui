using Avalonia.Controls;
using Shisui.UI.ViewModels;

namespace Shisui.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            // 更新による再起動・アプリ終了・OS終了は妨げず、利用者の閉じる操作だけを止める。
            if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing
                && DataContext is MainWindowViewModel { IsProcessing: true })
            {
                e.Cancel = true;
            }
        };
    }
}
