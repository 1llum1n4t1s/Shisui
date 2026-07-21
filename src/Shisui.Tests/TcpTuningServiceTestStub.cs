using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Tests;

internal abstract class TcpTuningServiceTestStub : ITcpTuningService
{
    public virtual Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<CommandExecutionResult>> SetCongestionProvidersAsync(IReadOnlyDictionary<string, string> providers, CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<CommandExecutionResult>> RevertGlobalOptionsToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> RevertTcpGlobalOptionToDefaultAsync(TcpGlobalOption option, CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<CommandExecutionResult>> RevertMtuToDefaultAsync(string adapterId, CancellationToken ct = default) => throw new NotSupportedException();
    public virtual Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default) => throw new NotSupportedException();

    protected static CommandExecutionResult Success() => new(true, "netsh", 0, string.Empty, string.Empty);
}
