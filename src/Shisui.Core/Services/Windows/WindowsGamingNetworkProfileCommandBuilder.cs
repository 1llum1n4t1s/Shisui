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

        if (expectedAdapterGuid == Guid.Empty)
        {
            throw new ArgumentException("NIC GUID が空です", nameof(expectedAdapterGuid));
        }

        ValidateKnownValue(registryKeyword, expectedCurrentValue, nameof(expectedCurrentValue));
        ValidateKnownValue(registryKeyword, targetValue, nameof(targetValue));
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
            "if(@($valid | Select-Object -Unique).Count -ne $valid.Count){throw 'Advanced property values are duplicated'};" +
            $"if({supportedValueChecks}){{throw 'Advanced property values are unsupported'}};" +
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
        $"(({quotedKeyword} -ceq '{RealtekGreenEthernetKeyword}' -or {quotedKeyword} -ceq '{RealtekGigaLiteKeyword}' -or {quotedKeyword} -ceq '{RealtekPowerSavingModeKeyword}') -and " +
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
