namespace Shisui.Core.Services.Windows;

/// <summary>IP スタックの取得・復元用。固定 cmdlet と検証済みデータだけを扱う。</summary>
public static class WindowsIpStackResetCommandBuilder
{
    public static IReadOnlyList<string> CaptureSections { get; } = ["Network", "Client"];
    public static IReadOnlyList<string> RestoreSections { get; } = ["Network", "Client", "Interfaces"];

    public static string BuildBootIdArguments() => Encode(Header + "((Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime.ToUniversalTime().ToString('O'))");

    public static string BuildCaptureArguments(string section) => Encode(Header + Inventory + (section switch
    {
        "Network" => CaptureNetwork,
        "Client" => CaptureClient,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    }) + "\n$r | ConvertTo-Json -Depth 14 -Compress");

    public static string BuildRestoreArguments(string payloadPath, string sha256, string section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        if (!Path.IsPathFullyQualified(payloadPath) || payloadPath.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("復元ファイルは絶対パスで指定してください。", nameof(payloadPath));
        if (sha256.Length != 64 || sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("復元ファイルの SHA256 が不正です。", nameof(sha256));

        var input = "$payloadPath='" + payloadPath.Replace("'", "''", StringComparison.Ordinal) + "';$expectedHash='" + sha256 + "';";
        return Encode(Header + input + ReadPayload + Inventory + ValidateIdentity + (section switch
        {
            "Network" => RestoreNetwork,
            "Client" => RestoreClient,
            "Interfaces" => RestoreInterfaces,
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        }) + "\nif($errors.Count -gt 0){[Console]::Error.WriteLine(($errors -join [Environment]::NewLine));exit 1};'復元要求が完了しました。';exit 0");
    }

    private static string Encode(string script)
    {
        var arguments = WindowsPowerShellArgumentEncoder.Encode(script);
        if (arguments.Length >= 30000)
            throw new InvalidOperationException("IP スタック操作のコマンド長が上限を超えました。");
        return arguments;
    }

    private const string Header = """
        $ErrorActionPreference='Stop';$WarningPreference='SilentlyContinue';
        $OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);
        trap {[Console]::Error.WriteLine($_.Exception.Message);exit 1};
        $inv=[Globalization.CultureInfo]::InvariantCulture;
        function S($v){if($null -eq $v){throw '必須設定値を取得できませんでした。'};[Convert]::ToString($v,$inv)};
        function ReadIpStackRegistry($path){if(Test-Path -LiteralPath $path){Get-ItemProperty -LiteralPath $path -ErrorAction Stop}else{$null}};
        function ReadIpStackNetbiosOption($v){
          if($null -eq $v){throw 'NetBIOS 設定の保存元がありません。'};
          $p=$v.PSObject.Properties['NetbiosOptions'];if($null -eq $p -or $null -eq $p.Value){return [uint32]0};
          if(($p.Value -isnot [int] -and $p.Value -isnot [uint32]) -or [long]$p.Value -lt 0 -or [long]$p.Value -gt 2){throw 'NetBIOS 設定の保存値が不正です。'};[uint32]$p.Value
        };
        function TestIpStackManualConfiguration($v,[bool]$netbt=$false){
          if($null -eq $v){return $false};
          if($netbt){return (@($v.NameServerList|Where-Object {$_ -and $_ -ne '0.0.0.0'}).Count -gt 0 -or ($null -ne $v.NetbiosOptions -and [int]$v.NetbiosOptions -ne 0))};
          return (-not [string]::IsNullOrWhiteSpace([string]$v.NameServer) -or -not [string]::IsNullOrWhiteSpace([string]$v.ProfileNameServer) -or -not [string]::IsNullOrWhiteSpace([string]$v.Domain) -or @($v.IPAddress|Where-Object {$_ -and $_ -ne '0.0.0.0' -and $_ -ne '::'}).Count -gt 0 -or @($v.DefaultGateway|Where-Object {$_ -and $_ -ne '0.0.0.0' -and $_ -ne '::'}).Count -gt 0 -or ($null -ne $v.EnableDHCP -and [int]$v.EnableDHCP -eq 0))
        };
        function IP($s){$v=$null;if(-not [Net.IPAddress]::TryParse([string]$s,[ref]$v)){throw 'IP アドレスが不正です。'};$v.ToString()};
        function Family($n){if([int]$n -eq 2){'IPv4'}elseif([int]$n -eq 23){'IPv6'}else{throw '未対応の address family です。'}};
        """;

    // hidden/切断 NIC も列挙する。組込み loopback は NetAdapter に現れないため別識別する。
    private const string Inventory = """
        $na=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop);
        $nc=@(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -ErrorAction Stop);
        $activeif=@(Get-NetIPInterface -IncludeAllCompartments -PolicyStore ActiveStore -ErrorAction Stop);$persistentif=@(Get-NetIPInterface -IncludeAllCompartments -PolicyStore PersistentStore -ErrorAction Stop);$allif=$activeif+$persistentif;
        if(@($activeif|Where-Object {$null -eq $_.CompartmentId -or [int]$_.CompartmentId -ne 1}).Count -gt 0 -or @($persistentif|Where-Object {$null -ne $_.CompartmentId -and [int]$_.CompartmentId -ne 1}).Count -gt 0){throw 'default 以外の compartment の復元には対応していません。'};
        $ids=@{};$ad=@();
        foreach($n in $na){
          $id=([guid]$n.InterfaceGuid).ToString('D');$ix=[int]$n.InterfaceIndex;
          if($ix -le 0 -or $ids.ContainsKey($ix) -or @($ad|Where-Object AdapterId -eq $id).Count){throw 'NIC 識別子が一意ではありません。'};
          $ids[$ix]=$id;$ad+=@{AdapterId=$id;InterfaceIndex=$ix;Description=[string]$n.InterfaceDescription;PnpDeviceId=[string]$n.PnPDeviceID;HasNetworkConfiguration=$false;NetbiosOptions=0;StaticWinsServers=@()}
        };
        if(@($allif|Where-Object InterfaceIndex -eq 1).Count){$ids[1]='loopback';$ad+=@{AdapterId='loopback';InterfaceIndex=1;Description='Windows loopback';PnpDeviceId='';HasNetworkConfiguration=$false;NetbiosOptions=0;StaticWinsServers=@()}};
        foreach($i in $allif){if(-not $ids.ContainsKey([int]$i.InterfaceIndex)){throw '復元可能な NIC に対応しない IP interface があります。'}};
        foreach($c in $nc){if($c.IPEnabled -and -not $ids.ContainsKey([int]$c.InterfaceIndex)){throw 'IP 有効な NIC の取得に漏れがあります。'}};
        $r=@{SchemaVersion=1;BootId=(Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime.ToUniversalTime().ToString('O');CapturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');Adapters=@($ad);Interfaces=@();Addresses=@();Routes=@();Dns=@()};
        """;

    private const string InterfaceMap = """
        $im=[ordered]@{Dhcp='Dhcp';AutomaticMetric='AutomaticMetric';InterfaceMetric='InterfaceMetric';NlMtuBytes='NlMtu';RouterDiscovery='RouterDiscovery';Forwarding='Forwarding';Advertising='Advertising';WeakHostSend='WeakHostSend';WeakHostReceive='WeakHostReceive';IgnoreDefaultRoutes='IgnoreDefaultRoutes';ManagedAddressConfiguration='ManagedAddressConfiguration';OtherStatefulConfiguration='OtherStatefulConfiguration';NeighborUnreachabilityDetection='NeighborUnreachabilityDetection';BaseReachableTimeMs='BaseReachableTime';RetransmitTimeMs='RetransmitTime';DadTransmits='DadTransmits';DadRetransmitTimeMs='DadRetransmitTime';AdvertisedRouterLifetime='AdvertisedRouterLifetime';AdvertiseDefaultRoute='AdvertiseDefaultRoute';CurrentHopLimit='CurrentHopLimit';ForceArpNdWolPattern='ForceArpNdWolPattern';DirectedMacWolPattern='DirectedMacWolPattern';EcnMarking='EcnMarking';ClampMss='ClampMss'};
        """;

    private const string CaptureNetwork = InterfaceMap + """
        foreach($store in @('PersistentStore','ActiveStore')){
          foreach($i in @(Get-NetIPInterface -IncludeAllCompartments -PolicyStore $store -ErrorAction Stop)){
            $settings=@();foreach($k in $im.Keys){$p=$i.PSObject.Properties[$im[$k]];if($null -eq $p){if($k -eq 'ClampMss'){continue};throw ('interface 設定を取得できません: '+$k)};if($null -eq $p.Value){continue};$v=$p.Value;$settings+=@{Name=$k;Value=(S $v);IsTimeSpan=($v -is [TimeSpan])}};
            $r.Interfaces+=@{AdapterId=$ids[[int]$i.InterfaceIndex];AddressFamily=[int]$i.AddressFamily;PolicyStore=$store;Settings=@($settings)}
          };
          foreach($a in @(Get-NetIPAddress -IncludeAllCompartments -PolicyStore $store -ErrorAction Stop)){
            if([string]$a.PrefixOrigin -ne 'Manual'){continue};
            if(-not $ids.ContainsKey([int]$a.InterfaceIndex)){throw '静的 IP の NIC が見つかりません。'};
            if($a.ValidLifetime.TotalSeconds -lt [uint32]::MaxValue -or $a.PreferredLifetime.TotalSeconds -lt [uint32]::MaxValue){throw '有効期限のある静的 IP は安全に復元できません。'};
            $r.Addresses+=@{AdapterId=$ids[[int]$a.InterfaceIndex];AddressFamily=[int]$a.AddressFamily;PolicyStore=$store;Address=(IP $a.IPAddress);PrefixLength=[int]$a.PrefixLength;SkipAsSource=[bool]$a.SkipAsSource;Type=[string]$a.Type}
          };
          foreach($q in @(Get-NetRoute -IncludeAllCompartments -PolicyStore $store -ErrorAction Stop)){
            $proto=[string]$q.Protocol;
            if($proto -in @('Local','Dhcp','RouterAdvertisement','Icmp','WellKnown')){continue};
            if($proto -ne 'NetMgmt'){throw ('復元を保証できない route protocol です: '+$proto)};
            if(-not $ids.ContainsKey([int]$q.InterfaceIndex)){throw '静的 route の NIC が見つかりません。'};
            if($null -eq $q.ValidLifetime -or $null -eq $q.PreferredLifetime){throw ('route の期限が取得できません: '+$q.InterfaceAlias+' '+$q.DestinationPrefix)};
            if($q.ValidLifetime.TotalSeconds -lt [uint32]::MaxValue -or $q.PreferredLifetime.TotalSeconds -lt [uint32]::MaxValue){if($store -eq 'ActiveStore'){continue};throw ('有効期限のある永続 route は復元できません: '+$q.InterfaceAlias+' '+$q.DestinationPrefix)};
            $r.Routes+=@{AdapterId=$ids[[int]$q.InterfaceIndex];AddressFamily=[int]$q.AddressFamily;PolicyStore=$store;DestinationPrefix=[string]$q.DestinationPrefix;NextHop=(IP $q.NextHop);RouteMetric=[uint32]$q.RouteMetric;Publish=[string]$q.Publish;Protocol=$proto}
          };
          foreach($n in @(Get-NetNeighbor -IncludeAllCompartments -PolicyStore $store -ErrorAction Stop|Where-Object State -eq Permanent)){
            $ip=[Net.IPAddress]::Parse($n.IPAddress);$b=$ip.GetAddressBytes();
            if(($ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetworkV6 -and -not $ip.IsIPv6Multicast) -or ($ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and $b[0] -lt 224 -and [string]$n.LinkLayerAddress -ne 'FF-FF-FF-FF-FF-FF')){throw '手動で固定された ARP/NDP neighbor は復元対象に対応していません。'}
          }
        };
        """;

    private const string CaptureClient = """
        $dc=@(Get-DnsClient -ErrorAction Stop);$ds=@(Get-DnsClientServerAddress -ErrorAction Stop);
        foreach($service in @('Tcpip','Tcpip6','NetBT')){
          $root='Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$service+'\Parameters\Interfaces';
          if(-not (Test-Path -LiteralPath $root)){if($service -eq 'Tcpip'){throw 'TCP/IP interface registry を列挙できません。'};continue};
          foreach($key in @(Get-ChildItem -LiteralPath $root -ErrorAction Stop)){
            $name=$key.PSChildName;if($service -eq 'NetBT'){$name=$name -replace '^Tcpip_',''};$guid=[guid]::Empty;$known=[guid]::TryParse($name,[ref]$guid) -and @($ad|Where-Object AdapterId -eq $guid.ToString('D')).Count -eq 1;if($known){continue};
            $v=Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop;$fixed=TestIpStackManualConfiguration $v ($service -eq 'NetBT');
            if($fixed){throw ('現存 NIC に対応しない '+$service+' interface に固定設定があり復元できません: '+$key.PSChildName)}
          }
        };
        foreach($a in $ad){
          if($a.AdapterId -eq 'loopback'){continue};
          $cfg=@($nc|Where-Object {([guid]$_.SettingID).ToString('D') -eq $a.AdapterId});
          if($cfg.Count -gt 1){throw 'NIC の Win32 設定が重複しています。'};
          $hasif=@($allif|Where-Object InterfaceIndex -eq $a.InterfaceIndex).Count -gt 0;
          $g='{'+$a.AdapterId+'}';$v4=ReadIpStackRegistry ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\'+$g);$v6=ReadIpStackRegistry ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\'+$g);
          $nb=ReadIpStackRegistry ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_'+$g);
          if(-not $hasif){foreach($v in @($v4,$v6)){if(TestIpStackManualConfiguration $v){throw ('IP interface を再作成できない NIC に固定設定があります: '+$a.Description)}};if(TestIpStackManualConfiguration $nb $true){throw ('IP interface のない NIC の WINS/NetBIOS を復元できません: '+$a.Description)};continue};
          if($null -ne $nb){
            if($cfg.Count -ne 1){throw ('NetBIOS 設定に対応する Win32 NIC がありません: '+$a.Description)};
            $c=$cfg[0];$a.HasNetworkConfiguration=$true;
            $a.NetbiosOptions=ReadIpStackNetbiosOption $nb;
            $wins=@($nb.NameServerList|Where-Object {$_ -and $_ -ne '0.0.0.0'}|ForEach-Object {IP $_});if($wins.Count -gt 2){throw ('3件以上の固定 WINS サーバーには対応していません: '+$a.Description)};
            $effectiveWins=@($c.WINSPrimaryServer,$c.WINSSecondaryServer|Where-Object {$_ -and $_ -ne '0.0.0.0'}|ForEach-Object {IP $_});if($c.IPEnabled -and $wins.Count -and ($wins -join '|') -ne ($effectiveWins -join '|')){throw ('固定 WINS と実効値が一致しません: '+$a.Description)};$a.StaticWinsServers=@($wins)
          };
          $client=@($dc|Where-Object InterfaceIndex -eq $a.InterfaceIndex);
          if($client.Count -eq 0 -and -not @($activeif|Where-Object InterfaceIndex -eq $a.InterfaceIndex).Count){
            foreach($v in @($v4,$v6)){if($null -ne $v -and (-not [string]::IsNullOrWhiteSpace([string]$v.NameServer) -or -not [string]::IsNullOrWhiteSpace([string]$v.ProfileNameServer) -or -not [string]::IsNullOrWhiteSpace([string]$v.Domain))){throw ('DNS client のない NIC に固定 DNS 設定があり復元できません: '+$a.Description)}};continue
          };
          if($client.Count -ne 1){throw ('DNS client 設定を一意に取得できません: '+$a.Description)};
          $suffix=[string]$v4.Domain;if(-not [string]::IsNullOrEmpty($suffix) -and $suffix -ine [string]$client[0].ConnectionSpecificSuffix){throw ('固定 DNS suffix と実効値が異なります: '+$a.Description)};
          foreach($fam in @(2,23)){
            if(-not @($allif|Where-Object {[int]$_.InterfaceIndex -eq $a.InterfaceIndex -and [int]$_.AddressFamily -eq $fam}).Count){continue};
            $v=if($fam -eq 2){$v4}else{$v6};if($null -eq $v){throw ('DNS source の registry を確認できません: '+$a.Description+' '+(Family $fam))};
            if(-not [string]::IsNullOrWhiteSpace([string]$v.ProfileNameServer)){throw 'ProfileNameServer による DNS は復元に対応していません。'};
            $dns=@($ds|Where-Object {[int]$_.InterfaceIndex -eq $a.InterfaceIndex -and [int]$_.AddressFamily -eq $fam});if($dns.Count -ne 1){throw ('DNS server 列を一意に取得できません: '+$a.Description+' '+(Family $fam))};
            $static=@(([string]$v.NameServer -split '[,;\s]+')|Where-Object {$_}|ForEach-Object {IP $_});$actual=@($dns[0].ServerAddresses|ForEach-Object {IP $_});
            foreach($s in $static){if([int]([Net.IPAddress]::Parse($s)).AddressFamily -ne $fam){throw 'DNS source と address family が一致しません。'}};
            if($static.Count -and ($static -join '|') -ne ($actual -join '|')){throw '固定 DNS と実効 DNS が異なり復元を保証できません。'};
            if(-not $static.Count){
              $policy=ReadIpStackRegistry 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient';if($null -ne $policy -and -not [string]::IsNullOrWhiteSpace([string]$policy.NameServer)){throw 'ポリシーで DNS server が強制されています。'}
            };
            $r.Dns+=@{AdapterId=$a.AdapterId;AddressFamily=$fam;Automatic=($static.Count -eq 0);ServerAddresses=@($actual);AutomaticSuffix=[string]::IsNullOrEmpty($suffix);ConnectionSpecificSuffix=[string]$client[0].ConnectionSpecificSuffix;RegisterThisConnectionsAddress=[bool]$client[0].RegisterThisConnectionsAddress;UseSuffixWhenRegistering=[bool]$client[0].UseSuffixWhenRegistering}
          }
        };
        $r.Adapters=@($ad);
        """;

    private const string ReadPayload = """
        $bytes=[IO.File]::ReadAllBytes($payloadPath);$hash=[Security.Cryptography.SHA256]::Create();try{$actualHash=([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-','')}finally{$hash.Dispose()};
        if($actualHash -cne $expectedHash.ToUpperInvariant()){throw '復元ファイルの SHA256 が一致しません。'};
        $s=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -ErrorAction Stop;
        if([int]$s.SchemaVersion -ne 1){throw '未知の復元 schema です。'};$errors=[Collections.Generic.List[string]]::new();
        function Attempt([string]$label,[scriptblock]$body){try{& $body}catch{$errors.Add($label+': '+$_.Exception.Message)}};
        """;

    private const string ValidateIdentity = """
        if(@($s.Adapters).Count -ne $ad.Count){throw 'NIC の数が退避時から変わっています。'};
        $byid=@{};$savedAdapters=@{};foreach($a in $s.Adapters){$live=@($ad|Where-Object AdapterId -eq $a.AdapterId);if($live.Count -ne 1 -or $live[0].Description -cne $a.Description -or $live[0].PnpDeviceId -cne $a.PnpDeviceId -or $byid.ContainsKey([string]$a.AdapterId)){throw '退避時と同じ NIC であることを確認できません。'};$byid[[string]$a.AdapterId]=[int]$live[0].InterfaceIndex;$savedAdapters[[string]$a.AdapterId]=$a};
        function Index($id){if(-not $byid.ContainsKey([string]$id)){throw '復元対象の NIC がありません。'};$ix=$byid[[string]$id];if($id -ne 'loopback'){$live=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop|Where-Object {([guid]$_.InterfaceGuid).ToString('D') -eq $id});if($live.Count -ne 1 -or [int]$live[0].InterfaceIndex -ne $ix -or [string]$live[0].InterfaceDescription -cne [string]$savedAdapters[[string]$id].Description -or [string]$live[0].PnPDeviceID -cne [string]$savedAdapters[[string]$id].PnpDeviceId){throw '復元処理中に NIC が入れ替わりました。'}};$ix};
        """;

    private const string RestoreNetwork = """
        foreach($a in @($s.Addresses|Sort-Object @{Expression={if($_.PolicyStore -eq 'PersistentStore'){0}else{1}}})){
          Attempt ('IP '+$a.Address) {
            $ix=Index $a.AdapterId;$fam=Family $a.AddressFamily;$ip=IP $a.Address;$store=[string]$a.PolicyStore;
            if($store -notin @('ActiveStore','PersistentStore') -or $a.Type -notin @('Unicast','Anycast')){throw 'IP の復元指定が不正です。'};
            $current=@(Get-NetIPAddress -PolicyStore $store -ErrorAction Stop|Where-Object {[int]$_.InterfaceIndex -eq $ix -and [int]$_.AddressFamily -eq [int]$a.AddressFamily -and $_.IPAddress -eq $ip});
            if($current.Count -gt 1){throw '同一 IP が重複しています。'};
            if($current.Count){if([int]$current[0].PrefixLength -ne [int]$a.PrefixLength -or [bool]$current[0].SkipAsSource -ne [bool]$a.SkipAsSource){$null=Index $a.AdapterId;Set-NetIPAddress -InputObject $current[0] -PrefixLength ([byte]$a.PrefixLength) -SkipAsSource ([bool]$a.SkipAsSource) -ErrorAction Stop|Out-Null}}
            else{
              $arg=@{InterfaceIndex=$ix;IPAddress=$ip;AddressFamily=$fam;PrefixLength=[byte]$a.PrefixLength;SkipAsSource=[bool]$a.SkipAsSource;Type=[string]$a.Type;ErrorAction='Stop'};
              if($store -eq 'ActiveStore'){$arg.PolicyStore='ActiveStore'};
              $null=Index $a.AdapterId;New-NetIPAddress @arg|Out-Null;
              if($store -eq 'PersistentStore' -and -not @($s.Addresses|Where-Object {$_.AdapterId -eq $a.AdapterId -and $_.AddressFamily -eq $a.AddressFamily -and $_.Address -eq $a.Address -and $_.PolicyStore -eq 'ActiveStore'}).Count){
                $ix=Index $a.AdapterId;Get-NetIPAddress -PolicyStore ActiveStore -ErrorAction Stop|Where-Object {[int]$_.InterfaceIndex -eq $ix -and [int]$_.AddressFamily -eq [int]$a.AddressFamily -and $_.IPAddress -eq $ip}|Remove-NetIPAddress -Confirm:$false -ErrorAction Stop|Out-Null
              }
            }
          }
        };
        foreach($q in @($s.Routes|Sort-Object @{Expression={if($_.PolicyStore -eq 'PersistentStore'){0}else{1}}})){
          Attempt ('route '+$q.DestinationPrefix) {
            $ix=Index $q.AdapterId;$fam=Family $q.AddressFamily;$hop=IP $q.NextHop;
            if($q.PolicyStore -notin @('ActiveStore','PersistentStore') -or $q.Protocol -ne 'NetMgmt' -or $q.Publish -notin @('No','Age','Yes')){throw 'route の復元指定が不正です。'};
            $current=@(Get-NetRoute -PolicyStore $q.PolicyStore -ErrorAction Stop|Where-Object {[int]$_.InterfaceIndex -eq $ix -and [int]$_.AddressFamily -eq [int]$q.AddressFamily -and $_.DestinationPrefix -eq $q.DestinationPrefix -and $_.NextHop -eq $hop});
            if($current.Count -gt 1){throw 'route が重複しています。'};
            if($current.Count){if([uint32]$current[0].RouteMetric -ne [uint32]$q.RouteMetric -or [string]$current[0].Publish -cne [string]$q.Publish){$null=Index $q.AdapterId;Set-NetRoute -InputObject $current[0] -RouteMetric ([uint32]$q.RouteMetric) -Publish $q.Publish -ErrorAction Stop|Out-Null}}
            else{
              $ix=Index $q.AdapterId;$arg=@{InterfaceIndex=$ix;AddressFamily=$fam;DestinationPrefix=[string]$q.DestinationPrefix;NextHop=$hop;RouteMetric=[uint32]$q.RouteMetric;Publish=[string]$q.Publish;Protocol='NetMgmt';ErrorAction='Stop'};
              if($q.PolicyStore -eq 'ActiveStore'){$arg.PolicyStore='ActiveStore'};New-NetRoute @arg|Out-Null;
              if($q.PolicyStore -eq 'PersistentStore' -and -not @($s.Routes|Where-Object {$_.AdapterId -eq $q.AdapterId -and $_.AddressFamily -eq $q.AddressFamily -and $_.DestinationPrefix -eq $q.DestinationPrefix -and $_.NextHop -eq $q.NextHop -and $_.PolicyStore -eq 'ActiveStore'}).Count){
                $ix=Index $q.AdapterId;Get-NetRoute -PolicyStore ActiveStore -ErrorAction Stop|Where-Object {[int]$_.InterfaceIndex -eq $ix -and [int]$_.AddressFamily -eq [int]$q.AddressFamily -and $_.DestinationPrefix -eq $q.DestinationPrefix -and $_.NextHop -eq $hop -and [string]$_.Protocol -eq 'NetMgmt'}|Remove-NetRoute -Confirm:$false -ErrorAction Stop|Out-Null
              }
            }
          }
        };
        """;

    private const string RestoreInterfaces = InterfaceMap + """
        foreach($i in @($s.Interfaces|Sort-Object @{Expression={if($_.PolicyStore -eq 'PersistentStore'){0}else{1}}})){
          Attempt ('interface '+$i.AdapterId+' '+$i.AddressFamily+' '+$i.PolicyStore) {
            $ix=Index $i.AdapterId;$fam=Family $i.AddressFamily;if($i.PolicyStore -notin @('ActiveStore','PersistentStore')){throw 'interface store が不正です。'};
            $current=@(Get-NetIPInterface -InterfaceIndex $ix -AddressFamily $fam -PolicyStore $i.PolicyStore -ErrorAction Stop);if($current.Count -ne 1){throw 'interface を一意に取得できません。'};
            $arg=@{InputObject=$current[0];ErrorAction='Stop'};$dhcp=$null;$metric=$null;$auto=$null;
            foreach($v in $i.Settings){if(-not $im.Contains($v.Name)){throw '未知の interface 設定です。'};$value=if($v.IsTimeSpan){[TimeSpan]::ParseExact($v.Value,'c',$inv)}else{[string]$v.Value};if($v.Name -eq 'Dhcp'){$dhcp=$value}elseif($v.Name -eq 'InterfaceMetric'){$metric=$value}elseif($v.Name -eq 'AutomaticMetric'){$auto=$value}elseif((S $current[0].PSObject.Properties[$im[$v.Name]].Value) -cne [string]$v.Value){$arg[[string]$v.Name]=$value}};
            if($arg.Count -gt 2){$null=Index $i.AdapterId;Set-NetIPInterface @arg|Out-Null};
            if($null -ne $metric -and $auto -eq 'Disabled' -and [uint32]$current[0].InterfaceMetric -ne [uint32]$metric){$ix=Index $i.AdapterId;Set-NetIPInterface -InterfaceIndex $ix -AddressFamily $fam -PolicyStore $i.PolicyStore -InterfaceMetric ([uint32]$metric) -ErrorAction Stop|Out-Null};
            if($null -ne $auto -and (S $current[0].AutomaticMetric) -cne [string]$auto){$ix=Index $i.AdapterId;Set-NetIPInterface -InterfaceIndex $ix -AddressFamily $fam -PolicyStore $i.PolicyStore -AutomaticMetric $auto -ErrorAction Stop|Out-Null};
            if($null -ne $dhcp -and (S $current[0].Dhcp) -cne [string]$dhcp){$ix=Index $i.AdapterId;Set-NetIPInterface -InterfaceIndex $ix -AddressFamily $fam -PolicyStore $i.PolicyStore -Dhcp $dhcp -ErrorAction Stop|Out-Null}
          }
        };
        """;

    private const string RestoreClient = """
        foreach($a in @($s.Adapters|Where-Object HasNetworkConfiguration)){
          Attempt ('WINS/NetBIOS '+$a.AdapterId) {
            $ix=Index $a.AdapterId;$cfg=@(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -ErrorAction Stop|Where-Object {([guid]$_.SettingID).ToString('D') -eq $a.AdapterId -and [int]$_.InterfaceIndex -eq $ix});if($cfg.Count -ne 1){throw 'Win32 NIC を一意に確認できません。'};
            $primary='';$secondary='';if(@($a.StaticWinsServers).Count -gt 2){throw 'WINS server 列が不正です。'};if(@($a.StaticWinsServers).Count -gt 0){$primary=IP $a.StaticWinsServers[0]};if(@($a.StaticWinsServers).Count -gt 1){$secondary=IP $a.StaticWinsServers[1]};
            $nb=ReadIpStackRegistry ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_{'+$a.AdapterId+'}');$currentWins=@($nb.NameServerList|Where-Object {$_ -and $_ -ne '0.0.0.0'}|ForEach-Object {IP $_});
            if(($currentWins -join '|') -ne (@($a.StaticWinsServers) -join '|')){$null=Index $a.AdapterId;$result=Invoke-CimMethod -InputObject $cfg[0] -MethodName SetWINSServer -Arguments @{WINSPrimaryServer=$primary;WINSSecondaryServer=$secondary} -ErrorAction Stop;if([uint32]$result.ReturnValue -notin @(0,1)){throw ('SetWINSServer: '+$result.ReturnValue)}};
            if([uint32]$a.NetbiosOptions -gt 2){throw 'NetBIOS option が不正です。'};if((ReadIpStackNetbiosOption $nb) -ne [uint32]$a.NetbiosOptions){$null=Index $a.AdapterId;$result=Invoke-CimMethod -InputObject $cfg[0] -MethodName SetTcpipNetbios -Arguments @{TcpipNetbiosOptions=[uint32]$a.NetbiosOptions} -ErrorAction Stop;if([uint32]$result.ReturnValue -notin @(0,1)){throw ('SetTcpipNetbios: '+$result.ReturnValue)}};
          }
        };
        foreach($d in $s.Dns){
          Attempt ('DNS '+$d.AdapterId+' '+$d.AddressFamily) {
            $ix=Index $d.AdapterId;$fam=Family $d.AddressFamily;$obj=@(Get-DnsClientServerAddress -InterfaceIndex $ix -AddressFamily $fam -ErrorAction Stop);if($obj.Count -ne 1){throw 'DNS family を一意に確認できません。'};
            $null=Index $d.AdapterId;if($d.Automatic){Set-DnsClientServerAddress -InputObject $obj[0] -ResetServerAddresses -ErrorAction Stop|Out-Null}else{$servers=@($d.ServerAddresses|ForEach-Object {IP $_});if(-not $servers.Count){throw '固定 DNS server 列が空です。'};Set-DnsClientServerAddress -InputObject $obj[0] -ServerAddresses $servers -ErrorAction Stop|Out-Null};
            $ix=Index $d.AdapterId;$dnsarg=@{InterfaceIndex=$ix;RegisterThisConnectionsAddress=[bool]$d.RegisterThisConnectionsAddress;UseSuffixWhenRegistering=[bool]$d.UseSuffixWhenRegistering;ErrorAction='Stop'};if($d.AutomaticSuffix){$dnsarg.ResetConnectionSpecificSuffix=$true}else{$dnsarg.ConnectionSpecificSuffix=[string]$d.ConnectionSpecificSuffix};Set-DnsClient @dnsarg|Out-Null
          }
        };
        """;

}
