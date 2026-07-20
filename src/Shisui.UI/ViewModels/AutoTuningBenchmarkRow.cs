using Shisui.Core.Interfaces;

namespace Shisui.UI.ViewModels;

public sealed record AutoTuningBenchmarkRow(AutoTuningLevel Level, string PingText, bool IsBest);
