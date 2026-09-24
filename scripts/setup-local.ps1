[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$ResetData,
    [switch]$TrustHttpsCertificate
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

Push-Location $projectRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK 10 não foi encontrado no PATH.'
    }

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker Desktop não foi encontrado no PATH.'
    }

    $sdkVersion = dotnet --version
    if (-not $sdkVersion.StartsWith('10.')) {
        throw "Este projeto requer .NET SDK 10. Versão encontrada: $sdkVersion"
    }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'O Docker Desktop foi encontrado, mas o daemon não está ativo. Inicie o Docker Desktop e tente novamente.'
    }

    if ($ResetData) {
        Write-Host 'Removendo os volumes locais...' -ForegroundColor Yellow
        docker compose -f compose.local.yml down --volumes
        if ($LASTEXITCODE -ne 0) {
            throw 'Não foi possível remover os volumes locais.'
        }
    }

    docker compose -f compose.local.yml up -d
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível iniciar PostgreSQL e MinIO.'
    }

    $deadline = (Get-Date).AddMinutes(2)
    do {
        $postgres = docker inspect --format '{{.State.Health.Status}}' avallo-postgres 2>$null
        $postgresReady = $postgres -eq 'healthy'
        $minioReady = (Test-NetConnection localhost -Port 9000 -InformationLevel Quiet) -and
            (Test-NetConnection localhost -Port 9001 -InformationLevel Quiet)
        if ($postgresReady -and $minioReady) { break }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    if (-not $postgresReady -or -not $minioReady) {
        docker compose -f compose.local.yml ps
        throw 'PostgreSQL ou MinIO não ficou pronto dentro do prazo.'
    }

    Write-Host 'Criando/atualizando o role de aplicacao (sujeito a RLS)...' -ForegroundColor Cyan
    Get-Content (Join-Path $projectRoot 'scripts/sql/create-app-role.sql') -Raw |
        docker exec -i avallo-postgres psql -U postgres -d Avallo -v app_password='Avallo_app' -f -
    if ($LASTEXITCODE -ne 0) {
        throw 'Nao foi possivel criar o role Avallo_app. Sem ele o Row Level Security fica inerte.'
    }

    if ($TrustHttpsCertificate) {
        dotnet dev-certs https --trust
        if ($LASTEXITCODE -ne 0) {
            throw 'Não foi possível confiar no certificado HTTPS de desenvolvimento.'
        }
    }

    dotnet restore Avallo.slnx
    if ($LASTEXITCODE -ne 0) {
        throw 'Falha ao restaurar os pacotes NuGet.'
    }

    if (-not $SkipBuild) {
        dotnet build Avallo.slnx --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw 'Falha ao compilar a solução.'
        }
    }

    Write-Host ''
    Write-Host 'Ambiente local pronto.' -ForegroundColor Green
    Write-Host 'Execute: .\scripts\run-local.ps1'
    Write-Host 'Acesse:  https://localhost:7128'
}
finally {
    Pop-Location
}
