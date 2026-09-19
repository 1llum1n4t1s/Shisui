namespace Shisui.Core.Models;

/// <summary>ゲーム向け NIC 設定を適用する前の、アダプター単位の復元情報。</summary>
public sealed class GamingNetworkProfileSnapshot
{
    public Guid AdapterGuid { get; set; }

    public string AdapterDescription { get; set; } = string.Empty;

    public List<GamingNetworkPropertySnapshot> Properties { get; set; } = [];
}

/// <summary>変更対象として許可された NIC 詳細プロパティの元の値。</summary>
public sealed class GamingNetworkPropertySnapshot
{
    public string RegistryKeyword { get; set; } = string.Empty;

    public string OriginalValue { get; set; } = string.Empty;
}
