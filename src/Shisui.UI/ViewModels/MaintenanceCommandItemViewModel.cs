using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// メンテナンスコマンド 1 行分。破壊的なコマンドは「確認済み」チェックが入るまで実行できない
/// (モーダルダイアログを使わない、コンパイルドバインディングだけで完結する安全ゲート)。
/// </summary>
public partial class MaintenanceCommandItemViewModel : ObservableObject
{
    private readonly Func<MaintenanceCommandItemViewModel, Task> _run;

    public MaintenanceCommandDefinition Definition { get; }

    [ObservableProperty]
    private bool isConfirmed;

    [ObservableProperty]
    private bool isRunning;

    public IAsyncRelayCommand RunCommand { get; }

    public MaintenanceCommandItemViewModel(MaintenanceCommandDefinition definition, Func<MaintenanceCommandItemViewModel, Task> run)
    {
        Definition = definition;
        _run = run;
        RunCommand = new AsyncRelayCommand(() => _run(this), CanRun);
    }

    private bool CanRun() => !IsRunning && (!Definition.IsDestructive || IsConfirmed);

    partial void OnIsConfirmedChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
}
