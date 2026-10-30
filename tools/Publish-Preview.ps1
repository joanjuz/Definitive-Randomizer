param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Version = '0.9.0-preview.1'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+-[A-Za-z0-9.-]+$') { throw 'Expected a prerelease version.' }
$repo = Split-Path $PSScriptRoot -Parent
$notes = Join-Path $repo ("docs/3ds-progressive/RELEASE_"+$Version.Replace('-','_').Replace('preview.','PREVIEW_')+'.md')
if (-not (Test-Path -LiteralPath $notes)) { throw "Missing release notes: $notes" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$name = "DefinitiveRandomizer-$Version-win-x64"
$stage = Join-Path $output $name
$zip = Join-Path $output ($name+'.zip')
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) { throw 'Use a new output directory; existing packages are never overwritten.' }
$logs = Join-Path $output 'build-logs'
New-Item -ItemType Directory -Path $stage,$logs -Force | Out-Null
Push-Location $repo
try {
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify source revision.' }
    foreach ($tool in @('ffmpeg.exe','ffprobe.exe','mobipeg-v2.3-source.tar.gz')) {
        if (-not (Test-Path -LiteralPath (Join-Path $repo "3DS.Progressive.App/Tools/Mobipeg/$tool"))) { throw "Missing Mobipeg dependency: $tool" }
    }
    $videoSource = Join-Path $repo '3DS.Progressive.App/Tools/Mobipeg/mobipeg-v2.3-source.tar.gz'
    if ((Get-FileHash -LiteralPath $videoSource -Algorithm SHA256).Hash -ne '532CAFE82CB0E3136A2677EAA82F615EB2BCAFAB2439C264BDE2CCB22DC1965C') { throw 'Mobipeg source checksum mismatch.' }
    & dotnet publish ./3DS.Progressive.App/3DS.Progressive.App.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:SatelliteResourceLanguages=en "-p:Version=$Version" "-p:SourceRevisionId=$revision" -o $stage "-bl:$logs/publish-app-{}.binlog" *> (Join-Path $logs 'publish-app.log')
    if ($LASTEXITCODE -ne 0) { throw "WPF publish failed. See $logs/publish-app.log" }
    & dotnet publish ./pk3DS.WinForms/pk3DS.WinForms.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:SatelliteResourceLanguages=en "-p:Version=$Version" "-p:SourceRevisionId=$revision" -o (Join-Path $stage 'LegacyBatch') "-bl:$logs/publish-legacy-{}.binlog" *> (Join-Path $logs 'publish-legacy.log')
    if ($LASTEXITCODE -ne 0) { throw "Legacy publish failed. See $logs/publish-legacy.log" }
    Move-Item -LiteralPath (Join-Path $stage '3DS.Progressive.exe') -Destination (Join-Path $stage 'DefinitiveRandomizer.exe')
    foreach ($destination in @('custom_balance_templates','LegacyBatch/custom_balance_templates','Templates','LegacyBatch/Templates')) {
        New-Item -ItemType Directory -Path (Join-Path $stage $destination) -Force | Out-Null
    }
    # Package only repository-owned defaults, never private recipes or local _state.
    $defaults = @(& git ls-files custom_balance_templates)
    foreach ($file in $defaults) {
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage 'custom_balance_templates') -Force
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage 'LegacyBatch/custom_balance_templates') -Force
    }
    foreach ($file in @(& git ls-files pk3DS.WinForms/Templates)) {
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage 'Templates') -Force
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage 'LegacyBatch/Templates') -Force
    }
    Copy-Item -LiteralPath (Join-Path $repo 'pk3DS.WinForms/Templates/field_items.txt') -Destination (Join-Path $stage 'custom_balance_templates/field_items.txt') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'pk3DS.WinForms/Templates/field_items.txt') -Destination (Join-Path $stage 'LegacyBatch/custom_balance_templates/field_items.txt') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE.md') -Destination $stage
    Copy-Item -LiteralPath $notes -Destination (Join-Path $stage 'LEEME.md')
    "Version: $Version`nCommit: $revision`nSource: https://github.com/joanjuz/Definitive-Randomizer/tree/v$Version" | Set-Content -LiteralPath (Join-Path $stage 'VERSION.txt') -Encoding utf8
    # Only remove development metadata from this newly generated package.
    Get-ChildItem -LiteralPath $stage -File -Recurse -Force | Where-Object { $_.Extension -in @('.pdb','.binlog','.log') -or $_.Name -eq '.gitignore' } | ForEach-Object {
        if (-not $_.FullName.StartsWith($stage+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected package path.' }
        Remove-Item -LiteralPath $_.FullName -Force
    }
    foreach ($required in @('DefinitiveRandomizer.exe','LegacyBatch/pk3DS.WinForms.exe','custom_balance_templates/field_items.txt','Templates/trainer_better_movesets.txt','Tools/Mobipeg/ffmpeg.exe','Tools/Mobipeg/ffprobe.exe','Tools/Mobipeg/mobipeg-v2.3-source.tar.gz','LICENSE.md','LEEME.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Missing packaged dependency: $required" }
    }
    # Neutral .NET resources suffice; app labels and ROM language banks are unaffected.
    $satellites = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Filter '*.resources.dll')
    if ($satellites.Count) { throw 'Unexpected localized runtime assemblies in the compact package.' }
    foreach ($file in $defaults) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $repo $file) -Algorithm SHA256).Hash
        foreach ($folder in @('custom_balance_templates','LegacyBatch/custom_balance_templates')) {
            $packaged = Join-Path (Join-Path $stage $folder) (Split-Path $file -Leaf)
            if ((Get-FileHash -LiteralPath $packaged -Algorithm SHA256).Hash -ne $sourceHash) { throw "Template changed while packaging: $file" }
        }
    }
    $forbidden = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force | Where-Object { $_.Extension -in @('.3ds','.cia','.cxi','.cci','.rom','.nds','.pdb','.binlog','.log','.bak') -or $_.FullName -match '[\\/](_state|ErisTemplate|sprites)[\\/]' })
    if ($forbidden.Count) { throw 'Package contains private or generated data.' }
    $hashes = Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$_.FullName.Substring($stage.Length+1).Replace('\','/')
    }
    $hashes | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
    Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
    '{0}  {1}' -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(),(Split-Path $zip -Leaf) | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Package: $zip"
} finally { Pop-Location }
