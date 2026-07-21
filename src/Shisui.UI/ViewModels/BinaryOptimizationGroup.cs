using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Shisui.UI.ViewModels;

public partial class BinaryOptimizationGroup(string title) : ObservableObject
{
    public string Title { get; } = title;
    public ObservableCollection<BinaryBenchmarkRow> Results { get; } = [];
    [ObservableProperty] private string statusText = "待機中";
    [ObservableProperty] private string recommendationText = "未計測";

    public void Reset()
    {
        Results.Clear();
        StatusText = "待機中";
        RecommendationText = "未計測";
    }

    public void FailWithWindowsDefault(string message)
    {
        StatusText = $"計測に失敗しました: {message}";
        RecommendationText = "推奨: Windows既定値";
    }
}

public sealed record BinaryBenchmarkRow(string StateText, string ResultText, bool IsBest);

public enum BinaryOptimizationRecommendation
{
    WindowsDefault,
    Enabled,
    Disabled,
}
