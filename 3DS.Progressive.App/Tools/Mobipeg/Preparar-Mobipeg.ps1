$ErrorActionPreference='Stop'
$version='v2.3'
$archive=Join-Path ([IO.Path]::GetTempPath()) ('mobipeg-'+[Guid]::NewGuid()+'.zip')
$stage=Join-Path ([IO.Path]::GetTempPath()) ('mobipeg-'+[Guid]::NewGuid())
Invoke-WebRequest "https://github.com/quatric/mobipeg/releases/download/$version/mobipeg-windows-x86_64.zip" -OutFile $archive
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '9F15C9F7359BE362AE8704EE0A0D6FC8046162311EEFE471F3466EBF4530831B'){throw 'SHA-256 de Mobipeg no coincide.'}
Expand-Archive -LiteralPath $archive -DestinationPath $stage
foreach($name in @('ffmpeg.exe','ffprobe.exe')){
    $tool=Get-ChildItem -LiteralPath $stage -Filter $name -File -Recurse | Select-Object -First 1
    if($null -eq $tool){throw "Falta $name"}
    Copy-Item -LiteralPath $tool.FullName -Destination (Join-Path $PSScriptRoot $name)
}
$source=Join-Path $PSScriptRoot 'mobipeg-v2.3-source.tar.gz'
if(-not(Test-Path -LiteralPath $source)){Invoke-WebRequest "https://github.com/quatric/mobipeg/releases/download/$version/mobipeg-v2.3-source.tar.gz" -OutFile $source}
if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne '532CAFE82CB0E3136A2677EAA82F615EB2BCAFAB2439C264BDE2CCB22DC1965C'){throw 'SHA-256 de las fuentes no coincide.'}
Write-Output 'Mobipeg v2.3 verificado. Las herramientas y las fuentes están listas.'
