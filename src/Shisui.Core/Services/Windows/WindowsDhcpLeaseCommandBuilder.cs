using System.Globalization;

namespace Shisui.Core.Services.Windows;

/// <summary>選択 NIC の識別と DHCP 操作を行う PowerShell 引数を純粋に組み立てる。</summary>
public static class WindowsDhcpLeaseCommandBuilder
{
    public static string BuildQueryArguments(string adapterName, Guid? expectedGuid = null, int? expectedIndex = null)
    {
        var script = BuildSelection(adapterName, expectedGuid, expectedIndex) +
            "$id=[guid]$a[0].InterfaceGuid;$idx=[int]$a[0].ifIndex;$status=[string]$a[0].Status;" +
            "$i=@(Get-NetIPInterface -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $a[0].ifIndex});" +
            "$ip=@(Get-NetIPAddress -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $a[0].ifIndex});" +
            "$r=@(Get-NetRoute -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $a[0].ifIndex});" +
            "'GUID='+([guid]$a[0].InterfaceGuid).ToString('D');'INDEX='+[int]$a[0].ifIndex;'STATUS='+[string]$a[0].Status;" +
            "foreach($f in @('IPv4','IPv6')){" +
            "$n=if($f -eq 'IPv4'){'4'}else{'6'};$j=@($i | Where-Object {[string]$_.AddressFamily -eq $f});" +
            "$d='Unknown';if($j.Count -eq 1 -and [string]$j[0].Dhcp -in @('Enabled','Disabled')){$d=[string]$j[0].Dhcp};" +
            "'DHCP'+$n+'='+$d;" +
            "$p=@($ip | Where-Object {[string]$_.AddressFamily -eq $f});'IPCOUNT'+$n+'='+$p.Count;" +
            "foreach($x in $p){'IP'+$n+'='+[string]$x.IPAddress+'|'+[string]$x.PrefixOrigin+'|'+[string]$x.AddressState};" +
            "$prefix=if($n -eq '4'){'0.0.0.0/0'}else{'::/0'};" +
            "$g=@($r | Where-Object {$_.DestinationPrefix -eq $prefix -and $_.NextHop -notin @('0.0.0.0','::','')});" +
            "'GATEWAYCOUNT'+$n+'='+$g.Count;foreach($x in $g){'GATEWAY'+$n+'='+[string]$x.NextHop}};" +
            BuildSelection(adapterName, expectedGuid, expectedIndex) +
            "if(([guid]$a[0].InterfaceGuid) -ne $id -or [int]$a[0].ifIndex -ne $idx -or [string]$a[0].Status -ne $status){throw 'Adapter changed during query'};'COMPLETE=1';";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    public static string BuildReleaseArguments(string adapterName, Guid expectedGuid, int expectedIndex, bool ipv6) =>
        BuildMutationArguments(adapterName, expectedGuid, expectedIndex, ipv6, release: true);

    public static string BuildRenewArguments(string adapterName, Guid expectedGuid, int expectedIndex, bool ipv6) =>
        BuildMutationArguments(adapterName, expectedGuid, expectedIndex, ipv6, release: false);

    private static string BuildMutationArguments(string adapterName, Guid expectedGuid, int expectedIndex, bool ipv6, bool release)
    {
        var operation = release ? (ipv6 ? "/release6" : "/release") : (ipv6 ? "/renew6" : "/renew");
        // 実行直前にも GUID と index を照合する。引数を省略した全 NIC 操作は生成しない。
        var family = ipv6 ? "IPv6" : "IPv4";
        var script = BuildSelection(adapterName, expectedGuid, expectedIndex) +
            "if([string]$a[0].Status -ne 'Up'){throw 'Adapter is not connected'};" +
            $"$i=@(Get-NetIPInterface -InterfaceIndex $a[0].ifIndex -AddressFamily {family} -PolicyStore ActiveStore -ErrorAction Stop);" +
            "if($i.Count -ne 1 -or [string]$i[0].Dhcp -ne 'Enabled'){throw 'DHCP is not enabled'};" +
            (ipv6 && release
                ? "if(@(Get-NetIPAddress -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {$_.InterfaceIndex -eq $a[0].ifIndex -and [string]$_.AddressFamily -eq 'IPv6' -and [string]$_.PrefixOrigin -eq 'Dhcp' -and [string]$_.AddressState -eq 'Preferred'}).Count -eq 0){throw 'No DHCPv6 lease'};"
                : string.Empty) +
            $"& (Join-Path ([Environment]::SystemDirectory) 'ipconfig.exe') '{operation}' ([string]$a[0].Name);" +
            "$code=$LASTEXITCODE;if($code -ne 0){exit $code};" +
            BuildSelection(adapterName, expectedGuid, expectedIndex);
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    private static string BuildSelection(string adapterName, Guid? expectedGuid, int? expectedIndex)
    {
        ValidateAdapterName(adapterName);
        if (expectedGuid == Guid.Empty || expectedIndex is <= 0 || expectedGuid.HasValue != expectedIndex.HasValue)
        {
            throw new ArgumentException("アダプターの GUID と index が不正です");
        }

        var script = "$ErrorActionPreference='Stop';$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            WindowsPowerShellAdapterSelection.BuildExactLookup(adapterName);
        if (expectedGuid is { } guid && expectedIndex is { } index)
        {
            script += $"if(([guid]$a[0].InterfaceGuid) -ne [guid]'{guid:D}' -or [int]$a[0].ifIndex -ne {index.ToString(CultureInfo.InvariantCulture)}){{throw 'Adapter identity changed'}};";
        }

        return script;
    }

    public static void ValidateAdapterName(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || adapterName != adapterName.Trim() ||
            adapterName.Any(c => char.IsControl(c) || c is '*' or '?' or '"' or '\'' or '‘' or '’' or '“' or '”' or '`' or '\\' or '/' or '[' or ']'))
        {
            throw new ArgumentException("IP 再取得にはワイルドカード、引用符、改行などを含まない接続名が必要です", nameof(adapterName));
        }
    }
}
