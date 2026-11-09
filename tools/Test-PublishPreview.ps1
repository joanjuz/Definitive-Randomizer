param([string]$ResultsPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Publish-Preview.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Packager contains PowerShell parser errors.' }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-DistributionMetadata' }, $true)
if (-not $definition) { throw 'Distribution metadata function not found.' }
# Load only the pure function. The packager body (and every dotnet publish call) is NEVER executed.
. ([scriptblock]::Create($definition.Extent.Text))
$notes = Get-Content -LiteralPath (Join-Path $repo 'docs/3ds-progressive/RELEASE_1.0.0.md') -Raw -Encoding utf8
$revision = 'a' * 40
$otherRevision = 'b' * 40
$results = [Collections.Generic.List[object]]::new()
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; $results.Add([pscustomobject]@{Case=$Name;Passed=$true}); Write-Output "PASS: $Name" }
    catch { $results.Add([pscustomobject]@{Case=$Name;Passed=$false}); Write-Output "FAIL: $Name — $($_.Exception.Message)" }
}
function Reject([scriptblock]$Body, [string]$Expected) {
    try { & $Body | Out-Null } catch { Assert ($_.Exception.Message -eq $Expected) 'Wrong rejection reason.'; return }
    throw 'Expected a rejection.'
}
$publicError = 'PUBLIC requires -PublicReleaseCommit equal to the full source HEAD SHA.'
Case 'PrivateDefaultStable' {
    $parameter = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'DistributionMode' }
    Assert ($parameter.DefaultValue.Value -ceq 'PRIVATE') 'Script default is not PRIVATE.'
    $r = Get-DistributionMetadata -Version '1.0.0' -Revision $revision -NotesText $notes
    Assert ($r.publicRelease -is [bool] -and -not $r.publicRelease) 'Stable version enabled public metadata.'
    Assert ($r.mode -ceq 'PRIVATE' -and $null -eq $r.publicReleaseCommit) 'Unexpected mode or confirmation.'
    Assert ($r.notes.Contains('Entrega privada v1.0.0') -and $r.notes.Contains('publicRelease=false')) 'Private notes are inconsistent.'
    Assert (-not $r.notes.Contains('@BUILD_COMMIT@') -and $r.notes.Contains($revision)) 'Commit not resolved.'
    Assert ($r.notes -notmatch '@DISTRIBUTION_[A-Z_]+@') 'Unresolved private status tokens.'
}
Case 'PrivateExplicit' {
    $r = Get-DistributionMetadata -Mode PRIVATE -Version '1.0.0' -Revision $revision -NotesText $notes
    $json = $r | ConvertTo-Json | ConvertFrom-Json
    Assert ($json.publicRelease -is [bool] -and -not $json.publicRelease -and $json.mode -ceq 'PRIVATE') 'Private JSON flag is not Boolean false.'
}
Case 'PrivatePrerelease' {
    $r = Get-DistributionMetadata -Version '1.0.0-rc.1' -Revision $revision -NotesText $notes
    Assert (-not $r.publicRelease -and $r.notes.Contains('Entrega privada v1.0.0-rc.1')) 'Prerelease selected wrong status.'
}
Case 'PrivateRejectsPublicConfirmation' {
    Reject { Get-DistributionMetadata -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit $revision } '-PublicReleaseCommit is only valid with -DistributionMode PUBLIC.'
}
Case 'PublicMissingConfirmation' {
    Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes } $publicError
}
Case 'PublicShortConfirmation' {
    Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit 'aaaaaaa' } $publicError
}
Case 'PublicWrongCommit' {
    Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit $otherRevision } $publicError
}
Case 'PublicConfirmationCaseMismatch' {
    Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit $revision.ToUpperInvariant() } $publicError
}
Case 'PublicSimulatedSelection' {
    $r = Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit $revision
    $json = $r | ConvertTo-Json | ConvertFrom-Json
    Assert ($json.publicRelease -is [bool] -and $json.publicRelease -and $json.mode -ceq 'PUBLIC') 'Public JSON flag is not Boolean true.'
    Assert ($json.publicReleaseCommit -ceq $revision) 'Confirmation is not bound to source commit.'
    Assert ($r.notes.Contains('Distribución pública v1.0.0') -and $r.notes.Contains('publicRelease=true') -and $r.notes.Contains($revision)) 'Public notes or commit are inconsistent.'
    Assert ($r.notes -notmatch 'candidata privada|Entrega privada|todavía no publicada|publicRelease=false|paquete privado') 'Public output contains private distribution status.'
    Assert ($r.notes -notmatch '@DISTRIBUTION_[A-Z_]+@|@BUILD_COMMIT@') 'Unresolved distribution tokens.'
}
Case 'PublicModeCaseNormalized' {
    $r = Get-DistributionMetadata -Mode public -Version '1.0.0' -Revision $revision -NotesText $notes -PublicReleaseCommit $revision
    Assert ($r.mode -ceq 'PUBLIC' -and $r.publicRelease) 'Mode casing bypassed selection.'
}
Case 'InvalidMode' {
    try { Get-DistributionMetadata -Mode PUBL1C -Version '1.0.0' -Revision $revision -NotesText $notes | Out-Null }
    catch { Assert ($_.FullyQualifiedErrorId -like 'ParameterArgumentValidationError*') 'Wrong invalid-mode rejection.'; return }
    throw 'Invalid mode was not rejected by ValidateSet.'
}
Case 'InvalidSourceRevision' {
    Reject { Get-DistributionMetadata -Version '1.0.0' -Revision 'short' -NotesText $notes } 'Expected the full source HEAD SHA.'
}
Case 'LegacyPrivateNotes' {
    $r = Get-DistributionMetadata -Version '0.15.0-preview.2' -Revision $revision -NotesText '# Historical preview notes'
    Assert (-not $r.publicRelease -and $r.notes.StartsWith('> **Entrega privada') -and $r.notes.Contains('# Historical preview notes')) 'PRIVATE broke historical notes.'
}
Case 'LegacyPublicNotesRejected' {
    Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText '# Historical preview notes' -PublicReleaseCommit $revision } 'PUBLIC requires mode-aware release notes.'
}
foreach ($markerCase in @(
    @{Name='PublicMissingStatus';Marker='@DISTRIBUTION_STATUS@'},
    @{Name='PublicMissingMetadata';Marker='@DISTRIBUTION_METADATA@'},
    @{Name='PublicMissingWorkflow';Marker='@DISTRIBUTION_WORKFLOW@'}
)) {
    Case $markerCase.Name {
        Reject { Get-DistributionMetadata -Mode PUBLIC -Version '1.0.0' -Revision $revision -NotesText $notes.Replace($markerCase.Marker,'') -PublicReleaseCommit $revision } 'PUBLIC requires mode-aware release notes.'
    }
}
$failed = @($results | Where-Object { -not $_.Passed }).Count
if ($ResultsPath) { $results | ConvertTo-Json | Set-Content -LiteralPath $ResultsPath -Encoding utf8 }
Write-Output "Passed: $($results.Count-$failed), Failed: $failed, Skipped: 0"
if ($failed) { exit 1 }
