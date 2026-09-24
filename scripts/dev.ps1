[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$ResetData,
    [switch]$TrustHttpsCertificate,
    [switch]$Watch,
    [ValidateSet('http', 'https')]
    [string]$Profile = 'https'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $PSCommandPath

& (Join-Path $scriptRoot 'setup-local.ps1') `
    -SkipBuild:$SkipBuild `
    -ResetData:$ResetData `
    -TrustHttpsCertificate:$TrustHttpsCertificate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $scriptRoot 'run-local.ps1') -Profile $Profile -Watch:$Watch -NoBuild:$SkipBuild
exit $LASTEXITCODE
