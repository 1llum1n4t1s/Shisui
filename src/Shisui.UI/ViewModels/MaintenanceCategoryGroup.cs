namespace Shisui.UI.ViewModels;

public sealed class MaintenanceCategoryGroup(string name, IReadOnlyList<MaintenanceCommandItemViewModel> items)
{
    public string Name { get; } = name;

    public IReadOnlyList<MaintenanceCommandItemViewModel> Items { get; } = items;
}
