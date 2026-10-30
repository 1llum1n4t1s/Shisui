using System.Text;

namespace Shisui.Core.Services.Windows;

/// <summary>物理 NIC の識別・詳細プロパティ読取と、限定された変更コマンドを生成する。</summary>
public static class WindowsGamingNetworkProfileCommandBuilder
{
    public const string FileName = "powershell";
    public const string InterruptModerationKeyword = WindowsGamingNetworkProfilePolicy.InterruptModerationKeyword;
    public const string EnergyEfficientEthernetKeyword = WindowsGamingNetworkProfilePolicy.EnergyEfficientEthernetKeyword;
    public const string MediaTekLowPowerKeyword = WindowsGamingNetworkProfilePolicy.MediaTekLowPowerKeyword;
    public const string MediaTekUapsdKeyword = WindowsGamingNetworkProfilePolicy.MediaTekUapsdKeyword;
    public const string MediaTekAmsduTxKeyword = WindowsGamingNetworkProfilePolicy.MediaTekAmsduTxKeyword;
    public const string RealtekGreenEthernetKeyword = WindowsGamingNetworkProfilePolicy.RealtekGreenEthernetKeyword;
    public const string RealtekGigaLiteKeyword = WindowsGamingNetworkProfilePolicy.RealtekGigaLiteKeyword;
    public const string RealtekPowerSavingModeKeyword = WindowsGamingNetworkProfilePolicy.RealtekPowerSavingModeKeyword;
    public const string FlowControlKeyword = WindowsGamingNetworkProfilePolicy.FlowControlKeyword;
    public const string RealtekAdvancedEeeKeyword = WindowsGamingNetworkProfilePolicy.RealtekAdvancedEeeKeyword;
    public const string UdpRscKeyword = WindowsGamingNetworkProfilePolicy.UdpRscKeyword;
    public const string SelectiveSuspendKeyword = WindowsGamingNetworkProfilePolicy.SelectiveSuspendKeyword;
    public const string DeviceIdleEnabledKeyword = WindowsGamingNetworkProfilePolicy.DeviceIdleEnabledKeyword;
    public const string WdfDeviceIdleEnabledKeyword = WindowsGamingNetworkProfilePolicy.WdfDeviceIdleEnabledKeyword;

    public static IReadOnlyList<string> AllowedKeywords { get; } =
        WindowsGamingNetworkProfilePolicy.AllKnownKeywords;

    public static string BuildQueryArguments(string adapterName)
    {
        var name = QuotePowerShellLiteral(adapterName, nameof(adapterName));
        var script =
            "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
            "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$a=@(Get-NetAdapter -Name '*' -IncludeHidden -ErrorAction Stop | " +
            $"Where-Object {{[string]::Equals([string]$_.Name,{name},[StringComparison]::OrdinalIgnoreCase)}});" +
            "if($a.Count -ne 1){throw 'Adapter resolution was not unique'};" +
            "$p=@();" +
            "$allowed=@();" +
            "if([bool]$a[0].HardwareInterface){" +
            $"$allowed=@({BuildKeywordArray(AllowedKeywords)} | Where-Object {{{BuildPowerShellPolicyExpression("$a[0]", "$_")}}})}};" +
            "if($allowed.Count -gt 0){" +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
            "$p=@(Get-NetAdapterAdvancedProperty -Name $escaped -IncludeHidden -AllProperties -ErrorAction Stop | " +
            "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase) -and " +
            $"$allowed -ccontains [string]$_.RegistryKeyword -and $_.RegistryKeyword -cne '{SelectiveSuspendKeyword}' -and $_.RegistryKeyword -cne '{DeviceIdleEnabledKeyword}' -and $_.RegistryKeyword -cne '{WdfDeviceIdleEnabledKeyword}'}});" +
            "foreach($x in $p){" +
            "$expectedInstance=([guid]$a[0].InterfaceGuid).ToString('B')+'::'+[string]$x.RegistryKeyword;" +
            "if(-not [string]::Equals([string]$x.InterfaceDescription,[string]$a[0].InterfaceDescription,[StringComparison]::Ordinal) -or " +
            "-not [string]::Equals([string]$x.InstanceID,$expectedInstance,[StringComparison]::OrdinalIgnoreCase))" +
            "{throw 'Advanced property identity mismatch'}}};" +
            "$props=@($p | ForEach-Object {[pscustomobject]@{RegistryKeyword=[string]$_.RegistryKeyword;" +
            "RegistryValues=@($_.RegistryValue | ForEach-Object {[string]$_});" +
            "ValidRegistryValues=@($_.ValidRegistryValues | ForEach-Object {[string]$_})}});" +
            "$powerError='';" +
            $"if($allowed -ccontains '{SelectiveSuspendKeyword}'){{try{{" +
            "$pm=@(Get-NetAdapterPowerManagement -Name $escaped -IncludeHidden -ErrorAction Stop | " +
            "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase)});" +
            "if($pm.Count -gt 1){throw 'Power management resolution was not unique'};" +
            "if($pm.Count -eq 1){" +
            "if(-not [string]::Equals([string]$pm[0].InterfaceDescription,[string]$a[0].InterfaceDescription,[StringComparison]::Ordinal) -or " +
            "-not [string]::Equals([string]$pm[0].InstanceID,([guid]$a[0].InterfaceGuid).ToString('B'),[StringComparison]::OrdinalIgnoreCase))" +
            "{throw 'Power management identity mismatch'};" +
            "if($null -ne $pm[0].AllowComputerToTurnOffDevice -and [int]$pm[0].AllowComputerToTurnOffDevice -eq 2 -and " +
            "$null -ne $pm[0].SelectiveSuspend -and [int]$pm[0].SelectiveSuspend -in @(1,2)){" +
            $"$props+= [pscustomobject]@{{RegistryKeyword='{SelectiveSuspendKeyword}';RegistryValues=@([string][int]$pm[0].SelectiveSuspend);ValidRegistryValues=@('1','2')}}" +
            "}}}catch{if($_.FullyQualifiedErrorId -cne 'CmdletizationQuery_NotFound_Name,Get-NetAdapterPowerManagement')" +
            "{$powerError=[string]$_.Exception.Message}}};" +
            "$deviceIdleError='';" +
            $"if($allowed -ccontains '{DeviceIdleEnabledKeyword}'){{try{{" +
            BuildWdfLookup() +
            "if($null -ne $wdf){" +
            BuildWdfValidate() +
            $"$props+= [pscustomobject]@{{RegistryKeyword='{WdfDeviceIdleEnabledKeyword}';RegistryValues=@([string]$idle);ValidRegistryValues=@('0','1')}}" +
            "}else{" +
            "if([string]::IsNullOrWhiteSpace([string]$a[0].DriverFileName)){throw 'Driver filename is unavailable'};" +
            BuildDeviceIdleQuery() +
            "if($d.Count -eq 1 -and $d[0].Active){" +
            $"$props+= [pscustomobject]@{{RegistryKeyword='{DeviceIdleEnabledKeyword}';RegistryValues=@($(if($d[0].Enable){{'1'}}else{{'0'}}));ValidRegistryValues=@('0','1')}}" +
            "}}}catch{if($_.FullyQualifiedErrorId -cne 'HRESULT 0x80041010,Microsoft.Management.Infrastructure.CimCmdlets.GetCimInstanceCommand')" +
            "{$deviceIdleError=[string]$_.Exception.Message}}};" +
            "$o=[pscustomobject]@{" +
            "AdapterName=[string]$a[0].Name;" +
            "InterfaceGuid=[string]$a[0].InterfaceGuid;" +
            "InterfaceDescription=[string]$a[0].InterfaceDescription;" +
            "HardwareInterface=[bool]$a[0].HardwareInterface;" +
            "InterfaceType=[int]$a[0].InterfaceType;" +
            "DriverProvider=[string]$a[0].DriverProvider;" +
            "DriverFileName=[string]$a[0].DriverFileName;" +
            "PnPDeviceId=[string]$a[0].PnPDeviceID;" +
            "Properties=@($props);PowerManagementError=$powerError;DeviceIdleError=$deviceIdleError};" +
            "$o | ConvertTo-Json -Compress -Depth 5";
        return BuildEncodedArguments(script);
    }

    public static string BuildSetPropertyArguments(
        string adapterName,
        Guid expectedAdapterGuid,
        string expectedAdapterDescription,
        int expectedInterfaceType,
        string expectedDriverProvider,
        string expectedPnpDeviceId,
        string registryKeyword,
        string expectedCurrentValue,
        string targetValue,
        string? expectedDriverFileName = null)
    {
        if (!WindowsGamingNetworkProfilePolicy.IsKeywordAllowed(
                true,
                expectedInterfaceType,
                expectedDriverProvider,
                expectedPnpDeviceId,
                registryKeyword))
        {
            throw new ArgumentOutOfRangeException(nameof(registryKeyword), registryKeyword, "変更対象外の NIC プロパティです");
        }

        if (expectedAdapterGuid == Guid.Empty)
        {
            throw new ArgumentException("NIC GUID が空です", nameof(expectedAdapterGuid));
        }

        ValidateKnownValue(registryKeyword, expectedCurrentValue, nameof(expectedCurrentValue));
        ValidateKnownValue(registryKeyword, targetValue, nameof(targetValue));
        if (registryKeyword is DeviceIdleEnabledKeyword or WdfDeviceIdleEnabledKeyword && string.IsNullOrWhiteSpace(expectedDriverFileName))
        {
            throw new ArgumentException("NIC ドライバーファイル名が空です", nameof(expectedDriverFileName));
        }
        var requiredValues = WindowsGamingNetworkProfilePolicy.GetRequiredSupportedValues(
            registryKeyword, expectedCurrentValue, targetValue);
        var supportedValueChecks = string.Join(" -or ", requiredValues.Select(value =>
            $"-not ($valid -ccontains {QuotePowerShellLiteral(value, nameof(requiredValues))})"));

        var name = QuotePowerShellLiteral(adapterName, nameof(adapterName));
        var guid = QuotePowerShellLiteral(expectedAdapterGuid.ToString("D"), nameof(expectedAdapterGuid));
        var description = QuotePowerShellLiteral(expectedAdapterDescription, nameof(expectedAdapterDescription));
        var provider = QuotePowerShellLiteral(expectedDriverProvider, nameof(expectedDriverProvider));
        var pnpDeviceId = QuotePowerShellLiteral(expectedPnpDeviceId, nameof(expectedPnpDeviceId));
        var keyword = QuotePowerShellLiteral(registryKeyword, nameof(registryKeyword));
        var expected = QuotePowerShellLiteral(expectedCurrentValue, nameof(expectedCurrentValue));
        var target = QuotePowerShellLiteral(targetValue, nameof(targetValue));
        var propertyWrite = registryKeyword switch
        {
            SelectiveSuspendKeyword => BuildPowerManagementWrite(guid, description, expected, target),
            DeviceIdleEnabledKeyword => BuildDeviceIdleWrite(expected, target),
            WdfDeviceIdleEnabledKeyword => BuildWdfWrite(expected, target),
            _ =>
            "$p=@(Get-NetAdapterAdvancedProperty -Name $escaped -IncludeHidden -AllProperties -ErrorAction Stop | " +
            "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase) -and " +
            $"$_.RegistryKeyword -ceq {keyword}}});" +
            "if($p.Count -ne 1){throw 'Advanced property resolution was not unique'};" +
            $"$expectedInstance=([guid]{guid}).ToString('B')+'::'+{keyword};" +
            $"if(-not [string]::Equals([string]$p[0].InterfaceDescription,{description},[StringComparison]::Ordinal) -or " +
            "-not [string]::Equals([string]$p[0].InstanceID,$expectedInstance,[StringComparison]::OrdinalIgnoreCase))" +
            "{throw 'Advanced property identity mismatch'};" +
            "$current=@($p[0].RegistryValue | ForEach-Object {[string]$_});" +
            $"if($current.Count -ne 1 -or $current[0] -cne {expected}){{throw 'Advanced property value changed'}};" +
            "$valid=@($p[0].ValidRegistryValues | ForEach-Object {[string]$_});" +
            "if(@($valid | Select-Object -Unique).Count -ne $valid.Count){throw 'Advanced property values are duplicated'};" +
            $"if({supportedValueChecks}){{throw 'Advanced property values are unsupported'}};" +
            $"Set-NetAdapterAdvancedProperty -InputObject $p[0] -RegistryValue {target} -NoRestart -Confirm:$false -ErrorAction Stop;",
        };
        var script =
            "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
            "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$a=@(Get-NetAdapter -Name '*' -IncludeHidden -ErrorAction Stop | " +
            $"Where-Object {{[string]::Equals([string]$_.Name,{name},[StringComparison]::OrdinalIgnoreCase)}});" +
            "if($a.Count -ne 1){throw 'Adapter resolution was not unique'};" +
            $"if(-not [bool]$a[0].HardwareInterface -or [int]$a[0].InterfaceType -ne {expectedInterfaceType})" +
            "{throw 'Adapter media changed'};" +
            $"if([guid]$a[0].InterfaceGuid -ne [guid]{guid} -or " +
            $"-not [string]::Equals([string]$a[0].InterfaceDescription,{description},[StringComparison]::Ordinal))" +
            "{throw 'Adapter identity changed'};" +
            $"if(-not [string]::Equals([string]$a[0].DriverProvider,{provider},[StringComparison]::Ordinal) -or " +
            $"-not [string]::Equals([string]$a[0].PnPDeviceID,{pnpDeviceId},[StringComparison]::OrdinalIgnoreCase))" +
            "{throw 'Adapter provider identity changed'};" +
            (registryKeyword is DeviceIdleEnabledKeyword or WdfDeviceIdleEnabledKeyword
                ? $"if(-not [string]::Equals([string]$a[0].DriverFileName,{QuotePowerShellLiteral(expectedDriverFileName!, nameof(expectedDriverFileName))},[StringComparison]::OrdinalIgnoreCase)){{throw 'Adapter driver filename changed'}};"
                : string.Empty) +
            $"$allowed=({BuildPowerShellPolicyExpression("$a[0]", keyword)});" +
            "if(-not $allowed){throw 'Advanced property is not allowed for this adapter'};" +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
            propertyWrite +
            $"'UPDATED='+{keyword}+';VALUE='+{target}";
        return BuildEncodedArguments(script);
    }

    private static string BuildDeviceIdleQuery() =>
        "$pattern='^'+[regex]::Escape([string]$a[0].PnPDeviceID)+'_[0-9]+\\z';" +
        "$d=@(Get-CimInstance -Namespace 'root/wmi' -ClassName 'MSPower_DeviceEnable' -ErrorAction Stop | " +
        "Where-Object {$_.InstanceName -is [string] -and $_.InstanceName -match $pattern});" +
        "if($d.Count -gt 1){throw 'Device power instance resolution was not unique'};" +
        "if($d.Count -eq 1 -and ($d[0].Active -isnot [bool] -or $d[0].Enable -isnot [bool]))" +
        "{throw 'Device power instance has invalid value types'};";

    private static string BuildDeviceIdleWrite(string expected, string target) =>
        BuildWdfLookup() +
        "if($null -ne $wdf){throw 'WDF power settings appeared; WMI write refused'};" +
        BuildDeviceIdleQuery() +
        "if($d.Count -ne 1 -or -not $d[0].Active){throw 'Device power instance is unavailable or inactive'};" +
        $"if($(if($d[0].Enable){{'1'}}else{{'0'}}) -cne {expected}){{throw 'Device power value changed'}};" +
        $"$enabled=({target} -ceq '1');" +
        "Set-CimInstance -InputObject $d[0] -Property @{Enable=$enabled} -ErrorAction Stop;";

    private static string BuildWdfLookup(bool writable = false) =>
        "$pnp=[string]$a[0].PnPDeviceID;" +
        "if($pnp -notmatch '^[A-Za-z0-9_&.{}-]+\\\\[A-Za-z0-9_&.{}-]+\\\\[A-Za-z0-9_&.{}-]+\\z' -or " +
        "@($pnp.Split('\\') | Where-Object {$_ -eq '.' -or $_ -eq '..'}).Count -ne 0){throw 'PnP デバイス識別子が不正です'};" +
        "$base=Get-Item -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Enum\\'+$pnp) -ErrorAction Stop;" +
        $"$wdf=$base.OpenSubKey('Device Parameters\\WDF',${(writable ? "true" : "false")});";

    private static string BuildWdfValidate() =>
        $"if(-not [string]::Equals([string]$a[0].DriverFileName,'{WindowsGamingNetworkProfilePolicy.VerifiedWdfDriver}',[StringComparison]::OrdinalIgnoreCase))" +
        "{throw 'WDF ドライバーが確認済みの対象ではありません'};" +
        "if(@($wdf.GetValueNames()) -notcontains 'IdleInWorkingState'){throw 'WDF IdleInWorkingState が存在しません'};" +
        "if($wdf.GetValueKind('IdleInWorkingState') -ne [Microsoft.Win32.RegistryValueKind]::DWord)" +
        "{throw 'WDF IdleInWorkingState が DWORD ではありません'};" +
        "$idle=$wdf.GetValue('IdleInWorkingState');" +
        "if($idle -isnot [int] -or $idle -notin @(0,1)){throw 'WDF IdleInWorkingState の値が不正です'};";

    private static string BuildWdfWrite(string expected, string target) =>
        BuildWdfLookup(writable: true) +
        "if($null -eq $wdf){throw 'WDF power settings disappeared'};" +
        BuildWdfValidate() +
        $"if([string]$idle -cne {expected}){{throw 'WDF idle value changed'}};" +
        $"$wdf.SetValue('IdleInWorkingState',[int]{target},[Microsoft.Win32.RegistryValueKind]::DWord);";

    private static string BuildPowerManagementWrite(string guid, string description, string expected, string target) =>
        "$pm=@(Get-NetAdapterPowerManagement -Name $escaped -IncludeHidden -ErrorAction Stop | " +
        "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase)});" +
        "if($pm.Count -ne 1){throw 'Power management resolution was not unique'};" +
        $"if(-not [string]::Equals([string]$pm[0].InterfaceDescription,{description},[StringComparison]::Ordinal) -or " +
        $"-not [string]::Equals([string]$pm[0].InstanceID,([guid]{guid}).ToString('B'),[StringComparison]::OrdinalIgnoreCase))" +
        "{throw 'Power management identity mismatch'};" +
        "if($null -eq $pm[0].AllowComputerToTurnOffDevice -or [int]$pm[0].AllowComputerToTurnOffDevice -ne 2)" +
        "{throw 'Power management master setting changed or is unsupported'};" +
        $"if($null -eq $pm[0].SelectiveSuspend -or [string][int]$pm[0].SelectiveSuspend -cne {expected})" +
        "{throw 'Selective suspend value changed or is unsupported'};" +
        $"$setting=if({target} -ceq '1'){{'Disabled'}}else{{'Enabled'}};" +
        "Set-NetAdapterPowerManagement -InputObject $pm[0] -SelectiveSuspend $setting -NoRestart -Confirm:$false -ErrorAction Stop;";

    private static string QuotePowerShellLiteral(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("空の値は PowerShell へ渡せません", parameterName);
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static void ValidateKnownValue(string registryKeyword, string value, string parameterName)
    {
        if (!WindowsGamingNetworkProfilePolicy.IsKnownValue(registryKeyword, value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "NIC プロパティの既知の列挙値だけが対象です");
        }
    }

    private static string BuildKeywordArray(IEnumerable<string> keywords) =>
        $"@({string.Join(",", keywords.Select(keyword => QuotePowerShellLiteral(keyword, nameof(keywords))))})";

    private static string BuildPowerShellPolicyExpression(string adapterExpression, string quotedKeyword) =>
        $"({BuildKeywordArray(WindowsGamingNetworkProfilePolicy.StandardOffloadKeywords)} -ccontains {quotedKeyword} -and " +
        $"([int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType} -or " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType})) -or " +
        $"(({quotedKeyword} -ceq '{InterruptModerationKeyword}' -or {quotedKeyword} -ceq '{UdpRscKeyword}') -and " +
        $"([int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType} -or " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType})) -or " +
        $"({quotedKeyword} -ceq '{EnergyEfficientEthernetKeyword}' -and [int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType}) -or " +
        $"(({quotedKeyword} -ceq '{RealtekGreenEthernetKeyword}' -or {quotedKeyword} -ceq '{RealtekGigaLiteKeyword}' -or {quotedKeyword} -ceq '{RealtekPowerSavingModeKeyword}' -or {quotedKeyword} -ceq '{SelectiveSuspendKeyword}' -or {quotedKeyword} -ceq '{DeviceIdleEnabledKeyword}' -or {quotedKeyword} -ceq '{WdfDeviceIdleEnabledKeyword}') -and " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType} -and " +
        $"(([string]{adapterExpression}.DriverProvider).StartsWith('{WindowsGamingNetworkProfilePolicy.RealtekDriverProviderToken}',[StringComparison]::OrdinalIgnoreCase) -or " +
        $"[string]{adapterExpression}.PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.RealtekPciPnpPrefix}*' -or " +
        $"[string]{adapterExpression}.PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.RealtekUsbPnpPrefix}*')) -or " +
        $"(({quotedKeyword} -ceq '{FlowControlKeyword}' -or {quotedKeyword} -ceq '{RealtekAdvancedEeeKeyword}') -and " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType} -and " +
        $"[string]{adapterExpression}.PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.RealtekPciPnpPrefix}*') -or " +
        $"(({quotedKeyword} -ceq '{MediaTekLowPowerKeyword}' -or {quotedKeyword} -ceq '{MediaTekUapsdKeyword}') -and " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType} -and " +
        $"[string]::Equals([string]{adapterExpression}.DriverProvider,'{WindowsGamingNetworkProfilePolicy.MediaTekDriverProvider}',[StringComparison]::Ordinal) -and " +
        $"[string]{adapterExpression}.PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.MediaTekPnpPrefix}*') -or " +
        $"({quotedKeyword} -ceq '{MediaTekAmsduTxKeyword}' -and " +
        // 元の CIM 属性の型を、query の JSON 用変換や setter の媒体変換よりも厳密に確認する。
        $"{adapterExpression}.HardwareInterface -is [bool] -and {adapterExpression}.HardwareInterface -eq $true -and " +
        $"$null -ne {adapterExpression}.InterfaceType -and " +
        $"{adapterExpression}.InterfaceType.GetType().FullName -cmatch '^System\\.(SByte|Byte|U?Int(16|32|64))$' -and " +
        $"{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType} -and " +
        $"[string]::Equals([string]{adapterExpression}.DriverProvider,'{WindowsGamingNetworkProfilePolicy.MediaTekDriverProvider}',[StringComparison]::Ordinal) -and " +
        $"[string]{adapterExpression}.PnPDeviceID -match '^{System.Text.RegularExpressions.Regex.Escape(WindowsGamingNetworkProfilePolicy.MediaTekRz616PnpPrefix)}(?:&|\\\\|\\z)')";

    private static string BuildEncodedArguments(string script) =>
        $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}";
}
