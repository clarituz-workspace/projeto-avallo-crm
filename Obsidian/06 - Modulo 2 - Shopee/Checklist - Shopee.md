---
tags:
  - avallo
  - modulo-2
  - shopee
  - oauth
  - hmac
  - marketplace
fase: fase-2
status: aguardando-homologacao
ultima-revisao: 2026-08-03
---

# Modulo 2 — Shopee

> Segunda entrega da Fase 2, integrada a Shopee Open Platform API. A implementacao tecnica esta pronta, mas a ativacao permanece bloqueada ate acesso de parceiro e homologacao no sandbox.

## Checklist da implementacao

- [x] Modulo Shopee separado do Core
- [x] Autenticacao da loja pelo fluxo OAuth da Shopee
- [x] Troca do codigo por access token e refresh token
- [x] Refresh automatico antes do vencimento
- [x] Assinatura HMAC-SHA256 para endpoints publicos
- [x] Assinatura HMAC-SHA256 com access token e shop ID
- [x] Consulta da lista de pedidos
- [x] Consulta em lote dos detalhes dos pedidos
- [x] Consulta dos pagamentos
- [x] Consulta dos dados e status da loja
- [x] Consulta e classificacao das taxas Shopee
- [x] Paginacao por cursor
- [x] Sincronizacao incremental e idempotente
- [x] Rate limit de 1.000 requisicoes por minuto por loja
- [x] Retry para excesso de requisicoes
- [x] Testes unitarios das assinaturas HMAC
- [x] Card bloqueado como `Em desenvolvimento`

## Pendencias de ativacao

- [ ] Solicitar aprovacao como parceiro em `open.shopee.com`
- [ ] Obter `PartnerId` e `PartnerKey`
- [ ] Cadastrar a callback no aplicativo da Shopee
- [ ] Configurar a URL base fornecida para o sandbox
- [ ] Executar OAuth com uma loja sandbox
- [ ] Validar pedidos, pagamentos e taxas com payloads reais do Brasil
- [ ] Validar campos e comportamentos especificos da Shopee BR
- [ ] Executar teste de carga controlado do rate limit por loja
- [ ] Liberar a conexao depois da homologacao

## Endpoints implementados

| Endpoint | Estado | Finalidade |
|---|---|---|
| `GET /api/v2/order/get_order_list` | Implementado | Lista paginada de pedidos |
| `GET /api/v2/order/get_order_detail` | Implementado | Detalhes e itens dos pedidos |
| `GET /api/v2/payment/get_payment_list` | Implementado | Pagamentos, liberacao e taxas |
| `GET /api/v2/shop/get_shop_info` | Implementado | Identificacao e status da loja |
| `GET /api/v2/shop/auth_partner` | Implementado | Autorizacao da loja |
| `POST /api/v2/auth/token/get` | Implementado | Troca do codigo por tokens |
| `POST /api/v2/auth/access_token/get` | Implementado | Renovacao dos tokens |

## Assinatura HMAC-SHA256

### Endpoint publico

Base da assinatura:

```text
partner_id + api_path + timestamp
```

### Endpoint vinculado a loja

Base da assinatura:

```text
partner_id + api_path + timestamp + access_token + shop_id
```

- A `PartnerKey` e utilizada como chave do HMAC-SHA256.
- O resultado e serializado em hexadecimal minusculo.
- Cada chamada gera timestamp e assinatura novos.
- Os testes conferem as assinaturas contra calculos HMAC independentes.

## Pedidos, pagamentos e taxas

- A lista usa intervalo de criacao, cursor e pagina de ate 100 pedidos.
- Os detalhes sao buscados em lote por `order_sn_list`.
- Pedidos e itens sao normalizados para os contratos padrao do Core.
- Pagamentos incluem valor bruto, valor liquido, metodo, pagamento e liberacao.
- Taxas reconhecidas: comissao, servico, transacao e frete do vendedor.
- A sincronizacao percorre os cursores ate `more=false`.

## Rate limit e resiliencia

- Token bucket isolado por `shop_id`.
- Limite de 1.000 tokens por minuto por loja.
- Fila local de ate 1.000 requisicoes.
- Ate tres tentativas para HTTP 429 ou `error_too_many_request`.
- Erros de rate limit e HTTP 5xx sao classificados como transitorios.

## Configuracao necessaria

```text
Connectors__Shopee__PartnerId
Connectors__Shopee__PartnerKey
Connectors__Shopee__RedirectUri
Connectors__Shopee__BaseUrl
```

Callback local:

```text
https://localhost:7128/api/connectors/oauth/callback
```

Callback Azure:

```text
https://Avallo-web.purplemeadow-42f09588.brazilsouth.azurecontainerapps.io/api/connectors/oauth/callback
```

## Regra de liberacao

O card da Shopee deve permanecer bloqueado enquanto `PartnerId`, `PartnerKey`, sandbox e homologacao da Shopee BR nao estiverem validados. A liberacao para usuarios nao deve ocorrer apenas pela presenca do codigo.

## Criterio de aceite

O modulo sera considerado concluido quando uma loja sandbox brasileira concluir OAuth, sincronizar pedidos, detalhes, pagamentos e taxas assinados, renovar tokens automaticamente e operar dentro do limite de 1.000 requisicoes por minuto sem divergencias nos payloads da Shopee BR.

