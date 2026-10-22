namespace Shisui.Core.Services.Windows;

/// <summary>固定設定を変更せず、一時的な通信状態だけを更新する条件付きコマンド。</summary>
public static class WindowsConditionalNetworkResetCommandBuilder
{
    public static string BuildClearActiveIpsecSaArguments() => WindowsPowerShellArgumentEncoder.Encode("""
        $ErrorActionPreference='Stop';
        $OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);
        $stage='事前確認';$quickRemoved=0;$mainRemoved=0;
        try {
            $modules=@(Get-Module -ListAvailable -Name NetSecurity -ErrorAction Stop);
            if($modules.Count -eq 0){'IPsec SA の破棄は未実施: NetSecurity モジュールが未導入のため省略しました。';exit 0};
            Import-Module NetSecurity -ErrorAction Stop;
            foreach($name in @('Get-NetIPsecMainModeSA','Get-NetIPsecQuickModeSA','Remove-NetIPsecMainModeSA','Remove-NetIPsecQuickModeSA')) {
                try {$null=Get-Command -Name ('NetSecurity\'+$name) -ErrorAction Stop}
                catch [System.Management.Automation.CommandNotFoundException] {
                    ('IPsec SA の破棄は未実施: '+$name+' が非対応のため省略しました。');exit 0
                }
            };
            $quick=@(NetSecurity\Get-NetIPsecQuickModeSA -ErrorAction Stop);
            $main=@(NetSecurity\Get-NetIPsecMainModeSA -ErrorAction Stop);
            if($quick.Count -eq 0 -and $main.Count -eq 0){'IPsec SA の破棄は未実施: 確立済み SA が0件のため省略しました。';exit 0};
            ('IPsec SA を取得しました: QuickMode '+$quick.Count+'件、MainMode '+$main.Count+'件。');
            $stage='QuickMode SA の削除';
            foreach($sa in $quick){NetSecurity\Remove-NetIPsecQuickModeSA -InputObject $sa -Confirm:$false -ErrorAction Stop;$quickRemoved++};
            $stage='MainMode SA の削除';
            foreach($sa in $main){NetSecurity\Remove-NetIPsecMainModeSA -InputObject $sa -Confirm:$false -ErrorAction Stop;$mainRemoved++};
            ('取得した IPsec SA の削除要求が完了しました: QuickMode '+$quickRemoved+'件、MainMode '+$mainRemoved+'件。必要な SA は通信時に再確立されます。固定ポリシーは変更していません。');
            exit 0
        } catch {
            [Console]::Error.WriteLine('IPsec SA の'+$stage+'に失敗しました。完了した削除要求: QuickMode '+$quickRemoved+'件、MainMode '+$mainRemoved+'件。失敗した要求の反映は未確認です: '+$_.Exception.Message);
            exit 1
        }
        """);

    public static string BuildReregisterWinsArguments() => WindowsPowerShellArgumentEncoder.Encode("""
        $ErrorActionPreference='Stop';
        $OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);
        try {
            $adapters=@(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = TRUE' -ErrorAction Stop);
            $hasWins=$false;
            foreach($adapter in $adapters) {
                $netbios=$adapter.PSObject.Properties['TcpipNetbiosOptions'];
                if($null -eq $netbios -or $null -eq $netbios.Value -or [string]$netbios.Value -notin @('0','1','2')){throw 'NetBIOS の有効状態を確認できませんでした。'};
                if([int]$netbios.Value -eq 2){continue};
                foreach($name in @('WINSPrimaryServer','WINSSecondaryServer')) {
                    $property=$adapter.PSObject.Properties[$name];
                    if($null -eq $property){throw ('WINS 構成のプロパティがありません: '+$name)};
                    $server=[string]$property.Value;
                    if([string]::IsNullOrWhiteSpace($server) -or $server -eq '0.0.0.0'){continue};
                    $address=$null;
                    if(-not [Net.IPAddress]::TryParse($server,[ref]$address) -or $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $server -eq '255.255.255.255'){throw ('WINS サーバー構成を確認できませんでした: '+$name)};
                    $hasWins=$true
                }
            };
            if(-not $hasWins){'WINS 名前の再登録は未実施: IP が有効で NetBIOS が無効でないアダプターに WINS サーバー構成がないため省略しました。';exit 0};
            & ([IO.Path]::Combine([Environment]::SystemDirectory,'nbtstat.exe')) -RR;
            $code=$LASTEXITCODE;
            if($code -ne 0){[Console]::Error.WriteLine('WINS 名前の再登録が失敗しました。nbtstat 終了コード: '+$code);exit $code};
            'WINS 名前の解放・再登録コマンドが完了しました。WINS サーバー設定は変更していません。';
            exit 0
        } catch {
            [Console]::Error.WriteLine('WINS 構成の確認または名前の再登録に失敗しました: '+$_.Exception.Message);
            exit 1
        }
        """);
}
