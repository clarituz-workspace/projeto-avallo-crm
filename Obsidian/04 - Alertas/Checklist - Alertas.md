---
tags:
  - avallo
  - alertas
  - notificacoes
  - email
  - outbox
status: concluido
ultima-revisao: 2026-08-10
---

# Alertas

> Sistema centralizado de comunicacao com o usuario, com notificacoes internas, preferencias individuais e entrega de e-mail por outbox.

## Checklist funcional

- [x] E-mail automatico de fechamento mensal com resumo
- [x] Alerta quando pagamento do Mercado Livre esta proximo de liberar
- [x] Notificacao opcional de nova venda, ativavel pelo usuario
- [x] Relatorio semanal automatico enviado ao contador

## Infraestrutura centralizada

- [x] Central de notificacoes dentro do sistema
- [x] Preferencias configuraveis por usuario
- [x] Outbox persistente para envio de e-mails
- [x] Deduplicacao por usuario e chave do evento
- [x] Worker em background para agendamento e envio
- [x] Retry exponencial para falhas do provedor de e-mail
- [x] Sender Azure Communication Services Email integrado ao worker
- [x] Isolamento das notificacoes por tenant
- [x] Anexos persistidos na outbox quando aplicavel

## Evidencias da implementacao

### Fechamento mensal

- O scheduler calcula o periodo mensal anterior.
- Consolida faturado, recebido e taxas dos lancamentos financeiros.
- Envia notificacao interna para Admin e Vendedor.
- Gera e-mail com resumo quando a preferencia `MonthlyCloseEmail` esta habilitada.
- A chave `monthly:AAAA-MM` impede envios duplicados para o mesmo usuario.

### Liberacao do Mercado Livre

- Localiza pagamentos do Mercado Livre ainda nao recebidos.
- Considera a data prevista de liberacao e o numero de dias configurado pelo usuario.
- A antecedencia aceita valores entre 1 e 7 dias; o padrao e 2 dias.
- Informa venda, valor a receber e data prevista.
- A preferencia `MercadoLivreReleaseAlert` permite desativar o alerta.

### Nova venda

- A notificacao e opcional e permanece desativada por padrao.
- O usuario pode ativa-la pela central de preferencias.
- Admin e Vendedor habilitados recebem notificacao interna e e-mail.
- O evento identifica marketplace, pedido, descricao e valor da venda.
- A chave da venda impede notificacoes repetidas do mesmo pedido.

### Relatorio semanal do contador

- Seleciona apenas usuarios ativos com papel `Contador`.
- Calcula automaticamente a semana anterior, de segunda-feira a domingo.
- Utiliza `ReportExportService` e o motor compartilhado `IReportExportEngine`.
- Gera PDF consolidado com os dados de todos os marketplaces do tenant.
- Anexa o PDF ao e-mail enviado pela outbox.
- A preferencia `WeeklyAccountantReport` permite desativar o recebimento.
- A chave semanal impede duplicidade de envio.

## Preferencias disponiveis

| Preferencia | Padrao | Destinatarios |
|---|---:|---|
| Fechamento mensal | Ativada | Admin e Vendedor |
| Liberacao Mercado Livre | Ativada | Admin e Vendedor |
| Nova venda | Desativada | Admin e Vendedor |
| Relatorio semanal | Ativada | Contador |

## Fluxo de entrega

1. O `NotificationScheduler` identifica os eventos aplicaveis ao tenant.
2. O `NotificationDispatchService` verifica a chave de deduplicacao.
3. A notificacao interna e persistida.
4. Quando o e-mail esta habilitado, uma entrada e criada em `EmailOutbox`.
5. O `NotificationWorker` processa a outbox.
6. O `AzureCommunicationEmailSender` realiza a entrega ou agenda uma nova tentativa.

## Dependencia operacional

O envio em producao usa Azure Communication Services Email e depende de:

- `AzureCommunicationEmail__Enabled=true`
- `AzureCommunicationEmail__ConnectionString` configurada como secret do Container App
- `AzureCommunicationEmail__SenderAddress` pertencente a um dominio verificado

Com o provedor desabilitado, as notificacoes internas continuam funcionando e os eventos permanecem registrados na outbox.

As credenciais do recurso Azure devem ser rotacionadas caso sejam expostas fora do Azure Key Vault ou dos secrets do Container App.

## Criterio de aceite

O modulo e considerado concluido quando cada evento gera no maximo uma notificacao por usuario, respeita tenant e preferencias, persiste e-mails antes do envio e entrega o relatorio semanal consolidado somente aos contadores habilitados.
