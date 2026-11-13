# release-local.ps1 — 署名付き Velopack リリース (VStoVSC テンプレートから横展開)
#
# 署名は Sigilune (Certum SimplySign のクラウド鍵を signtool /dlib で使う CLI) で行う。
# 通常のリリースは GitHub Actions の .github/workflows/release.yml (手動起動・Environment "release") が
# 本スクリプトを実行する。ローカル実行も同じ手順で動き、検証や再実行に使う。
#
# 前提:
#   - Sigilune を使えること (scripts/Sigilune.Common.ps1 参照)。
#     ローカルは `sigilune setup` で Certum アカウントを登録済み、CI は SIGILUNE_EMAIL / SIGILUNE_TOTP_URI を渡す
#   - Directory.Build.props の <Version> がリリースしたいバージョンになっていること (/vava 済み)
#   - 環境変数 CLOUDFLARE_API_TOKEN (CI)、なければ D:\Dev\Secret\secrets.json の cloudflare.api_token (ローカル)
#
# 使い方:
#   pwsh scripts/release-local.ps1                # フルリリース (build + sign + upload)
#   pwsh scripts/release-local.ps1 -SkipUpload    # ビルド + 署名のみ (アップロードしない動作確認用)

[CmdletBinding()]
param(
    [switch]$SkipUpload,
    [string[]]$Runtimes = @('win-x64'),
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) "Codex\Shisui\release-$([Guid]::NewGuid().ToString('N'))"),
    [switch]$UseEnvironmentalAotTools,
    [string]$SourceGitSha
)


# 検証済みの作業対象だけを標準APIで削除する。
function Remove-LocalWorkItem {
    param([string[]]$LiteralPath, [string]$AllowedRoot, [switch]$IgnoreMissing, [switch]$AllowLeafJunction)
    if ([string]::IsNullOrWhiteSpace($AllowedRoot) -or -not [IO.Path]::IsPathRooted($AllowedRoot)) { throw 'Cleanup root must be absolute.' }
    $boundary = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($target in $LiteralPath) {
        if ([string]::IsNullOrWhiteSpace($target) -or -not [IO.Path]::IsPathRooted($target)) { throw 'Cleanup target must be absolute.' }
        $resolved = [IO.Path]::GetFullPath($target)
        if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target is outside the allowed root.' }
        $ancestor = $resolved
        while ($ancestor) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and -not ($AllowLeafJunction -and $ancestor -eq $resolved -and $item.LinkType -eq 'Junction')) { throw 'Cleanup path contains a reparse point.' }
            }
            $ancestor = Split-Path $ancestor -Parent
        }
        if (-not (Test-Path -LiteralPath $resolved)) { if ($IgnoreMissing) { continue }; throw "Cleanup target is missing: $resolved" }
        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        $leafLink = [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
        if ($item.PSIsContainer -and -not $leafLink) {
            if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force -ErrorAction Stop | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Cleanup directory contains a reparse point.' }
        }
        Remove-Item -LiteralPath $resolved -Recurse:($item.PSIsContainer -and -not $leafLink) -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $resolved) { throw "Cleanup target remains: $resolved" }
    }
}

# R2 への書込みは同梱の PowerShell/.NET 実装で行う。
. (Join-Path $PSScriptRoot 'R2Distribution.Common.ps1')

function Send-ReleaseR2Object {
    param([Parameter(Mandatory)][System.IO.FileInfo]$File, [switch]$PassThru, [string]$ExpectedETag)
    # バージョン付き成果物は不変、固定 URL とメタデータは毎回再検証する。
    $immutable = $File.Extension -eq '.nupkg' -or $File.Name -match '\d+\.\d+\.\d+'
    $cacheControl = if ($immutable) { 'public, max-age=31536000, immutable' } else { 'no-cache, must-revalidate' }
    $contentType = switch ($File.Extension.ToLowerInvariant()) {
        '.json' { 'application/json'; break }
        '.exe' { 'application/vnd.microsoft.portable-executable'; break }
        '.zip' { 'application/zip'; break }
        default { if ($File.Name -like 'RELEASES*') { 'text/plain; charset=utf-8' } else { 'application/octet-stream' } }
    }
    $conditions = @{}
    if ($PSBoundParameters.ContainsKey('ExpectedETag')) { $conditions.ExpectedETag = $ExpectedETag }
    $result = Publish-R2Object @conditions -AccountId $AccountId -Bucket $Bucket -FilePath $File.FullName `
        -ApiToken $env:CLOUDFLARE_API_TOKEN -ContentType $contentType -CacheControl $cacheControl -Immutable:$immutable
    if ($PassThru) { return $result }
}

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- 定数 ----
# リリースの再現性と set-msi-program-files-location.ps1 の前提 (1.2.0 の MSI レイアウト) を保つため、
# vpk はリポジトリ内で明示的に固定する (Lumin4ti と同方式)。
# 更新時は公式 NuGet の安定版を確認し、-SkipUpload で署名成果物を検証してから変更する。
$VpkVersion = '1.2.161'
Write-Host "vpk 固定バージョン: $VpkVersion"
$Bucket = 'shisui-updates'
$BaseUrl = 'https://shisui.kagayoi.com'
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'D:\Dev\Secret\secrets.json'
# 署名者の CN。証明書の年次更新で thumbprint が変わっても同じ値で照合できる。
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'

# Shisui は win-x64 のみ配信 (ARM64 配信なし)。macOS は Apple 署名/notarization が別途必要なため対象外。
$RuntimeMatrix = @{
    'win-x64' = @{ Channel = 'win' }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot
$TemporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'Codex\Shisui'))
$WorkDir = [IO.Path]::GetFullPath($WorkDirectory)
if (-not $WorkDir.StartsWith($TemporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '作業ディレクトリはユーザー Temp の Codex\Shisui 配下に指定してください。'
}
$forbiddenRoots = @($RepoRoot, (Join-Path $env:USERPROFILE '.codex'))
if ($env:CODEX_HOME) { $forbiddenRoots += [IO.Path]::GetFullPath($env:CODEX_HOME) }
foreach ($root in $forbiddenRoots) {
    if ($WorkDir.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'リポジトリまたは Codex ユーザーディレクトリを作業場所にはできません。'
    }
}
for ($cursor = $WorkDir; $cursor; $cursor = Split-Path -Parent $cursor) {
    if ((Split-Path -Leaf $cursor) -ieq 'artifacts' -or
        ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint))) {
        throw '禁止された名前または再解析ポイントを含む作業ディレクトリです。'
    }
}
$ArtifactsDir = Join-Path $WorkDir 'packages'
$BuildOutputDir = Join-Path $WorkDir 'build'
$RollbackDir = Join-Path $WorkDir 'rollback-previous'
$taskKey = [IO.Path]::GetRelativePath($TemporaryRoot, $WorkDir).Split([IO.Path]::DirectorySeparatorChar)[0]

function Invoke-Native {
    param([string]$Description, [scriptblock]$Block)
    & $Block
    if ($LASTEXITCODE -ne 0) { throw "$Description が失敗しました (exit $LASTEXITCODE)" }
}

# ---- 0. プリフライト ----
Write-Host '== プリフライト ==' -ForegroundColor Cyan

# Git Bash (MSYS) 経由で起動すると括弧入り環境変数が落ちて vswhere.exe 解決等が
# 壊れることがあるため補完する (非 AOT でも実害なしの保険)
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)' }
$vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ($env:PATH -notlike "*$vsInstallerDir*") { $env:PATH = "$env:PATH;$vsInstallerDir" }

# vpk (dotnet tool) は .NET 9 ランタイム要求だがローカルは 10 のみ → ロールフォワード
$env:DOTNET_ROLL_FORWARD = 'Major'

# XPath で取得 (member enumeration は Version を持たない PropertyGroup 混在時に StrictMode で throw する)
$versionNode = ([xml](Get-Content 'Directory.Build.props' -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
if (-not $version) { throw 'Directory.Build.props から <Version> を取得できませんでした' }
Write-Host "バージョン: $version"
if (-not $SourceGitSha) {
    $SourceGitSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'source SHAを取得できません。' }
}
if ($SourceGitSha -notmatch '^[0-9a-f]{40}$') { throw 'SourceGitShaは完全なcommit SHAを指定してください。' }
$currentSha = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $currentSha -cne $SourceGitSha) { throw '固定source SHAとHEADが一致しません。' }
$sourceChanges = @(& git status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -gt 0) { throw '固定sourceの配布にはクリーンなworking treeが必要です。' }
$dotnetSdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $dotnetSdkVersion -notmatch '^11\.') { throw '.NET 11 SDKが必要です。' }

# 署名の準備 (Sigilune の場所とクラウドへのログインを、ビルド前に確認して早く落とす)。
. (Join-Path $PSScriptRoot 'Sigilune.Common.ps1')
$Sigilune = Resolve-SigiluneExe
Connect-Sigilune -SigiluneExe $Sigilune
# Velopack はファイルごとに別プロセスで署名テンプレートを実行する。Sigilune はログインを共有し、
# 署名後に有効性・署名者・タイムスタンプを検証して失敗なら非0で終わる。署名済みファイルは vpk 自身が飛ばす。
$SignTemplate = "`"$Sigilune`" sign {{file}}"

# vpk を固定バージョンで用意
$vpkInstalled = (dotnet tool list --global | Select-String -SimpleMatch 'vpk') -match [regex]::Escape($VpkVersion)
if (-not $vpkInstalled) {
    Write-Host "vpk $VpkVersion をインストールします..."
    dotnet tool uninstall --global vpk 2>$null | Out-Null
    Invoke-Native 'vpk のインストール' { dotnet tool install --global vpk --version $VpkVersion }
}

# Cloudflare トークン (アップロード時のみ必要)
if (-not $SkipUpload) {
    # CI では Environment "release" の Secret が環境変数で渡る。ローカルは secrets.json から必要なキーだけ読む。
    if (-not $env:CLOUDFLARE_API_TOKEN) {
        $secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
        if (-not $secrets.cloudflare.api_token) { throw "secrets.json に cloudflare.api_token が見つかりません" }
        $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
    }
    $env:CLOUDFLARE_ACCOUNT_ID = $AccountId
}

if (Test-Path -LiteralPath $WorkDir) {
    Remove-LocalWorkItem -LiteralPath $WorkDir -AllowedRoot $TemporaryRoot
}
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
$toolTemp = Join-Path $WorkDir 'tool-temp'
New-Item -ItemType Directory -Path $toolTemp -Force | Out-Null
$env:TEMP = $toolTemp
$env:TMP = $toolTemp
[string[]]$aotToolArgs = if ($UseEnvironmentalAotTools) { @('-p:IlcUseEnvironmentalTools=true') } else { @() }

# ---- 1. ビルド + 署名付きパッケージング (RID ごと) ----
foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    if (-not $config) { throw "未知の runtime: $runtime" }
    $publishDir = Join-Path $WorkDir "publish-$runtime"

    # restore と publish のパラメータを完全一致させる (self-contained を restore にも渡さないと
    # runtime packs が assets.json に入らず NETSDK1112 が出る)。
    Write-Host "== restore: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet restore ($runtime)" {
        dotnet restore src/Shisui.UI/Shisui.UI.csproj -r $runtime `
            -p:SelfContained=true -p:OS=Windows_NT -p:Configuration=Release `
            -p:UseArtifactsOutput=true "-p:ArtifactsPath=$BuildOutputDir"
    }

    Write-Host "== publish: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet publish ($runtime)" {
        dotnet publish src/Shisui.UI/Shisui.UI.csproj -c Release -r $runtime `
            --self-contained true --no-restore -p:OS=Windows_NT -o $publishDir `
            -p:PublishAot=true -p:TargetFramework=net11.0 "-p:SourceRevisionId=$SourceGitSha" `
            "-p:InformationalVersion=$version+$SourceGitSha" -p:IncludeSourceRevisionInInformationalVersion=false `
            -p:UseArtifactsOutput=true "-p:ArtifactsPath=$BuildOutputDir" @aotToolArgs
    }

    if (-not (Test-Path (Join-Path $publishDir 'Shisui.UI.exe'))) {
        throw "Shisui.UI.exe が publish 出力にありません ($runtime)"
    }

    Write-Host "== vpk pack + 署名: $runtime ==" -ForegroundColor Cyan
    # StartMenu は packAuthors 名のサブフォルダを作るため、直下登録の StartMenuRoot を使う。
    Invoke-Native "vpk pack ($runtime)" {
        vpk pack `
            --packId Shisui `
            --runtime $runtime `
            --packVersion $version `
            --packTitle 'Shisui' `
            --packAuthors 'Kagayoi' `
            --mainExe Shisui.UI.exe `
            --icon (Join-Path 'src' 'Shisui.UI' 'icon' 'app.ico') `
            --packDir $publishDir `
            --outputDir $ArtifactsDir `
            --channel $config.Channel `
            --shortcuts 'StartMenuRoot,Desktop' `
            --msi `
            --instLocation PerMachine `
            --signTemplate $SignTemplate
    }
}

# Velopack 1.2.0 の生成MSIは PerMachine 指定でも INSTALLFOLDER が TARGETDIR 直下になり、
# Windows Installer が C:\Shisui と解決する。Directory表を公式の64-bit Program Files構造へ直し、
# 変更によって無効になったMSI本体の署名を上書きして再署名する。
# 補正後の MSI は「署名あり・ダイジェスト不一致」の状態で、Sigilune は既存署名のあるファイルを
# 既定で拒否するため --replace で置き換える。署名者・タイムスタンプは Sigilune と直後の検証で確認する。
foreach ($msi in Get-ChildItem $ArtifactsDir -Filter '*.msi' -File) {
    & (Join-Path $PSScriptRoot 'set-msi-program-files-location.ps1') -MsiPath $msi.FullName
    Invoke-Native "Program Files固定MSIの再署名 ($($msi.Name))" {
        & $Sigilune sign --replace $msi.FullName
    }
}

# 署名検証 (MSIを含むインストーラが正しく署名されているかリリース前に確認)
Write-Host '== 署名検証 ==' -ForegroundColor Cyan
foreach ($signedArtifact in Get-ChildItem $ArtifactsDir -File | Where-Object { $_.Extension -in '.exe', '.msi' }) {
    $sig = Get-AuthenticodeSignature $signedArtifact.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "CN=$CertSubjectName*") {
        throw "署名検証失敗: $($signedArtifact.Name) → $($sig.Status)"
    }
    Write-Host "  ✅ $($signedArtifact.Name): Valid ($($sig.SignerCertificate.Subject -replace ',.*$'))"
}

# 署名・再署名後の同じ実物を検証し、この後はrepack/buildしない。
if ($Runtimes.Count -ne 1 -or $Runtimes[0] -ne 'win-x64') { throw '検証対象の配布RIDはwin-x64だけです。' }
& (Join-Path $PSScriptRoot 'verify-release-artifacts.ps1') -ArtifactsDirectory $ArtifactsDir `
    -WorkDirectory $WorkDir -Version $version -SourceGitSha $SourceGitSha `
    -SignerName $CertSubjectName -DotnetSdkVersion $dotnetSdkVersion -Runtime $Runtimes[0]

if ($SkipUpload) {
    Write-Host "`n✅ -SkipUpload 指定のためここで終了。成果物: $ArtifactsDir" -ForegroundColor Green
    Get-ChildItem $ArtifactsDir | Format-Table Name, @{n='Size(MB)'; e={[math]::Round($_.Length/1MB,1)}}
    return
}

# ---- 1.5 現行配信メタデータの退避 ----
Set-Location $WorkDir
# 新しい版のアップロードやキャッシュ削除が失敗した場合、固定URLのメタデータだけを直前版へ戻せるようにする。
# 直前 manifest が参照する nupkg を含め、既存の配布物は保持する。
New-Item -ItemType Directory -Path $RollbackDir -Force | Out-Null
$artifactFiles = @(Get-ChildItem $ArtifactsDir -File)
# PerUser Setup.exe は今後公開しない。過去に配信されたファイルは保持する。
$publishArtifactFiles = @($artifactFiles | Where-Object { $_.Name -notlike '*-Setup.exe' })
$metadataFiles = @($publishArtifactFiles | Where-Object {
    $_.Name -eq 'RELEASES' -or $_.Name -like 'releases.*.json' -or $_.Name -like 'assets.*.json'
} | Sort-Object @{ Expression = {
    # 現行クライアントが読む releases.*.json を更新メタデータの中でも最後に切り替える。
    if ($_.Name -like 'releases.*.json') { 2 } elseif ($_.Name -eq 'RELEASES') { 1 } else { 0 }
} }, Name)
$payloadFiles = @($publishArtifactFiles | Where-Object { $_.Name -like '*.nupkg' })
$fixedBinaryFiles = @($publishArtifactFiles | Where-Object {
    $_.Name -notlike '*.nupkg' -and $_.Name -ne 'RELEASES' -and
    $_.Name -notlike 'releases.*.json' -and $_.Name -notlike 'assets.*.json'
})
$previousManifestAssets = @{}
foreach ($metadata in $metadataFiles) {
    $url = "$BaseUrl/$($metadata.Name)?_=$([Guid]::NewGuid().ToString('N'))"
    try {
        $response = Invoke-WebRequest -Uri $url -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        $raw = $response.Content
        if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }
        Set-Content -LiteralPath (Join-Path $RollbackDir $metadata.Name) -Value $raw -NoNewline -Encoding utf8

        if ($metadata.Name -like 'releases.*.json') {
            foreach ($asset in ($raw | ConvertFrom-Json).Assets) {
                if ($asset.FileName) { $previousManifestAssets[$asset.FileName] = $true }
            }
        }
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $statusCode = if ($responseProperty -and $responseProperty.Value) {
            [int]$responseProperty.Value.StatusCode
        } else {
            $null
        }
        if ($statusCode -ne 404) {
            throw "現行配信メタデータの退避に失敗しました: $($metadata.Name) — $($_.Exception.Message)"
        }
    }
}
Write-Host "直前版の退避: $((Get-ChildItem $RollbackDir -File).Count) メタデータ / $($previousManifestAssets.Count) nupkg"

function Publish-R2Artifact {
    param([System.IO.FileInfo]$File, [switch]$PassThru)
    Write-Host "  ↑ $($File.Name)"
    Send-ReleaseR2Object -File $File -PassThru:$PassThru
}

# ---- 2. R2 アップロード ----
Write-Host '== R2 アップロード ==' -ForegroundColor Cyan
$uploaded = 0
$publishedMetadata = @{}
# メタデータの公開前に、失敗時の復元にも使う配信 zone を確定する。
$cfHeaders = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }
$zoneName = ([uri]$BaseUrl).Host -replace '^[^.]+\.', ''
$zoneResp = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones?name=$zoneName" -Headers $cfHeaders -TimeoutSec 30
if (-not $zoneResp.success -or @($zoneResp.result).Count -ne 1 -or
    $zoneResp.result[0].account.id -ne $AccountId) {
    throw "Cloudflare zone '$zoneName' と配信アカウントを一意に確認できませんでした"
}
$zoneId = $zoneResp.result[0].id
# 最初のPUTより前に、版付きpayloadの正確なR2 keyを認証済みGETで照合する。
# 404だけを未送信とし、同一bytesは再送せず、不一致・照会不能は停止する。
$existingPayloads = @{}
foreach ($file in $payloadFiles) {
    if ($file.Name -notmatch [regex]::Escape($version)) { throw "payload keyに今回の版がありません: $($file.Name)" }
    $encodedKey = [uri]::EscapeDataString($file.Name)
    $objectUri = "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket/objects/$encodedKey"
    $existingPath = Join-Path $WorkDir ("existing-" + $file.Name)
    try {
        Invoke-WebRequest -Uri $objectUri -Headers $cfHeaders -OutFile $existingPath -TimeoutSec 60
        if ((Get-FileHash -LiteralPath $existingPath -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "同じ版のR2 payloadが異なるbytesで存在します: $($file.Name)" }
        $existingPayloads[$file.Name] = $true
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $statusCode = if ($responseProperty -and $responseProperty.Value) { [int]$responseProperty.Value.StatusCode } else { $null }
        if ($statusCode -ne 404) { throw "R2 payloadの事前照合に失敗しました: $($file.Name)" }
    }
}
try {
    # manifest が先に見えて未アップロードの nupkg を参照しないよう、版付きpayloadを最初に置く。
    foreach ($file in $payloadFiles) {
        if ($existingPayloads.ContainsKey($file.Name)) { Write-Host "  ✅ 送信済みpayloadのSHA256一致: $($file.Name)"; continue }
        Publish-R2Artifact $file
        $uploaded++
    }
    foreach ($file in $fixedBinaryFiles) {
        Publish-R2Artifact $file
        $uploaded++
    }
    # 自動更新クライアントが読む固定URLのメタデータは最後に公開する。
    foreach ($file in $metadataFiles) {
        $published = Publish-R2Artifact $file -PassThru
        $publishedMetadata[$file.Name] = $published.ETag
        $uploaded++
    }
    Write-Host "✅ R2 アップロード完了: $uploaded ファイル"

    # ---- 2.5 Cloudflare エッジキャッシュのパージ ----
    Write-Host '== Cloudflare キャッシュパージ ==' -ForegroundColor Cyan
    $purgeUrls = @($publishArtifactFiles | Where-Object { $_.Name -notlike '*.nupkg' } | ForEach-Object { "$BaseUrl/$($_.Name)" })
    if ($purgeUrls.Count -gt 0) {
        $purgeBody = [PSCustomObject]@{ files = $purgeUrls } | ConvertTo-Json -Compress
        $purgeResp = Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
            -Headers $cfHeaders -ContentType 'application/json' -Body $purgeBody -TimeoutSec 30
        if (-not $purgeResp.success) { throw "Cloudflare キャッシュパージに失敗しました: $($purgeResp.errors | ConvertTo-Json -Compress)" }
        Write-Host "  ✅ パージ要求受理: $($purgeUrls.Count) URL"
    }
} catch {
    $publishError = $_
    # 1件の復元失敗で残りのメタデータ復元と再パージを止めず、元の公開失敗も失わないよう、
    # 復元側の失敗は収集して最後に元のエラーと一緒に報告する。
    $rollbackFailures = [System.Collections.Generic.List[string]]::new()
    $restoredUrls = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($publishedMetadata.Keys)) {
        $backup = Join-Path $RollbackDir $name
        if (Test-Path -LiteralPath $backup) {
            Write-Warning "公開失敗のため直前版へ復元します: $name"
            try {
                Send-ReleaseR2Object -File (Get-Item -LiteralPath $backup) -ExpectedETag $publishedMetadata[$name]
                $restoredUrls.Add("$BaseUrl/$name")
            } catch {
                $rollbackFailures.Add("復元 $name — $($_.Exception.Message)")
            }
        }
    }

    if ($zoneId -and $restoredUrls.Count -gt 0) {
        try {
            $rollbackBody = "{`"files`":$(ConvertTo-Json -InputObject @($restoredUrls) -AsArray -Compress)}"
            $rollbackPurge = Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
                -Headers $cfHeaders -ContentType 'application/json' -Body $rollbackBody -TimeoutSec 30
            if (-not $rollbackPurge.success) {
                $rollbackFailures.Add("復元後のキャッシュパージ — $($rollbackPurge.errors | ConvertTo-Json -Compress)")
            }
        } catch {
            $rollbackFailures.Add("復元後のキャッシュパージ — $($_.Exception.Message)")
        }
    }

    if ($rollbackFailures.Count -gt 0) {
        # 元の失敗に加え、直前版へ戻せていない固定URLがあることを明示して手動復旧を促す。
        throw ("公開に失敗しました: $($publishError.Exception.Message)`n" +
            "さらに直前版への復元で失敗があります。$RollbackDir の退避メタデータで手動復旧してください:`n  " +
            ($rollbackFailures -join "`n  "))
    }
    throw $publishError
}

# 旧ドメイン経由の更新を維持するため、既存の R2 オブジェクトは削除しない。

Write-Host "`n🎉 リリース完了: v$version → $BaseUrl" -ForegroundColor Green
