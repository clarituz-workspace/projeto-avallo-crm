---
tags:
  - avallo
  - fase-2
  - shopee
  - mercado-livre
  - aceite
status: em-desenvolvimento
ultima-revisao: 2026-08-03
---

# Aceite da Fase 2 — Modulo Shopee

> Meta: permitir que clientes conectem Mercado Livre e Shopee simultaneamente, com dados consolidados no mesmo painel.

## Escopo tecnico

- [x] Conector Shopee separado do Core
- [x] OAuth da Shopee implementado
- [x] Assinatura HMAC-SHA256 implementada
- [x] Assinatura publica e assinatura vinculada a loja
- [x] Mapeamento de pedidos para o contrato padrao do Core
- [x] Mapeamento de pagamentos para o contrato padrao do Core
- [x] Mapeamento de taxas para o contrato padrao do Core
- [x] Sincronizacao paginada de pedidos
- [x] Refresh automatico do token
- [x] Rate limit de 1.000 requisicoes por minuto por loja
- [x] Dashboard preparado para consolidar ML e Shopee
- [x] Relatorios e exportacoes agrupam dados por plataforma
- [x] Testes unitarios de assinatura HMAC
- [x] Testes existentes do conector Mercado Livre continuam aprovados

## Pendencias da entrega

- [ ] Obter acesso de parceiro da Shopee Open Platform
- [ ] Configurar `PartnerId` e `PartnerKey`
- [ ] Configurar e validar a URL base do sandbox
- [ ] Executar OAuth real com uma loja sandbox brasileira
- [ ] Validar payloads reais de pedidos, pagamentos e taxas da Shopee BR
- [ ] Desbloquear o card da Shopee na tela de conectores
- [ ] Entregar a mesma experiencia de conexao ativa usada pelo Mercado Livre
- [ ] Conectar ML e Shopee simultaneamente no mesmo tenant de teste
- [ ] Sincronizar as duas plataformas sem conflito ou duplicidade
- [ ] Validar os totais combinados no dashboard
- [ ] Validar filtros individuais por ML e Shopee
- [ ] Validar PDF, Excel e CSV com dados das duas plataformas
- [ ] Adicionar teste de regressao automatizado do fluxo simultaneo ML + Shopee
- [ ] Executar teste de aceite em producao ou ambiente homologado

## Avaliacao por item da fase

| Item | Estado | Observacao |
|---|---|---|
| OAuth + HMAC e mapeamento padrao | Implementado | Aguarda validacao com credenciais e payload real |
| Pedidos, pagamentos e taxas | Implementado | Aguarda homologacao no sandbox BR |
| Tela de conexao igual ao ML | Pendente | Card existe, mas permanece bloqueado como `Em desenvolvimento` |
| Relatorio combinado ML + Shopee | Implementado no Core | Precisa ser validado com dados reais das duas plataformas |
| Regressao do Mercado Livre | Parcial | Testes isolados do ML passam; falta cenario simultaneo automatizado |

## Estado atual da interface

O conector Shopee aparece na tela, mas o botao de conexao nao e oferecido. O card informa que aguarda credenciais da Shopee Open Platform. Esse bloqueio deve permanecer ate a homologacao no sandbox ser concluida.

## Cenario obrigatorio de regressao

1. Conectar uma conta Mercado Livre.
2. Conectar uma loja Shopee no mesmo tenant.
3. Sincronizar pedidos das duas plataformas.
4. Repetir a sincronizacao e confirmar idempotencia.
5. Confirmar que pedidos ML continuam inalterados.
6. Conferir faturado, recebido, taxas e valor a receber consolidados.
7. Filtrar individualmente por Mercado Livre e Shopee.
8. Exportar PDF consolidado e por plataforma.
9. Exportar Excel e confirmar uma aba para cada marketplace.
10. Confirmar que outro tenant nao visualiza nenhum desses dados.

## Configuracao pendente

```text
Connectors__Shopee__PartnerId
Connectors__Shopee__PartnerKey
Connectors__Shopee__RedirectUri
Connectors__Shopee__BaseUrl
```

## Decisao de aceite

**Estado atual: implementacao tecnica avancada, entrega ainda nao concluida.**

A Fase 2 somente podera receber o status `clientes podem conectar ML e Shopee simultaneamente` quando o card estiver desbloqueado, OAuth e sincronizacao forem homologados com a Shopee BR e o cenario de regressao simultanea for aprovado.

