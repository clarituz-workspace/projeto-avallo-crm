[CmdletBinding()]
param([switch]$RemoveData)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $arguments = @('-f', 'compose.local.yml', 'down')
    if ($RemoveData) { $arguments += '--volumes' }
    docker compose @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
