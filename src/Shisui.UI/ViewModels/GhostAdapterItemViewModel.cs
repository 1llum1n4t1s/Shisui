using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// 切断済みネットワークデバイス 1 件分。
/// </summary>
public partial class GhostAdapterItemViewModel : ObservableObject
{
    private readonly Func<GhostAdapterItemViewModel, Task> _remove;

    public GhostAdapterInfo Info { get; }

    [ObservableProperty]
    private bool isRemoving;

    public IAsyncRelayCommand RemoveCommand { get; }

    public GhostAdapterItemViewModel(GhostAdapterInfo info, Func<GhostAdapterItemViewModel, Task> remove)
    {
        Info = info;
        _remove = remove;
        RemoveCommand = new AsyncRelayCommand(() => _remove(this), CanRemove);
    }

    private bool CanRemove() => !IsRemoving;

    partial void OnIsRemovingChanged(bool value) => RemoveCommand.NotifyCanExecuteChanged();
}
