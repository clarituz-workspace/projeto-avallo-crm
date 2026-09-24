---
tags:
  - avallo
  - modulo-1
  - mercado-livre
  - oauth
  - marketplace
fase: fase-1-mvp
status: concluido
ultima-revisao: 2026-08-03
---

# Modulo 1 — Mercado Livre

> Primeira entrega da Fase 1 (MVP), integrada a MELI API em `https://api.mercadolibre.com`.

## Checklist funcional

- [x] Aplicativo cadastrado e configuravel por Client ID e Client Secret
- [x] Autenticacao OAuth 2.0
- [x] Fluxo OAuth protegido com state e PKCE (`S256`)
- [x] Callback unico integrado ao sistema de conectores
- [x] Armazenamento criptografado de access token e refresh token
- [x] Consulta da lista de pedidos do vendedor
- [x] Consulta dos detalhes e itens do pedido
- [x] Consulta dos dados de pagamento e data de liberacao
- [x] Consulta dos dados e status do vendedor
- [x] Paginacao completa de pedidos
- [x] Sincronizacao incremental e idempotente
- [x] Conversao de datas UTC para `America/Sao_Paulo`
- [x] Calculo de taxas com `sale_fee` e custo de envio
- [x] Limite local equivalente a 6.000 requisicoes por hora por token
- [x] Tratamento de HTTP 429 com `Retry-After`
- [x] Refresh automatico do token antes do vencimento
- [x] Marcacao da conexao como expirada quando a renovacao nao e recuperavel

## Endpoints MELI utilizados

| Endpoint | Implementacao | Finalidade |
|---|---|---|
| `GET /orders/search?seller={id}` | Concluida | Lista paginada de pedidos do vendedor |
| `GET /orders/{id}` | Concluida | Detalhes, itens, comprador, status e envio |
| `GET /payments/{id}` | Concluida | Pagamento, valor liquido e data de liberacao |
| `GET /users/{id}` | Concluida | Identificacao, apelido e status do vendedor |
| `POST /oauth/token` | Concluida | Troca do authorization code e renovacao do token |

## OAuth e ciclo dos tokens

- A autorizacao inicia em `https://auth.mercadolivre.com.br/authorization`.
- O fluxo envia `state`, `redirect_uri`, `code_challenge` e `code_challenge_method=S256`.
- O callback troca o codigo por access token e refresh token.
- O tempo de expiracao retornado pela MELI e persistido na conexao.
- Quando faltam dois minutos ou menos para expirar, o `ConnectorGateway` renova o token automaticamente antes da chamada.
- Tokens novos substituem os anteriores e permanecem criptografados com ASP.NET Core Data Protection, usando finalidade separada por tenant e conector.

## Pedidos e pagamentos

- A busca utiliza seller ID, offset, limite e ordenacao decrescente por data.
- O limite por pagina e controlado entre 1 e 50 pedidos.
- Filtros de data sao enviados em UTC no formato esperado pela MELI.
- Pedidos, itens e pagamentos sao normalizados para os contratos padrao do Core.
- A sincronizacao percorre todas as paginas desde a data solicitada.
- Status financeiros e de fulfillment sao convertidos para enums padronizados.

## Datas e timezone

- Datas recebidas em UTC sao interpretadas com `DateTimeOffset`.
- Criacao, aprovacao, liberacao e entrega sao convertidas para `America/Sao_Paulo`.
- Os testes confirmam o offset brasileiro esperado para os dados de exemplo.

## Taxas

- Soma de `sale_fee` de cada item do pedido.
- Inclusao do custo de envio retornado em `shipping.cost`.
- Classificacao separada entre comissao do marketplace e frete do vendedor.
- Valor liquido prioriza `transaction_details.net_received_amount`, com fallback para bruto menos taxas.

## Rate limit e resiliencia

- Token bucket independente por access token.
- Limite de 100 requisicoes por minuto, equivalente a 6.000 por hora.
- Fila local de ate 500 requisicoes.
- Ate tres tentativas em respostas HTTP 429.
- Respeito ao cabecalho `Retry-After`, limitado a 30 segundos por espera.
- Erros 429 e 5xx sao classificados como transitorios.

## Testes existentes

- [x] Autenticacao retorna vendedor, access token, refresh token e expiracao de seis horas
- [x] Inicio OAuth utiliza state e PKCE
- [x] Renovacao segue o protocolo MELI
- [x] Pedido e normalizado com taxas, pagamento e timezone de Sao Paulo
- [x] Consulta de `sale_fee`, frete e valor liquido

## Configuracao operacional

```text
Connectors__MercadoLivre__ClientId
Connectors__MercadoLivre__ClientSecret
Connectors__MercadoLivre__RedirectUri
```

Callback local:

```text
https://localhost:7128/api/connectors/oauth/callback
```

Callback Azure:

```text
https://Avallo-web.purplemeadow-42f09588.brazilsouth.azurecontainerapps.io/api/connectors/oauth/callback
```

## Criterio de aceite

O modulo e considerado concluido quando o vendedor conecta sua conta via OAuth, a sincronizacao percorre e normaliza pedidos, pagamentos e taxas sem exceder o limite da MELI, as datas sao apresentadas no timezone brasileiro e tokens vencidos sao renovados sem intervencao manual.

