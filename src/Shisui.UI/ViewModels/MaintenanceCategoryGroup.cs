using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shisui.UI.ViewModels;

public partial class MaintenanceCategoryGroup : ObservableObject
{
    public string Name { get; }

    public IReadOnlyList<MaintenanceCommandItemViewModel> Items { get; }

    /// <summary>このカテゴリが「まとめて実行」ボタンを持つか。</summary>
    public bool SupportsBatch { get; }

    /// <summary>「まとめて実行」ボタンのラベル (SupportsBatch のときのみ意味を持つ)。</summary>
    public string BatchLabel { get; }

    public IAsyncRelayCommand? RunAllCommand { get; }

    [ObservableProperty]
    private bool isRunningAll;

    public MaintenanceCategoryGroup(
        string name,
        IReadOnlyList<MaintenanceCommandItemViewModel> items,
        string? batchLabel,
        Func<MaintenanceCategoryGroup, Task>? runAll)
    {
        Name = name;
        Items = items;
        SupportsBatch = batchLabel is not null && runAll is not null;
        BatchLabel = batchLabel ?? string.Empty;
        if (SupportsBatch)
        {
            RunAllCommand = new AsyncRelayCommand(() => runAll!(this), () => !IsRunningAll);
        }
    }

    partial void OnIsRunningAllChanged(bool value) => RunAllCommand?.NotifyCanExecuteChanged();
}
