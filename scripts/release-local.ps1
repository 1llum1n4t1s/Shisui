# release-local.ps1 — ローカル署名付き Velopack リリース (VStoVSC テンプレートから横展開)
#
# SimplySign (Certum クラウド署名) は Desktop 接続 + スマホトークンが必要で
# GitHub Actions からは署名できないため、リリースは本スクリプトでローカル実行する。
#
# 前提:
#   - SimplySign Desktop が接続済み (証明書が CurrentUser\My に見えていること)
#   - Directory.Build.props の <Version> がリリースしたいバージョンになっていること (/vava 済み)
#   - D:\Dev\Secret\secrets.json に cloudflare.api_token があること
#
# 使い方:
#   pwsh scripts/release-local.ps1                # フルリリース (build + sign + upload + cleanup)
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

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- 定数 ----
# リリースの再現性と set-msi-program-files-location.ps1 の前提 (1.2.0 の MSI レイアウト) を保つため、
# vpk はリポジトリ内で明示的に固定する (Lumin4ti と同方式)。
# 更新時は公式 NuGet の安定版を確認し、-SkipUpload で署名成果物を検証してから変更する。
$VpkVersion = '1.2.161'
Write-Host "vpk 固定バージョン: $VpkVersion"
$WranglerVersion = '4.149.0'        # サプライチェーン対策でバージョン固定
$Bucket = 'shisui-updates'
$BaseUrl = 'https://shisui.kagayoi.com'
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'D:\Dev\Secret\secrets.json'
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'
# /n (Subject 名) で選択: 証明書の年次更新で thumbprint が変わっても動く
$SignParams = "/n `"$CertSubjectName`" /fd SHA256 /td SHA256 /tr http://time.certum.pl"
$SignToolPath = Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like '*\x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $SignToolPath) { throw 'Windows SDK x64 signtool.exe が見つかりません' }

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
if ($LASTEXITCODE -ne 0 -or $dotnetSdkVersion -notmatch '^10\.') { throw '.NET 10 SDKが必要です。' }

# SimplySign 接続確認 (証明書が見えなければ署名できないので最初に落とす)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -like "CN=$CertSubjectName*" -and $_.NotAfter -gt (Get-Date) }
if (-not $cert) {
    throw "署名証明書 (CN=$CertSubjectName) が見つかりません。SimplySign Desktop を起動してトークンでログインしてください。"
}
Write-Host "署名証明書: $($cert.Subject) (期限 $($cert.NotAfter.ToString('yyyy-MM-dd')))"

# vpk を固定バージョンで用意
$vpkInstalled = (dotnet tool list --global | Select-String -SimpleMatch 'vpk') -match [regex]::Escape($VpkVersion)
if (-not $vpkInstalled) {
    Write-Host "vpk $VpkVersion をインストールします..."
    dotnet tool uninstall --global vpk 2>$null | Out-Null
    Invoke-Native 'vpk のインストール' { dotnet tool install --global vpk --version $VpkVersion }
}

# Cloudflare トークン (アップロード時のみ必要)
if (-not $SkipUpload) {
    $secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
    if (-not $secrets.cloudflare.api_token) { throw "secrets.json に cloudflare.api_token が見つかりません" }
    $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
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
$env:NPM_CONFIG_CACHE = Join-Path $WorkDir 'npm-cache'
$env:WRANGLER_LOG_PATH = Join-Path $WorkDir 'wrangler-logs'
$env:WRANGLER_SEND_METRICS = 'false'
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
            -p:PublishAot=true -p:TargetFramework=net10.0 "-p:SourceRevisionId=$SourceGitSha" `
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
            --signParams $SignParams
    }
}

# Velopack 1.2.0 の生成MSIは PerMachine 指定でも INSTALLFOLDER が TARGETDIR 直下になり、
# Windows Installer が C:\Shisui と解決する。Directory表を公式の64-bit Program Files構造へ直し、
# 変更によって無効になったMSI本体の署名を上書きして再署名する。
foreach ($msi in Get-ChildItem $ArtifactsDir -Filter '*.msi' -File) {
    & (Join-Path $PSScriptRoot 'set-msi-program-files-location.ps1') -MsiPath $msi.FullName
    Invoke-Native "Program Files固定MSIの再署名 ($($msi.Name))" {
        & $SignToolPath sign /n $CertSubjectName /fd SHA256 /td SHA256 `
            /tr 'http://time.certum.pl' $msi.FullName
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
# 新しい版を公開した直後に検証が失敗した場合、固定URLのメタデータだけを直前版へ戻せるようにする。
# 直前 manifest が参照する nupkg は後段の cleanup でも保持する。
New-Item -ItemType Directory -Path $RollbackDir -Force | Out-Null
$artifactFiles = @(Get-ChildItem $ArtifactsDir -File)
$legacySetupFiles = @($artifactFiles | Where-Object { $_.Name -like '*-Setup.exe' })
# PerUser Setup.exe は今後公開しない。MSIの伝播確認後にR2上の旧Setupも削除する。
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
    param([System.IO.FileInfo]$File)
    Write-Host "  ↑ $($File.Name)"
    Invoke-Native "R2 put ($($File.Name))" {
        pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$($File.Name)" --file $File.FullName --remote
    }
}

# ---- 2. R2 アップロード ----
Write-Host '== R2 アップロード ==' -ForegroundColor Cyan
$uploaded = 0
$publishedMetadata = [System.Collections.Generic.List[string]]::new()
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
        Publish-R2Artifact $file
        $publishedMetadata.Add($file.Name)
        $uploaded++
    }
    Write-Host "✅ R2 アップロード完了: $uploaded ファイル"

    # ---- 2.5 Cloudflare エッジキャッシュのパージ ----
    # 固定名ファイルの実物を先に照合し、古い内容が確認できた URL だけをパージする。
    Write-Host '== Cloudflare キャッシュ確認 ==' -ForegroundColor Cyan
    $probeDir = Join-Path $WorkDir 'cache-probe'
    New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
    $purgeUrls = @(
        foreach ($file in ($publishArtifactFiles | Where-Object { $_.Name -notlike '*.nupkg' })) {
            $url = "$BaseUrl/$($file.Name)"
            $probePath = Join-Path $probeDir $file.Name
            Invoke-WebRequest -Uri "${url}?_=$([Guid]::NewGuid().ToString('N'))" `
                -Headers @{ 'Cache-Control' = 'no-cache' } -OutFile $probePath -TimeoutSec 60
            # query が別キャッシュになる設定でも、クライアントが使う固定 URL を確認する。
            $fixedProbePath = $probePath + '.fixed'
            Invoke-WebRequest -Uri $url -OutFile $fixedProbePath -TimeoutSec 60
            if ((Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-FileHash -LiteralPath $fixedProbePath -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                $url
            }
        }
    )
    if ($purgeUrls.Count -gt 0) {
        $purgeBody = [PSCustomObject]@{ files = $purgeUrls } | ConvertTo-Json -Compress
        $purgeResp = Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
            -Headers $cfHeaders -ContentType 'application/json' -Body $purgeBody -TimeoutSec 30
        if (-not $purgeResp.success) { throw "Cloudflare キャッシュパージに失敗しました: $($purgeResp.errors | ConvertTo-Json -Compress)" }
        Write-Host "  ✅ パージ: $($purgeUrls.Count) URL"
        foreach ($file in ($publishArtifactFiles | Where-Object { "$BaseUrl/$($_.Name)" -in $purgeUrls })) {
            $matched = $false
            for ($attempt = 1; $attempt -le 6; $attempt++) {
                $probePath = Join-Path $probeDir ($file.Name + '.fixed')
                Invoke-WebRequest -Uri "$BaseUrl/$($file.Name)" -OutFile $probePath -TimeoutSec 60
                if ((Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash -eq
                    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                    $matched = $true
                    break
                }
                Start-Sleep -Seconds 3
            }
            if (-not $matched) { throw "固定 URL の配信内容が一致しません: $($file.Name)" }
        }
    } else {
        Write-Host '  ✅ 配信ファイルの SHA256 一致。パージ不要'
    }

# ---- 3. 配信確認 (manifest 完全一致リトライ方式) ----
# 単純な HTTP 200 だと CDN/edge が古い manifest を返している間に cleanup が走り、旧 manifest を
# 取得したクライアントが直後に消える .nupkg を取りに行って 404 する race がある。
# ローカルアップロード済 manifest と完全一致するまでリトライしてから次へ進む。
    Write-Host '== 配信確認 (manifest 伝播待機) ==' -ForegroundColor Cyan
    foreach ($runtime in $Runtimes) {
    $channel = $RuntimeMatrix[$runtime].Channel
    $url = "$BaseUrl/releases.$channel.json"
    $localManifest = Get-Content (Join-Path $ArtifactsDir "releases.$channel.json") -Raw |
        ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress

    $maxAttempts = 18
    $matched = $false
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $resp = Invoke-WebRequest -Uri "${url}?_=$([Guid]::NewGuid().ToString('N'))" `
            -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        # R2 は text 系でない Content-Type で返すことがあり、その場合 .Content は byte[] になる
        $raw = $resp.Content
        if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }
        $remoteManifest = $raw | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress
        if ($localManifest -eq $remoteManifest) {
            Write-Host "  ✅ $url がローカル manifest と一致 (attempt $attempt)"
            $matched = $true
            break
        }
        Write-Host "  ⚠️ remote manifest がまだ古い (attempt $attempt / $maxAttempts)、5 秒待機..."
        Start-Sleep -Seconds 5
    }
        if (-not $matched) {
            throw "remote manifest が $($maxAttempts * 5) 秒以内にローカルと一致しませんでした。race 回避のため cleanup を中止します: $url"
        }
    }

    # 旧PerUser版が安全に移行できるよう、固定URLのMSI本体もサイズ一致まで確認する。
    foreach ($msi in $fixedBinaryFiles | Where-Object { $_.Extension -eq '.msi' }) {
        $msiUrl = "$BaseUrl/$($msi.Name)"
        $response = Invoke-WebRequest -Method Head -Uri "${msiUrl}?_=$([Guid]::NewGuid().ToString('N'))" `
            -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        $remoteLengths = @(
            @($response.Headers.'Content-Length') |
                ForEach-Object { $_ -split ',' } |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )
        if ($remoteLengths.Count -eq 0) {
            throw "remote MSI の Content-Length を取得できません: $msiUrl"
        }
        foreach ($remoteLengthText in $remoteLengths) {
            $remoteLength = 0L
            if (-not [long]::TryParse(
                    $remoteLengthText,
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$remoteLength) -or
                $remoteLength -ne $msi.Length) {
                throw "remote MSI のサイズがローカルと一致しません: $msiUrl (remote=$remoteLengthText local=$($msi.Length))"
            }
        }
        Write-Host "  ✅ $msiUrl の配信サイズ一致"
    }
    # 両方の通常URLを全bodyで照合する。query/headerやHEADは成功の代用にしない。
    Write-Host '== 新旧ドメインの通常URL・全body SHA256照合 ==' -ForegroundColor Cyan
    foreach ($hostUrl in @($BaseUrl, 'https://shisui.nephilim.jp')) {
        $matched = $false
        for ($attempt = 1; $attempt -le 18; $attempt++) {
            $allMatched = $true
            foreach ($file in @($metadataFiles + $payloadFiles + $fixedBinaryFiles)) {
                $probePath = Join-Path $probeDir ("normal-$(([uri]$hostUrl).Host)-$($file.Name)")
                try {
                    Invoke-WebRequest -Uri "$hostUrl/$($file.Name)" -OutFile $probePath -TimeoutSec 60
                    if ((Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash -ne
                        (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { $allMatched = $false }
                } catch { $allMatched = $false }
            }
            if ($allMatched) { $matched = $true; break }
            if ($attempt -lt 18) { Start-Sleep -Seconds 5 }
        }
        if (-not $matched) { throw "通常URLの最終成果物が一致しません。旧版清掃を中止します: $hostUrl" }
        Write-Host "  ✅ $hostUrl manifest/NUPKG/MSIの全body SHA256一致"
    }
} catch {
    $publishError = $_
    $restoredUrls = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $publishedMetadata) {
        $backup = Join-Path $RollbackDir $name
        if (Test-Path $backup) {
            Write-Warning "公開失敗のため直前版へ復元します: $name"
            Invoke-Native "R2 rollback ($name)" {
                pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$name" --file $backup --remote
            }
            $restoredUrls.Add("$BaseUrl/$name")
        }
    }

    if ($zoneId -and $restoredUrls.Count -gt 0) {
        $rollbackBody = "{`"files`":$(ConvertTo-Json -InputObject @($restoredUrls) -AsArray -Compress)}"
        Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
            -Headers $cfHeaders -ContentType 'application/json' -Body $rollbackBody -TimeoutSec 30 | Out-Null
    }
    throw $publishError
}

# ---- 4. 旧バージョン nupkg のクリーンアップ (Aggressive 戦略) ----
# ローカル artifacts の manifest (= 今アップロードしたものと同一) から keep set を作り、
# R2 上の「.nupkg かつ manifest 外」だけを削除する。固定ファイル名 (Setup.exe /
# MSI / Portable.zip / RELEASES* / assets.*.json / releases.*.json) は対象外なので安全。
Write-Host '== 旧 nupkg クリーンアップ ==' -ForegroundColor Cyan
$keep = @{}
$manifests = Get-ChildItem $ArtifactsDir -Filter 'releases.*.json'
if (-not $manifests) { throw 'artifacts に releases.*.json が見つかりません' }
foreach ($m in $manifests) {
    foreach ($asset in (Get-Content $m.FullName -Raw | ConvertFrom-Json).Assets) {
        if ($asset.FileName) { $keep[$asset.FileName] = $true }
    }
}
foreach ($assetName in $previousManifestAssets.Keys) {
    $keep[$assetName] = $true
}
Write-Host "  保持対象 nupkg: $($keep.Count) 件 (現行 + 直前版)"

$api = "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket"
$headers = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }

# MSIが配信済みになった後で、再びPerUser版を入れられないよう旧Setup.exeをR2から除去する。
foreach ($legacySetup in $legacySetupFiles) {
    $encoded = [uri]::EscapeDataString($legacySetup.Name)
    try {
        Invoke-RestMethod -Method Delete -Uri "$api/objects/$encoded" -Headers $headers -TimeoutSec 30 | Out-Null
        Write-Host "  🗑️  旧PerUserインストーラ: $($legacySetup.Name)"
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $statusCode = if ($responseProperty -and $responseProperty.Value) { [int]$responseProperty.Value.StatusCode } else { $null }
        if ($statusCode -ne 404) { throw }
    }
}
$legacySetupUrls = @(
    foreach ($legacySetup in $legacySetupFiles) {
        $url = "$BaseUrl/$($legacySetup.Name)"
        try {
            $null = Invoke-WebRequest -Method Head -Uri $url -TimeoutSec 30
            $url
        } catch {
            $responseProperty = $_.Exception.PSObject.Properties['Response']
            $statusCode = if ($responseProperty -and $responseProperty.Value) { [int]$responseProperty.Value.StatusCode } else { $null }
            if ($statusCode -ne 404) { throw }
        }
    }
)
if ($legacySetupUrls.Count -gt 0) {
    $legacyPurgeBody = [PSCustomObject]@{ files = $legacySetupUrls } | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
        -Headers $cfHeaders -ContentType 'application/json' -Body $legacyPurgeBody -TimeoutSec 30 | Out-Null
}

$allKeys = [System.Collections.Generic.List[string]]::new()
$cursor = ''
while ($true) {
    $uri = "$api/objects?per_page=1000" + $(if ($cursor) { "&cursor=$cursor" })
    $resp = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 30
    foreach ($obj in $resp.result) { $allKeys.Add($obj.key) }
    $info = $resp.PSObject.Properties['result_info']
    if (-not $info -or -not $info.Value) { break }
    $truncated = $info.Value.PSObject.Properties['is_truncated']
    if (-not $truncated -or -not $truncated.Value) { break }
    $cursorProp = $info.Value.PSObject.Properties['cursor']
    $cursor = if ($cursorProp) { $cursorProp.Value } else { '' }
    if (-not $cursor) { break }
}

# 全プロジェクト共通の保持ポリシー: 直近 2 バージョン。
# 旧実装は '*.nupkg' だけを削除対象にしていたため、バージョン付きの配布物
# (zip / deb / rpm / AppImage 等) が R2 に永久に溜まっていた (Ferry で 351 個 7.2GB)。
$KeepVersionCount = 2
$versionPattern = '(\d+\.\d+\.\d+)'
$allVersions = @(
    $allKeys | ForEach-Object {
        $m = [regex]::Match($_, $versionPattern)
        if ($m.Success) { $m.Groups[1].Value }
    } | Sort-Object -Property { [version]$_ } -Unique
)
$keepVersions = @($allVersions | Select-Object -Last $KeepVersionCount)
Write-Host "  保持バージョン: $($keepVersions -join ', ') (全 $($allVersions.Count) 世代)"

$toDelete = $allKeys | Where-Object {
    # manifest が参照するファイルは絶対保持 (消すと自動更新が壊れる)
    if ($keep.ContainsKey($_)) { return $false }
    # 固定ファイル名はバージョン文字列を含まない = 毎リリース上書きなので保持
    $m = [regex]::Match($_, $versionPattern)
    if (-not $m.Success) { return $false }
    return $keepVersions -notcontains $m.Groups[1].Value
}
if (-not $toDelete) {
    Write-Host '  ✅ 削除対象なし'
} else {
    $deleted = 0; $failed = 0
    foreach ($key in $toDelete) {
        $encoded = [uri]::EscapeDataString($key)
        try {
            Invoke-RestMethod -Method Delete -Uri "$api/objects/$encoded" -Headers $headers -TimeoutSec 30 | Out-Null
            Write-Host "  🗑️  $key"
            $deleted++
        } catch {
            Write-Warning "  削除失敗: $key — $($_.Exception.Message)"
            $failed++
        }
    }
    Write-Host "  🧹 クリーンアップ: $deleted 削除 / $failed 失敗"
    if ($failed -gt 0 -and $deleted -eq 0) { throw '旧 nupkg の削除がすべて失敗しました。API token の権限を確認してください。' }
}

Write-Host "`n🎉 リリース完了: v$version → $BaseUrl" -ForegroundColor Green
