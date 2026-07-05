using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// メンテナンスコマンド 1 行分。
/// </summary>
public partial class MaintenanceCommandItemViewModel : ObservableObject
{
    private readonly Func<MaintenanceCommandItemViewModel, Task> _run;

    public MaintenanceCommandDefinition Definition { get; }

    [ObservableProperty]
    private bool isRunning;

    public IAsyncRelayCommand RunCommand { get; }

    public MaintenanceCommandItemViewModel(MaintenanceCommandDefinition definition, Func<MaintenanceCommandItemViewModel, Task> run)
    {
        Definition = definition;
        _run = run;
        RunCommand = new AsyncRelayCommand(() => _run(this), CanRun);
    }

    private bool CanRun() => !IsRunning;

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
}
