using System.Text.Json.Serialization;

namespace Shisui.Core.Models;

public sealed class AppSettings
{
    public string? LastSelectedAdapterId { get; set; }
    public List<DnsProviderPreset> CustomPresets { get; set; } = [];
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 640;

    /// <summary>起動時に自動で更新を確認するか。</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Velopack 自動更新の配信元。Cloudflare R2 (カスタムドメイン) をハードコード固定する。
    /// <see cref="JsonIgnore"/> なので settings.json から書き換え不可 (悪意ある第三者ホストへの誘導を防ぐ)。
    /// </summary>
    [JsonIgnore]
    public string UpdateBaseUrl => "https://shisui.nephilim.jp";

    /// <summary>Velopack channel。Windows 単独配信なので "win" 固定。</summary>
    [JsonIgnore]
    public string UpdateChannel => "win";
}
