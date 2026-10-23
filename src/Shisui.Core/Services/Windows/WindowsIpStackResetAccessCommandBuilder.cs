namespace Shisui.Core.Services.Windows;

/// <summary>IP スタック初期化に必要な NSI キーの最小権限補完を組み立てる。</summary>
public static class WindowsIpStackResetAccessCommandBuilder
{
    public static string BuildArguments(bool ipv6)
    {
        var keyPath = ipv6
            ? @"SYSTEM\CurrentControlSet\Control\Nsi\{eb004a01-9b1a-11d4-9123-0050047759bc}\26"
            : @"SYSTEM\CurrentControlSet\Control\Nsi\{eb004a00-9b1a-11d4-9123-0050047759bc}\26";

        return WindowsPowerShellArgumentEncoder.Encode($$"""
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $baseKey = $null
            $probeKey = $null
            $readWriteKey = $null
            $writeDaclKey = $null
            $identity = $null
            $aclWritten = $false
            try {
                $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
                $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
                if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
                    throw '管理者として実行されていないため、NSI 権限補完を保留しました。'
                }
                $path = '{{keyPath}}'
                $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
                    [Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
                $checkMode = [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree
                $requiredRights = [System.Security.AccessControl.RegistryRights]0x3001f
                try {
                    $probeKey = $baseKey.OpenSubKey($path, $checkMode, $requiredRights)
                    if ($null -eq $probeKey) {
                        [Console]::WriteLine('変更なし: 対象の NSI キーが存在しないため、権限補完を省略しました。')
                        exit 0
                    }
                    [Console]::WriteLine('変更なし: NSI キーには初期化に必要な権限があります。')
                    exit 0
                } catch [System.UnauthorizedAccessException] {
                    # Delete 不足かどうかを、既存の読み書き権限だけで確認する。
                } catch [System.Security.SecurityException] {
                    # 下記の制約を満たせない ACL は変更しない。
                }
                $readWriteKey = $baseKey.OpenSubKey($path, $checkMode,
                    [System.Security.AccessControl.RegistryRights]0x2001f)
                if ($null -eq $readWriteKey) {
                    throw '対象の NSI キーが確認中に消失したため、権限補完を保留しました。'
                }
                $sections = [System.Security.AccessControl.AccessControlSections]::Access -bor
                    [System.Security.AccessControl.AccessControlSections]::Owner -bor
                    [System.Security.AccessControl.AccessControlSections]::Group
                $security = $readWriteKey.GetAccessControl($sections)
                $before = $security.GetSecurityDescriptorSddlForm($sections)
                $descriptor = [System.Security.AccessControl.RawSecurityDescriptor]::new($before)
                $admins = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
                if ($null -eq $descriptor.Owner -or $descriptor.Owner.Value -ne $admins.Value -or
                    -not $security.AreAccessRulesProtected -or -not $security.AreAccessRulesCanonical -or
                    $null -eq $descriptor.DiscretionaryAcl) {
                    throw '所有者または DACL が対応条件と異なるため、NSI 権限補完を保留しました。'
                }
                $acl = $descriptor.DiscretionaryAcl
                $insertIndex = $acl.Count
                for ($index = 0; $index -lt $acl.Count; $index++) {
                    $ace = $acl[$index]
                    if ($ace -isnot [System.Security.AccessControl.CommonAce] -or $ace.IsCallback -or
                        $ace.AceQualifier -ne [System.Security.AccessControl.AceQualifier]::AccessAllowed -or
                        $ace.SecurityIdentifier.Value -eq 'S-1-3-4') {
                        throw '特殊な ACE を含むため、NSI 権限補完を保留しました。'
                    }
                    if (([int]$ace.AceFlags -band [int][System.Security.AccessControl.AceFlags]::Inherited) -ne 0 -and
                        $insertIndex -eq $acl.Count) {
                        $insertIndex = $index
                    }
                }
                # 所有権取得や特権変更をせず、現在の管理者権限で WRITE_DAC を要求する。
                $writeDaclKey = $baseKey.OpenSubKey($path, $checkMode,
                    [System.Security.AccessControl.RegistryRights]0x60000)
                if ($null -eq $writeDaclKey) {
                    throw '対象の NSI キーが確認中に消失したため、権限補完を保留しました。'
                }
                $deleteAce = [System.Security.AccessControl.CommonAce]::new(
                    [System.Security.AccessControl.AceFlags]::None,
                    [System.Security.AccessControl.AceQualifier]::AccessAllowed, 0x10000, $admins, $false, $null)
                $acl.InsertAce($insertIndex, $deleteAce)
                $expected = $descriptor.GetSddlForm($sections)
                $updated = [System.Security.AccessControl.RegistrySecurity]::new()
                $updated.SetSecurityDescriptorSddlForm($expected,
                    [System.Security.AccessControl.AccessControlSections]::Access)
                if (-not $updated.AreAccessRulesCanonical -or -not $updated.AreAccessRulesProtected) {
                    throw '補完後の DACL が対応条件と異なるため、NSI 権限補完を保留しました。'
                }
                if ($writeDaclKey.GetAccessControl($sections).GetSecurityDescriptorSddlForm($sections) -cne $before) {
                    throw '確認中に NSI キーの権限が変化したため、権限補完を保留しました。'
                }
                # 既存 ACE の順序・内容を保ち、Administrators の非継承 Delete だけを追加する。
                $writeDaclKey.SetAccessControl($updated)
                $aclWritten = $true
                $actual = $readWriteKey.GetAccessControl($sections)
                $actualSddl = $actual.GetSecurityDescriptorSddlForm($sections)
                $actualDescriptor = [System.Security.AccessControl.RawSecurityDescriptor]::new($actualSddl)
                $expectedDescriptor = [System.Security.AccessControl.RawSecurityDescriptor]::new($expected)
                # 保存時に Windows が付ける AI だけを除外し、権限・所有者・保護などは厳密に比較する。
                foreach ($comparable in @($actualDescriptor, $expectedDescriptor)) {
                    $comparable.SetFlags([System.Security.AccessControl.ControlFlags](
                        [int]$comparable.ControlFlags -band (-bnot 0x400)))
                }
                if ($actualDescriptor.GetSddlForm($sections) -cne $expectedDescriptor.GetSddlForm($sections) -or
                    -not $actual.AreAccessRulesProtected -or -not $actual.AreAccessRulesCanonical) {
                    throw ('NSI 権限補完後の所有者・グループ・DACL の検証に失敗しました。期待値=' + $expected + ' 実際=' + $actualSddl)
                }
                $probeKey = $baseKey.OpenSubKey($path, $checkMode, $requiredRights)
                if ($null -eq $probeKey) {
                    throw 'NSI 権限補完後のキーアクセス検証に失敗しました。'
                }
                [Console]::WriteLine('補完済み: NSI キーの Administrators に非継承の Delete 権限を追加しました。')
                exit 0
            } catch {
                $status = if ($aclWritten) { '補完操作済み・検証失敗' } else { '保留' }
                [Console]::Error.WriteLine($status + ': NSI 権限補完を完了できませんでした: ' + $_.Exception.Message)
                exit 1
            } finally {
                if ($null -ne $probeKey) { $probeKey.Dispose() }
                if ($null -ne $writeDaclKey) { $writeDaclKey.Dispose() }
                if ($null -ne $readWriteKey) { $readWriteKey.Dispose() }
                if ($null -ne $baseKey) { $baseKey.Dispose() }
                if ($null -ne $identity) { $identity.Dispose() }
            }
            """);
    }
}
