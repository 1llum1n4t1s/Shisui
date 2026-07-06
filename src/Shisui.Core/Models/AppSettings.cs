using System.Text.Json.Serialization;

namespace Shisui.Core.Models;

public sealed class AppSettings
{
    public string? LastSelectedAdapterId { get; set; }

    /// <summary>前回選択した DNS プロバイダプリセットの Id (次回起動時に復元する)。</summary>
    public string? LastSelectedPresetId { get; set; }

    /// <summary>カスタムプリセットの入力 IP (前回値を次回起動時に復元する)。</summary>
    public string? CustomIpv4Primary { get; set; }

    public string? CustomIpv4Secondary { get; set; }

    public string? CustomIpv6Primary { get; set; }

    public string? CustomIpv6Secondary { get; set; }

    /// <summary>起動時に自動で更新を確認するか。</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>「このバージョンをスキップ」で保存した更新タグ。以降この版の自動更新通知を出さない。</summary>
    public string? IgnoreUpdateTag { get; set; }

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
