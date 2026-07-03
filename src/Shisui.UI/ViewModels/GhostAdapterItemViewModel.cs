using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// 切断済みネットワークデバイス 1 件分。削除はすべて「確認済み」チェックが入るまで実行できない
/// (レジストリ上のデバイス登録を消す不可逆に近い操作のため、Microsoft 製の判定に関わらず一律でゲートする)。
/// </summary>
public partial class GhostAdapterItemViewModel : ObservableObject
{
    private readonly Func<GhostAdapterItemViewModel, Task> _remove;

    public GhostAdapterInfo Info { get; }

    [ObservableProperty]
    private bool isConfirmed;

    [ObservableProperty]
    private bool isRemoving;

    public IAsyncRelayCommand RemoveCommand { get; }

    public GhostAdapterItemViewModel(GhostAdapterInfo info, Func<GhostAdapterItemViewModel, Task> remove)
    {
        Info = info;
        _remove = remove;
        RemoveCommand = new AsyncRelayCommand(() => _remove(this), CanRemove);
    }

    private bool CanRemove() => !IsRemoving && IsConfirmed;

    partial void OnIsConfirmedChanged(bool value) => RemoveCommand.NotifyCanExecuteChanged();

    partial void OnIsRemovingChanged(bool value) => RemoveCommand.NotifyCanExecuteChanged();
}
