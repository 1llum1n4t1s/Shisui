# 配布する最終bytesだけを検証する。アプリやインストーラは起動しない。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactsDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$SourceGitSha,
    [Parameter(Mandatory)][string]$SignerName,
    [Parameter(Mandatory)][string]$DotnetSdkVersion,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Runtime -ne 'win-x64' -or $SourceGitSha -notmatch '^[0-9a-f]{40}$' -or $DotnetSdkVersion -notmatch '^10\.') {
    throw '配布入力は win-x64 / .NET 10 / 完全な source SHA に固定してください。'
}
$root = [IO.Path]::GetFullPath($WorkDirectory)
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
if (-not $artifacts.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '成果物が作業root外です。' }
for ($cursor = $artifacts; $cursor; $cursor = Split-Path -Parent $cursor) {
    if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '成果物の経路に再解析ポイントがあります。' }
}
$verificationDirectory = Join-Path $root ('artifact-verification-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationDirectory | Out-Null

function Assert-SignedFile([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -cne $SignerName) {
        throw "配布物の署名が不一致です: $Path"
    }
}
function Read-MsiRows($Database, [string]$Sql, [int]$Columns) {
    $view = $null
    try {
        $view = $Database.OpenView($Sql)
        $view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            try {
                $values = @(for ($index = 1; $index -le $Columns; $index++) { $record.StringData($index) })
                ,$values
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally {
        if ($null -ne $view) { $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    }
}
function Get-Identity([IO.FileInfo]$File) {
    [ordered]@{ Path = $File.FullName; FileName = $File.Name; Size = $File.Length; SHA256 = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash }
}

$packages = @(Get-ChildItem -LiteralPath $artifacts -Filter '*.nupkg' -File)
$msis = @(Get-ChildItem -LiteralPath $artifacts -Filter '*.msi' -File)
if ($packages.Count -ne 1 -or $msis.Count -ne 1) { throw 'win-x64 の完全NUPKGとMSIが各1個必要です。' }
$package = $packages[0]
$zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    # 展開対象を固定し、重複・zip slip・symlinkを読み取り前に拒否する。
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.StartsWith('/') -or $name -match '(^|/)\.\.(/|$)|:' -or -not $names.Add($name) -or
            (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw "安全でないpackage entryです: $name" }
    }
    $nuspecs = @($zip.Entries | Where-Object { $_.FullName -match '^[^/\\]+\.nuspec$' })
    if ($nuspecs.Count -ne 1) { throw 'package nuspecを一意に確認できません。' }
    $reader = [IO.StreamReader]::new($nuspecs[0].Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    foreach ($expected in @{ id = 'Shisui'; version = $Version; rid = $Runtime; mainExe = 'Shisui.UI.exe'; os = 'win'; machineArchitecture = 'x64'; channel = 'win' }.GetEnumerator()) {
        $node = $metadata.SelectSingleNode("*[local-name()='$($expected.Key)']")
        if ($null -eq $node -or $node.InnerText -cne $expected.Value) { throw "nuspec $($expected.Key) が不一致です。" }
    }
    $executables = @($zip.Entries | Where-Object { $_.FullName -ceq 'lib/app/Shisui.UI.exe' })
    if ($executables.Count -ne 1) { throw 'packageにlib/app/Shisui.UI.exeが必要です。' }
    $exePath = Join-Path $verificationDirectory 'Shisui.UI.exe'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($executables[0], $exePath, $false)
} finally { $zip.Dispose() }
Assert-SignedFile $exePath
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
if ($info.ProductVersion -cne "$Version+$SourceGitSha" -or
    $info.FileVersion -ne "$Version.0") { throw "package内EXEの版/source SHAが不一致です: $($info.ProductVersion) / $($info.FileVersion)" }
$stream = [IO.File]::OpenRead($exePath)
$binary = [IO.BinaryReader]::new($stream)
try {
    if ($binary.ReadUInt16() -ne 0x5A4D) { throw 'EXEのMZ headerが不正です。' }
    $stream.Position = 0x3C; $peOffset = $binary.ReadInt32()
    $stream.Position = $peOffset
    if ($binary.ReadUInt32() -ne 0x4550 -or $binary.ReadUInt16() -ne 0x8664) { throw 'EXEはAMD64 PEではありません。' }
    $stream.Position = $peOffset + 24
    if ($binary.ReadUInt16() -ne 0x20B) { throw 'EXEはPE32+ではありません。' }
    # PE32+のCLR directoryが空であることも確認し、管理EXE混入を拒否する。
    $stream.Position = $peOffset + 24 + 112 + 14 * 8
    if ($binary.ReadUInt64() -ne 0) { throw 'Native AOT配布物にCLR headerがあります。' }
} finally { $binary.Dispose() }

$msi = $msis[0]
Assert-SignedFile $msi.FullName
$installer = $database = $summary = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($msi.FullName, 0)
    $properties = @{}
    foreach ($row in (Read-MsiRows $database 'SELECT `Property`, `Value` FROM `Property`' 2)) { $properties[$row[0]] = $row[1] }
    $summary = $database.SummaryInformation(0)
    if ($properties['ProductVersion'] -ne "$Version.0" -or $summary.Property(7) -notmatch '^x64(;|$)') { throw 'MSIの版/platformが不一致です。' }
    # 固定vpkのPerMachine配布物（1.0.29実物）と同じ単一scopeを要求する。
    if ($properties['ALLUSERS'] -ne '1' -or $properties.ContainsKey('MSIINSTALLPERUSER')) { throw 'MSIは単一scopeのPerMachineではありません。' }
    $directories = @{}
    foreach ($row in (Read-MsiRows $database 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`' 3)) { $directories[$row[0]] = $row }
    if (-not $directories.ContainsKey('INSTALLFOLDER') -or -not $directories.ContainsKey('ProgramFiles64Folder') -or
        $directories['INSTALLFOLDER'][1] -ne 'ProgramFiles64Folder' -or $directories['INSTALLFOLDER'][2] -ne 'Shisui' -or
        $directories['ProgramFiles64Folder'][1] -ne 'TARGETDIR' -or $directories['ProgramFiles64Folder'][2] -ne '.') {
        throw 'MSIのProgramFiles64Folder/INSTALLFOLDER構造が不一致です。'
    }
} finally {
    foreach ($com in @($summary, $database, $installer)) { if ($null -ne $com) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) } }
}
$manifestPath = Join-Path $artifacts 'releases.win.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$assets = @($manifest.Assets)
if ($assets.Count -ne 1) { throw 'manifestは今回の完全NUPKG 1個を参照する必要があります。' }
$asset = $assets[0]
$packageIdentity = Get-Identity $package
if ($asset.FileName -cne $package.Name -or $asset.PackageId -cne 'Shisui' -or $asset.Version -cne $Version -or
    $asset.Type -cne 'Full' -or $asset.Size -ne $package.Length -or $asset.SHA256 -ne $packageIdentity.SHA256) { throw 'manifestと最終NUPKGのidentity/hashが不一致です。' }
if ($asset.PSObject.Properties['SHA1'] -and $asset.SHA1 -ne (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA1).Hash) { throw 'manifestのSHA1が不一致です。' }
$record = [ordered]@{
    Version = $Version; SourceGitSha = $SourceGitSha; Runtime = $Runtime; Configuration = 'Release'
    TargetFramework = 'net10.0'; DotnetSdkVersion = $DotnetSdkVersion; PublishAot = $true; SelfContained = $true
    ExeProductVersion = $info.ProductVersion; ExeFileVersion = $info.FileVersion; PeMachine = 'AMD64'; ClrHeader = $false
    Signer = $SignerName; AuthenticodeStatus = 'Valid'; MsiProductVersion = $properties['ProductVersion']; MsiPlatform = 'x64'
    MsiAllUsers = $properties['ALLUSERS']; MsiInstallPerUser = $properties['MSIINSTALLPERUSER']; InstallDirectory = 'ProgramFiles64Folder\Shisui'
    Package = $packageIdentity; Msi = (Get-Identity $msi); Manifest = (Get-Identity (Get-Item -LiteralPath $manifestPath))
}
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'verified-release-artifacts.json') -Encoding utf8
Write-Host "  ✅ 最終配布物の版・source・RID・署名・MSI・manifest/hash一致: $Version"
