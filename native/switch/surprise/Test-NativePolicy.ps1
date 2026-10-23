param(
    [Parameter(Mandatory=$true)][string]$ZigPath,
    [string]$OutputDirectory=(Join-Path ([IO.Path]::GetTempPath()) ("DR-SwordSurprise-"+[Guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference='Stop'
if (-not (Test-Path -LiteralPath $ZigPath -PathType Leaf)) { throw 'Indica una instalación local de Zig (probado con 0.13.0).' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'La salida debe ser una carpeta nueva.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$previousCache=[Environment]::GetEnvironmentVariable('ZIG_GLOBAL_CACHE_DIR','Process')
try {
    $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $OutputDirectory 'zig-cache'
    $policy=Join-Path $PSScriptRoot 'policy.c'
    $tests=Join-Path $PSScriptRoot 'policy_tests.c'
    $hostExe=Join-Path $OutputDirectory 'policy-tests.exe'
    & $ZigPath cc -std=c11 -Wall -Wextra -Werror $policy $tests -o $hostExe
    if($LASTEXITCODE -ne 0){throw 'Falló la compilación de pruebas nativas.'}
    & $hostExe
    if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas de selección.'}
    $object=Join-Path $OutputDirectory 'policy-aarch64.o'
    & $ZigPath cc -target aarch64-freestanding-none -ffreestanding -fno-stack-protector -std=c11 -Wall -Wextra -Werror -O2 -c $policy -o $object
    if($LASTEXITCODE -ne 0){throw 'Falló la compilación ARM64.'}
    $elf=[IO.File]::ReadAllBytes($object)
    if($elf.Length -lt 64 -or $elf[0] -ne 127 -or $elf[1] -ne 69 -or $elf[2] -ne 76 -or $elf[3] -ne 70 -or $elf[4] -ne 2 -or $elf[5] -ne 1 -or [BitConverter]::ToUInt16($elf,18) -ne 183){throw 'El resultado no es ELF64 AArch64 little-endian.'}
    Write-Output "PASS ELF64 AArch64. Artefactos: $OutputDirectory"
    Write-Output 'Esto comprueba el núcleo de selección; no genera un módulo NSO ni acredita jugabilidad.'
}
finally { [Environment]::SetEnvironmentVariable('ZIG_GLOBAL_CACHE_DIR',$previousCache,'Process') }
