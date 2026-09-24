---
tags:
  - avallo
  - fase-1
  - mvp
  - mercado-livre
  - aceite
status: aguardando-aceite-operacional
ultima-revisao: 2026-08-03
---

# Aceite da Fase 1 MVP

> Core + Modulo Mercado Livre. Meta: produto funcional em producao para vendedores do Mercado Livre.

## Escopo da fase

- [x] Core: autenticacao por e-mail, senha e Google
- [x] Core: isolamento multi-tenant
- [x] Core: banco de dados e migrations
- [x] Core: painel financeiro base
- [x] Core: exportacao PDF, Excel e CSV
- [x] Core: sistema central de notificacoes e outbox
- [x] Core: acesso secundario do Contador
- [x] Core: Contador restrito a somente leitura
- [x] Conector ML: OAuth 2.0 com PKCE
- [x] Conector ML: sincronizacao de pedidos
- [x] Conector ML: pagamentos, liberacao e taxas
- [x] Conector ML: refresh automatico de tokens
- [x] Frontend: dashboard unificado
- [x] Frontend: filtros completos
- [x] Frontend: tabela de lancamentos com busca e ordenacao
- [x] Deploy no Azure Container Apps
- [x] Health check de producao respondendo `healthy`
- [x] Monitoramento de logs configurado no Log Analytics
- [x] Azure Blob Storage habilitado em producao
- [x] Credenciais e callback do Mercado Livre configurados no Container App

## Bloqueadores do aceite final

- [x] Habilitar e configurar SMTP em producao (`Email__Enabled=true`)
- [x] Validar entrega real dos quatro tipos de e-mail
- [ ] Conectar uma conta homologada do Mercado Livre em producao
- [ ] Executar sincronizacao ponta a ponta de pedidos, pagamentos e taxas
- [ ] Conferir os dados sincronizados no dashboard, relatorios e exportacoes
- [ ] Registrar evidencia do teste de aceite do vendedor ML

## Estado de producao verificado

| Item | Estado em 03/08/2026 |
|---|---|
| Container App | `Running` |
| Provisionamento | `Succeeded` |
| Health check | `healthy` |
| Revisao ativa | `Avallo-web--auth-clean-20260803-8` |
| Log Analytics | Configurado |
| Blob Storage | Habilitado |
| Credenciais ML | Configuradas por secrets |
| Callback ML | Configurada para a URL publica |
| E-mail SMTP | Desabilitado |

## URL de producao

```text
https://Avallo-web.purplemeadow-42f09588.brazilsouth.azurecontainerapps.io
```

## Decisao de aceite

**Estado atual: tecnicamente implementada, aguardando aceite operacional.**

A entrega somente deve receber o status `100% funcional para vendedores do ML` depois que SMTP estiver habilitado e o fluxo real OAuth → sincronizacao → dashboard → relatorio → notificacao for executado com sucesso em producao.
