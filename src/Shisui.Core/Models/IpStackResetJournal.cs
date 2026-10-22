namespace Shisui.Core.Models;

// 失敗条件: 取得漏れ・未対応 compartment/設定・DNS source 不明・保存失敗・NIC 入替え・部分 reset・復元失敗・再起動前後の不一致。
// 未解決の原本は再取得で上書きせず、全設定の検証と永続保存が成功するまで保持する。
public sealed class IpStackResetJournal
{
    public int SchemaVersion { get; set; } = 1;
    public string OperationId { get; set; } = Guid.NewGuid().ToString("D");
    public string Phase { get; set; } = "Prepared";
    public IpStackSnapshot Original { get; set; } = new();
    public IpStackSnapshot? ExpectedFinal { get; set; }
    public bool RequiresRebootBeforeRecovery { get; set; }
    public string? LastMutationBootId { get; set; }
    public string? LastError { get; set; }
}

public sealed class IpStackSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string BootId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public List<IpStackAdapterSnapshot> Adapters { get; set; } = [];
    public List<IpStackInterfaceSnapshot> Interfaces { get; set; } = [];
    public List<IpStackAddressSnapshot> Addresses { get; set; } = [];
    public List<IpStackRouteSnapshot> Routes { get; set; } = [];
    public List<IpStackDnsSnapshot> Dns { get; set; } = [];
}

public sealed class IpStackAdapterSnapshot
{
    // AdapterId は InterfaceGuid の D 形式。組込み loopback だけは "loopback"。
    public string AdapterId { get; set; } = string.Empty;
    public int InterfaceIndex { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PnpDeviceId { get; set; } = string.Empty;
    public bool HasNetworkConfiguration { get; set; }
    public uint NetbiosOptions { get; set; }
    public List<string> StaticWinsServers { get; set; } = [];
}

public sealed class IpStackInterfaceSnapshot
{
    public string AdapterId { get; set; } = string.Empty;
    public int AddressFamily { get; set; }
    public string PolicyStore { get; set; } = string.Empty;
    public List<IpStackSetting> Settings { get; set; } = [];
}

public sealed class IpStackSetting
{
    // Name は各固定 allowlist の cmdlet パラメーター名。Value をスクリプトとして評価しない。
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsTimeSpan { get; set; }
}

public sealed class IpStackAddressSnapshot
{
    public string AdapterId { get; set; } = string.Empty;
    public int AddressFamily { get; set; }
    public string PolicyStore { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int PrefixLength { get; set; }
    public bool SkipAsSource { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class IpStackRouteSnapshot
{
    public string AdapterId { get; set; } = string.Empty;
    public int AddressFamily { get; set; }
    public string PolicyStore { get; set; } = string.Empty;
    public string DestinationPrefix { get; set; } = string.Empty;
    public string NextHop { get; set; } = string.Empty;
    public uint RouteMetric { get; set; }
    public string Publish { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
}

public sealed class IpStackDnsSnapshot
{
    public string AdapterId { get; set; } = string.Empty;
    public int AddressFamily { get; set; }
    public bool Automatic { get; set; }
    public List<string> ServerAddresses { get; set; } = [];
    public bool AutomaticSuffix { get; set; }
    public string ConnectionSpecificSuffix { get; set; } = string.Empty;
    public bool RegisterThisConnectionsAddress { get; set; }
    public bool UseSuffixWhenRegistering { get; set; }
}
