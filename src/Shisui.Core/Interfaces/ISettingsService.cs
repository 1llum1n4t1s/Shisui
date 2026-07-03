using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task SaveAsync(CancellationToken ct = default);
}
