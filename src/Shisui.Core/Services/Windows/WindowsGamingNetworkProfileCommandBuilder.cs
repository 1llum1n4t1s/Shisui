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
            $"if([bool]$a[0].HardwareInterface -and [int]$a[0].InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType})" +
            $"{{$allowed=@('{InterruptModerationKeyword}','{EnergyEfficientEthernetKeyword}')}}" +
            $"elseif([bool]$a[0].HardwareInterface -and [int]$a[0].InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType}){{" +
            $"$allowed=@('{InterruptModerationKeyword}');" +
            $"if([string]::Equals([string]$a[0].DriverProvider,'{WindowsGamingNetworkProfilePolicy.MediaTekDriverProvider}',[StringComparison]::Ordinal) -and " +
            $"[string]$a[0].PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.MediaTekPnpPrefix}*')" +
            $"{{$allowed+=@('{MediaTekLowPowerKeyword}','{MediaTekUapsdKeyword}')}}}};" +
            "if($allowed.Count -gt 0){" +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
            "$p=@(Get-NetAdapterAdvancedProperty -Name $escaped -IncludeHidden -AllProperties -ErrorAction Stop | " +
            "Where-Object {[string]::Equals([string]$_.Name,[string]$a[0].Name,[StringComparison]::OrdinalIgnoreCase) -and " +
            "$allowed -ccontains [string]$_.RegistryKeyword});" +
            "foreach($x in $p){" +
            "$expectedInstance=([guid]$a[0].InterfaceGuid).ToString('B')+'::'+[string]$x.RegistryKeyword;" +
            "if(-not [string]::Equals([string]$x.InterfaceDescription,[string]$a[0].InterfaceDescription,[StringComparison]::Ordinal) -or " +
            "-not [string]::Equals([string]$x.InstanceID,$expectedInstance,[StringComparison]::OrdinalIgnoreCase))" +
            "{throw 'Advanced property identity mismatch'}}};" +
            "$o=[pscustomobject]@{" +
            "AdapterName=[string]$a[0].Name;" +
            "InterfaceGuid=[string]$a[0].InterfaceGuid;" +
            "InterfaceDescription=[string]$a[0].InterfaceDescription;" +
            "HardwareInterface=[bool]$a[0].HardwareInterface;" +
            "InterfaceType=[int]$a[0].InterfaceType;" +
            "DriverProvider=[string]$a[0].DriverProvider;" +
            "PnPDeviceId=[string]$a[0].PnPDeviceID;" +
            "Properties=@($p | ForEach-Object {[pscustomobject]@{" +
            "RegistryKeyword=[string]$_.RegistryKeyword;" +
            "RegistryValues=@($_.RegistryValue | ForEach-Object {[string]$_});" +
            "ValidRegistryValues=@($_.ValidRegistryValues | ForEach-Object {[string]$_})}})};" +
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
        string targetValue)
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

        ValidateBinaryValue(expectedCurrentValue, nameof(expectedCurrentValue));
        ValidateBinaryValue(targetValue, nameof(targetValue));

        var name = QuotePowerShellLiteral(adapterName, nameof(adapterName));
        var guid = QuotePowerShellLiteral(expectedAdapterGuid.ToString("D"), nameof(expectedAdapterGuid));
        var description = QuotePowerShellLiteral(expectedAdapterDescription, nameof(expectedAdapterDescription));
        var provider = QuotePowerShellLiteral(expectedDriverProvider, nameof(expectedDriverProvider));
        var pnpDeviceId = QuotePowerShellLiteral(expectedPnpDeviceId, nameof(expectedPnpDeviceId));
        var keyword = QuotePowerShellLiteral(registryKeyword, nameof(registryKeyword));
        var expected = QuotePowerShellLiteral(expectedCurrentValue, nameof(expectedCurrentValue));
        var target = QuotePowerShellLiteral(targetValue, nameof(targetValue));
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
            $"$allowed=({BuildPowerShellPolicyExpression("$a[0]", keyword)});" +
            "if(-not $allowed){throw 'Advanced property is not allowed for this adapter'};" +
            "$escaped=[WildcardPattern]::Escape([string]$a[0].Name);" +
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
            "if(-not ($valid -ccontains '0') -or -not ($valid -ccontains '1')){throw 'Advanced property values are unsupported'};" +
            $"Set-NetAdapterAdvancedProperty -InputObject $p[0] -RegistryValue {target} -NoRestart -Confirm:$false -ErrorAction Stop;" +
            $"'UPDATED='+{keyword}+';VALUE='+{target}";
        return BuildEncodedArguments(script);
    }

    private static string QuotePowerShellLiteral(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("空の値は PowerShell へ渡せません", parameterName);
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static void ValidateBinaryValue(string value, string parameterName)
    {
        if (value is not ("0" or "1"))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "NIC プロパティ値は 0 または 1 に限定されます");
        }
    }

    private static string BuildPowerShellPolicyExpression(string adapterExpression, string quotedKeyword) =>
        $"({quotedKeyword} -ceq '{InterruptModerationKeyword}' -and " +
        $"([int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType} -or " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType})) -or " +
        $"({quotedKeyword} -ceq '{EnergyEfficientEthernetKeyword}' -and [int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.EthernetInterfaceType}) -or " +
        $"(({quotedKeyword} -ceq '{MediaTekLowPowerKeyword}' -or {quotedKeyword} -ceq '{MediaTekUapsdKeyword}') -and " +
        $"[int]{adapterExpression}.InterfaceType -eq {WindowsGamingNetworkProfilePolicy.WifiInterfaceType} -and " +
        $"[string]::Equals([string]{adapterExpression}.DriverProvider,'{WindowsGamingNetworkProfilePolicy.MediaTekDriverProvider}',[StringComparison]::Ordinal) -and " +
        $"[string]{adapterExpression}.PnPDeviceID -like '{WindowsGamingNetworkProfilePolicy.MediaTekPnpPrefix}*')";

    private static string BuildEncodedArguments(string script) =>
        $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}";
}
