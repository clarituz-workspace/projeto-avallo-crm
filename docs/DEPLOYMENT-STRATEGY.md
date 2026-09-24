# Blue/Green e Canary no Azure Container Apps

O Avallo usa revisoes simultaneas do Azure Container Apps. O slot `blue` representa a
versao estavel; `green` representa a candidata. Cada label possui URL propria para testes
internos, enquanto o FQDN principal distribui o trafego pelos pesos configurados.

O modo de múltiplas revisões não aceita sticky sessions no Azure Container Apps. Por isso,
`Initialize` configura a afinidade como `none`. WebSockets já estabelecidos permanecem na
mesma réplica e o cliente SignalR mantém sua política de reconexão.

## Fluxo operacional

```text
Produção ── 100% ──> Blue (estável)
                    Green (candidata, URL isolada, 0%)

Canary   ─── 90% ──> Blue
          └─ 10% ──> Green

Promoção ── 100% ──> Green
Rollback ── 100% ──> Blue
```

## Comandos

```powershell
# Executado uma única vez
.\scripts\release-azure.ps1 -Action Initialize

# A imagem deve existir no ACR
.\scripts\release-azure.ps1 -Action DeployGreen -Version 20260730.23

# Green é testável em https://green.<fqdn>; produção continua no Blue
.\scripts\release-azure.ps1 -Action Canary -CanaryPercent 10
.\scripts\release-azure.ps1 -Action Promote

# Retorno instantâneo à versão anterior
.\scripts\release-azure.ps1 -Action Rollback

# Depois do período de observação, Green vira o Blue do próximo ciclo
.\scripts\release-azure.ps1 -Action NewCycle
.\scripts\release-azure.ps1 -Action Status
```

`DeployGreen`, `Canary` e `Promote` possuem health gate. A candidata precisa estar
`Healthy` e responder `200` em `/health` pela URL do label antes de receber tráfego.
Mantenha o Blue ativo durante a janela de observação para rollback sem novo build.

As migrações precisam continuar retrocompatíveis durante todo o canário, pois Blue e Green
usam o mesmo PostgreSQL. Alterações destrutivas de schema devem seguir expand/contract:
primeiro adicionar, depois migrar consumidores e somente em uma versão posterior remover.
