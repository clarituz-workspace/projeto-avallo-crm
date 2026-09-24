[CmdletBinding()]
param(
    [ValidateSet('http', 'https')]
    [string]$Profile = 'https',
    [switch]$Watch,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localPorts = @(7128, 5152)
$listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in $localPorts })

if ($listeners.Count -gt 0) {
    $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    $processNames = @($processIds | ForEach-Object {
        (Get-Process -Id $_ -ErrorAction SilentlyContinue).ProcessName
    } | Where-Object { $_ } | Select-Object -Unique)

    Write-Host 'O Avallo já está em execução em https://localhost:7128/login.' -ForegroundColor Yellow
    if ($processNames.Count -gt 0) {
        Write-Host "Processo: $($processNames -join ', ') | PID: $($processIds -join ', ')"
    }
    Write-Host 'Encerre a instância atual com Ctrl+C antes de iniciar outra.'
    exit 0
}

Push-Location $projectRoot
try {
    $arguments = @('--project', 'Avallo.Web', '--launch-profile', $Profile)
    if ($NoBuild) { $arguments += '--no-build' }
    if ($Watch) {
        dotnet watch @arguments
    }
    else {
        dotnet run @arguments
    }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
