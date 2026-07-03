namespace Shisui.UI.ViewModels;

public sealed record CommandLogEntry(DateTime Timestamp, string CommandLine, bool Success, string Detail);
