# Auditoria de Conformidade — Projeto Avallo

**Repositório auditado:** `C:\Projetos\Avallo.Web` (commit-base `57531a0` + remediações locais verificadas em 12/08/2026)
**Padrão de referência:** Notion — *Projeto Avallo | Documentação* → *Arquitetura Geral* (Documento Técnico v2.0 de 31/05/2026 + Especificações e Requisitos)
**Data da auditoria:** 12/08/2026

---

## Veredito

**Conformidade global estimada após remediações: ~90%.**

O projeto está **substancialmente aderente ao espírito do documento** — a arquitetura modular com conectores plugáveis, o motor contábil de partidas dobradas, o funil de deduções e a imutabilidade de fechamento estão implementados com qualidade acima da média e cobertos por testes.

As principais lacunas funcionais identificadas nesta auditoria foram remediadas: há fluxo de avaria sem reembolso, recepção de webhooks, Balanço Patrimonial, gatilho conservador de distribuição de lucros, papel efetivo de contador e operação BPO em lote com carteira cross-tenant controlada. A principal lacuna remanescente do produto é a **assinatura digital externa/qualificada** (ICP-Brasil, ZapSign, Clicksign ou equivalente). O fechamento possui snapshot e hashes imutáveis, mas ainda não integra um provedor de assinatura com validade jurídica própria.

Há ainda **divergência total de stack tecnológica** frente à recomendada, não registrada formalmente em ADR.

---

## 1. Regras Inegociáveis (Seção 10 do documento)

| # | Regra | Status | Evidência |
|---|---|---|---|
| 1 | **Despesa sem comprovante** não pode ser deduzida | ✅ Conforme | `ExpenseEndpoints.cs:97` e `:114` — bloqueio em *submit* e em *approve* (`Attachments.Count == 0` → 400) |
| 2 | **Imutabilidade de fechamento** após assinatura | ✅ Conforme | `AppDbContext.cs:514-529` `GuardClosedPeriods()`; linhas 537-547 entidades *append-only*; `ThrowIfClosed` valida por intervalo de período |
| 2b | Cancelamento retroativo → mês corrente | ✅ Conforme | `AccountingEngine.CreateReversal` usa `timeProvider.GetUtcNow()` como `OccurredAt`, não a data original |
| 3 | **Core não conhece plataforma** (proibido `if platform == ml`) | ✅ Conforme no motor | Core referencia apenas `Avallo.Connectors.Abstractions` (`Avallo.Web.csproj:16`); plugins carregados por *AssemblyLoadContext* isolado |
| 3b | UI declarativa por plugin | ✅ Conforme | O branching nominal foi removido. `ConnectorDescriptor` fornece `ConnectorPresentation`, `IsConfigured` e `ConfigurationHint`; a página monta apresentação e prontidão apenas a partir desses metadados. `ConnectorPresentationTests.Ui_layer_never_mentions_a_marketplace_by_name` impede regressão nos componentes compartilhados e páginas auditadas. |
| 4 | **Precisão decimal explícita; `FLOAT` proibido** | ✅ Conforme por decisão arquitetural formalizada | Nenhum `float`/`double` representa valor financeiro. O ADR 0002 define `DECIMAL(18,2)` para dinheiro, `(18,4)` para quantidade, `(18,6)` para custo unitário e `(9,6)` para alíquota. A mudança preserva exatidão decimal, evita overflow de `(10,2)` e impede erro acumulado no CMV. A fonte normativa no Notion ainda deve incorporar a decisão já registrada. |
| 5 | **Tokens encriptados (AES-256), front nunca recebe token** | ✅ Conforme | `ConnectorGateway.cs:150-152` — ASP.NET Data Protection (AES-256-CBC + HMAC-SHA256), *purpose* segregado por tenant+conector; tokens nunca serializados para o cliente |

**4 de 5 regras críticas plenamente atendidas.** A quinta é divergência de especificação, não de segurança.

---

## 2. Núcleo do Sistema (Seção 02)

| Requisito | Status | Observação |
|---|---|---|
| Isolamento multi-tenant | ✅ **Conforme em duas camadas** | Mantém *global query filters* e guardas no `SaveChanges`, agora complementados por RLS nativo do PostgreSQL (`EnableRowLevelSecurity` + `TenantRlsConnectionInterceptor`). A variável de sessão `app.tenant_id` é publicada a cada conexão e as tabelas tenant-owned recebem policy com `USING` e `WITH CHECK`. |
| Acesso dedicado do contador | ✅ Conforme | `Roles.Accountant` permanece fora de `Writers`, mas integra `AccountingManagers` para executar somente validações contábeis/fiscais. |
| Onboarding fiscal via CNPJ | ✅ Conforme | `FiscalEndpoints.cs:47` via `BrasilApiCnpjClient` (proxy da Receita — aceitável); traz Razão Social, CNAE principal e secundários, endereço |
| Regime tributário por tenant | ✅ Conforme | `Domain/Fiscal.cs` — enum `TaxRegime`, `TaxProfile` com CNAE e alíquotas |
| Cálculo dinâmico de imposto por faixa | ✅ Conforme | `TaxEngine.cs` — regras com vigência (`EffectiveFrom`/`EffectiveTo`), base tributável, simulação |
| Painel / DRE consolidada | ✅ Conforme | `GET /api/accounting/dre`, página `/dre`, snapshots imutáveis por revisão |

---

## 3. Camada de Conectores (Seção 03)

| Requisito | Status | Observação |
|---|---|---|
| Interface padronizada | ✅ Conforme | `IMarketplaceConnector`, `IConnectorModule`, `ConnectorDescriptor.CredentialFields` |
| `gross_value` | ✅ | `StandardOrder.GrossValue` |
| `platform_fee` | ✅ | `StandardOrder.PlatformFee` + `StandardFeeCategory.MarketplaceCommission` |
| `payment_fee` | ✅ | `StandardPayment.PaymentFee` + categoria `PaymentProcessing` |
| `shipping_cost` | ✅ Conforme | `StandardPayment.ShippingCost` possui serialização explícita como `shipping_cost`. Mercado Livre preenche a partir de `shipping.cost`, Shopee de `seller_shipping_fee` e Amazon classifica as taxas de frete da SP-API. `ConnectorLayerTests` verifica todos os campos do split e o residual `gross - platform_fee - payment_fee - shipping_cost - net_value`. O razão continua usando `StandardFee/SellerShipping` para evitar dupla contabilização. |
| `net_value` | ✅ | `StandardOrder.NetValue` / `StandardPayment.NetValue` |
| Receita só reconhecida em `delivered` | ✅ Conforme | `AccountingEngine.cs:50` — reconhecimento condicionado a `FulfillmentStatus.Delivered`; `TaxEngine.cs:16` e `InventoryCostService.cs:194` idem |
| `cancelled`/`returned` → estorno automático | ✅ Conforme | `AccountingEngine.cs:47` + `CreateReversal` (receita bruta → conta `3.2.01 Cancelamentos e devoluções`); estoque restituído em `InventoryCostService` |
| **Avaria sem reembolso → Despesa com Perdas** | ✅ **Implementado** | Conta `4.2.02 LossExpenses`, movimento `DamageWriteOff` e lançamento débito em Despesa com Perdas / crédito em Estoque. O endpoint de avaria valida saldo e usa `OperationId` para idempotência. |
| **Webhooks de status das plataformas** (req. 3.5) | ✅ **Implementado com polling de contingência** | `MarketplaceWebhookEndpoints` recebe evento assinado, limita payload, valida HMAC SHA-256 em tempo constante e aplica janela anti-replay. O evento adquire lease e agenda sincronização imediata no Azure Service Bus; o polling foi preservado como fallback. Os secrets e as pontes específicas dos provedores precisam ser configurados no ambiente. |

---

## 4. Painel do Contador e Validação Legal (Seção 05) — **maior lacuna**

| Requisito | Status | Observação |
|---|---|---|
| Visão somente leitura para o contador | ✅ Conforme | `Roles.Accountant` fora de `Writers` |
| **Assinatura digital (PKI/ICP-Brasil, ZapSign, Clicksign, e-CPF/e-CNPJ)** | ❌ **Ausente** | Busca por `zapsign\|clicksign\|icp-brasil\|e-CPF\|e-CNPJ\|certificad\|assinatura digital` retorna **zero resultados** em todo o código |
| **Gatilho de liberação de saque / tratamento de IRPF** | ✅ **Implementado com trava do contador** | `LegalAccountingService` calcula um teto técnico conservador com lucro acumulado, caixa e passivos. O saque exige período fechado, balanço equilibrado, snapshot existente e autorização auditável do contador. O sistema não afirma isenção automaticamente: registra beneficiário, CPF/CNPJ, valor, tratamento tributário, confirmação expressa e fundamento legal. |
| **Balanço Patrimonial** | ✅ **Implementado** | O painel de fechamento apresenta Ativo, Passivo, Patrimônio Líquido, lucros acumulados, distribuições autorizadas e diferença de balanço, calculados sobre o razão até a data da competência. Diferença superior à tolerância bloqueia a liberação. |
| **Papéis de validação** | ✅ **Contradição resolvida explicitamente** | `Roles.AccountingManagers` contém `Admin` e `Contador`. O contador pode aprovar/rejeitar despesas e regras fiscais, conciliar movimentos, aprovar e fechar períodos e liberar distribuição. Continua sem acesso geral de escrita, gestão de usuários ou reabertura administrativa. `RolePermissionTests` foi atualizado para refletir a decisão. |

---

## 5. Custos, Despesas e CMV (Seção 04)

| Requisito | Status | Observação |
|---|---|---|
| Upload/parser de XML de NF-e de fornecedor | ✅ Conforme | `NfeXmlParser.cs`, limite 10 MB, XML original preservado no storage, estorno de NF-e suportado |
| Custo Médio Ponderado | ✅ Conforme | `InventoryCostService.cs:131-134`, precisão `(18,6)` |
| Baixa automática de CMV na entrega | ✅ Conforme | `ProcessDeliveredOrderAsync`, lançamento em `4.2.01 CMV`, idempotente por `EventKey` |
| Despesas bancárias via Open Finance **ou** OFX | ✅ Conforme (pela alternativa) | `StatementParser.cs` — OFX e CSV, até 5 MB, bloqueio de duplicata por SHA-256. **Open Finance não implementado**, mas o documento aceita OFX como equivalente |
| Contas a pagar (folha, energia, aluguel, internet) | ✅ Conforme | `ExpenseCategories` + `DueDate` + categorias customizadas por tenant |
| Storage de comprovantes (AWS S3 ou equivalente) | ✅ Conforme | Azure Blob Storage — o commit `57531a0` migrou de S3 para Azure. Documento diz "AWS S3 **(ou equivalente)**" |

---

## 6. Módulos de Marketplace (Seção 06) — **acima do previsto**

| Módulo | Fase prevista | Status |
|---|---|---|
| Mercado Livre — OAuth 2.0, `sale_fee` + `shipping_cost` | Fase 1 | ✅ Implementado, com rate limiter (100 req/min/token) e normalização UTC → America/Sao_Paulo |
| Shopee — OAuth 2.0 + assinatura HMAC-SHA256, rate limit agressivo | Fase 2 | ✅ Implementado, HMAC + limiter de 1.000 req/min/loja |
| Amazon — SP-API, AWS Signature V4, `/finances/v0/financialEvents` | Fase 4 | ✅ Implementado (`AmazonSignature.cs` com `AWS4-HMAC-SHA256`, endpoint exato do documento) |

Os três conectores estão prontos. O roadmap colocava Amazon na última fase — **está adiantado**.

---

## 7. Stack Tecnológica (Seção 07) — divergência integral

| Camada | Documento (recomendado) | Implementado | Avaliação |
|---|---|---|---|
| Frontend | Next.js (React) na Vercel | Blazor WebAssembly + MudBlazor | ⚠️ Substituído |
| Backend | Node.js + Fastify **ou** Python FastAPI | .NET 10 / ASP.NET Core Minimal APIs | ⚠️ Substituído |
| Banco + Auth | PostgreSQL + **Supabase Auth (RLS nativo)** | PostgreSQL 18 + EF Core + ASP.NET Identity + JWT + RLS nativo | ⚠️ Stack substituída, mas o requisito de isolamento no banco foi restaurado com policies PostgreSQL e contexto de sessão por tenant |
| Storage | AWS S3 | Azure Blob Storage | ✅ Equivalente |
| Assinatura digital | ZapSign / Clicksign | — | ❌ Ausente |
| Background jobs | BullMQ + Redis | Outbox no PostgreSQL + Azure Service Bus + leases | ✅ Equivalente ou superior |
| Hospedagem | Vercel / AWS | Azure Container Apps | ⚠️ Substituído |

O documento usa a palavra "**recomendada**", então a troca é legítima em princípio. Permanece um problema de **governança**: a substituição integral da stack (Node/React/Supabase/AWS → .NET/Blazor/Azure) ainda não está registrada em ADR. A lacuna técnica de RLS que agravava essa divergência foi corrigida; resta documentar formalmente a decisão, o modelo de ameaças e o procedimento operacional do role PostgreSQL dedicado.

---

## 8. Modelo de Negócio (Especificações, seção 2)

| Modelo | Status |
|---|---|
| **SaaS (facilitador)** — lojista convida contador externo | ✅ Suportado (`POST /api/users` com role `Contador`) |
| **BPO (serviço embutido)** — equipe interna revisa e fecha balanços **em lote** | ✅ **Implementado com carteira explícita.** Há painel global `/bpo`, papéis internos separados (`OperadorBPO` e `AdministradorBPO`), atribuições revogáveis por tenant e aprovação/fechamento de até 100 competências por lote. Cada empresa é processada em escopo RLS próprio e cada resultado é auditado individualmente. A assinatura digital externa continua pendente. |

---

## 9. Banco de Dados (Seção 08)

Todas as tabelas exigidas têm equivalente: `tenants` ✅ · `tax_profiles` ✅ · `orders` ✅ (`MarketplaceOrders`, chave `tenant + platform + order_id`, idempotente) · `inventory_items` ✅ · `expenses` ✅ · `expense_attachments` ✅ · `dre_reports` ✅ (snapshots versionados e *append-only*).

O modelo real é **mais rico** que o especificado: razão contábil de partidas dobradas (`AccountingEntries`/`Postings`), plano de contas estruturado, conciliação financeira e outbox de notificações.

---

## 10. Higiene de documentação

- 🔴 O `README.md` referencia `docs/ARCHITECTURE.md` e `docs/adr/0001-modular-monolith.md`. **Nenhum dos dois existe.** A pasta `docs/` contém apenas `DEPLOYMENT-STRATEGY.md`. Links quebrados na documentação de arquitetura — e ausência do ADR que justificaria a troca de stack.
- ⚠️ O `README.md` documenta o contrato do conector com apenas `platform_fee` e `net_value` e status `paid|pending|cancelled`, mas o código já evoluiu para incluir `fulfillment_status`, `delivered_at`, `payment_fee` e categorias de taxa. **Documentação desatualizada em relação ao código.**
- ⚠️ Histórico Git com apenas 2 commits (`Initial commit` + migração de storage). Sem rastreabilidade de decisões.

---

## Plano de ação priorizado

### 🔴 Bloqueador remanescente do valor de negócio
1. **Implementar assinatura digital** (ZapSign, Clicksign ou ICP-Brasil A1/A3) sobre os snapshots contábeis. O hash e a imutabilidade já existem, assim como a autorização do contador e o processamento BPO, mas ainda falta evidência criptográfica emitida/validada por um provedor de assinatura.

### 🟠 Riscos técnicos e operacionais
2. **Concluir a configuração produtiva dos webhooks** por provedor: cadastrar URLs/secrets, configurar a ponte SQS/EventBridge da Amazon e validar entregas/retries em ambiente real.
3. **Provisionar as identidades internas BPO e suas carteiras** por processo administrativo controlado; os papéis BPO não são oferecidos na tela de usuários dos clientes.
4. **Aplicar e validar as migrations de RLS em staging**, usando o role dedicado `Avallo_app`, antes do rollout em produção.

### 🟡 Governança
5. **Escrever `docs/ARCHITECTURE.md` e o `ADR 0001`** (já referenciados e inexistentes), incluindo um **ADR novo justificando a troca de stack** .NET/Blazor/Azure vs. a recomendação Node/Next/Supabase/AWS.
6. **Atualizar o documento do Notion** quanto a `DECIMAL(10,2)` → `DECIMAL(18,2)`, ao contrato ampliado do conector, ao modelo BPO e às regras tributárias vigentes para lucros e dividendos em 2026.

---

## Fontes

- [Documento Técnico de Arquitetura Geral — Notion](https://jeffpro23.notion.site/Documento-T-cnico-de-Arquitetura-Geral-372367da46e8808fa147d37305ec5936)
- [Especificações e Requisitos — Notion](https://jeffpro23.notion.site/Especifica-es-e-Requisitos-372367da46e880f78841f8810f693de0)
- [Projeto Avallo | Documentação — Notion](https://jeffpro23.notion.site/Projeto-Avallo-Documenta-o-372367da46e880d1bdc1fdc7f9f43781)
- Código-fonte: `computer:///C:/Projetos/Avallo.Web`
