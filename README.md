# Avallo

Base de autenticacao multi-tenant em .NET 10 e PostgreSQL 18.

## Aviso de deploy em tempo real

O endpoint administrativo `POST /api/system/deployment-notice` transmite pelo
SignalR um aviso global com contagem regressiva. Em multiplas replicas o aviso
usa backplane distribuido: `AzureSignalR__ConnectionString` (Azure SignalR
Service) quando configurado, senao o Redis de `AzureRedis__ConnectionString`.
Sem nenhum dos dois, o broadcast alcanca apenas os clientes da replica que
disparou o aviso. O script abaixo avisa os clientes, aguarda o prazo e somente
depois atualiza o Azure Container App:

```powershell
$env:BRASSELLER_ADMIN_TOKEN = '<jwt-admin>'
.\scripts\deploy-azure.ps1 -Version '20260729.7'
```

Componentes que precisam persistir rascunhos antes da recarga podem escutar o
evento JavaScript `nucleo:deployment`.

## Arquitetura

O Avallo evolui como um monolito modular: fatias verticais por capacidade de
negocio, dependencias orientadas para o dominio e workers assincronos para cargas
longas ou sujeitas a picos. A comparacao das alternativas, regras de dependencia,
estrutura-alvo e gatilhos para extracao de servicos estao em
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). A decisao esta registrada no
[`ADR 0001`](docs/adr/0001-modular-monolith.md).

## Seguranca

- ASP.NET Core Identity para senha, lockout e papeis.
- JWT HMAC-SHA256 com validade de 10 minutos.
- Refresh token aleatorio de 512 bits, armazenado apenas como SHA-256 no banco.
- Rotacao de refresh token e deteccao de reutilizacao, com cookie `HttpOnly`, `Secure` e `SameSite=Strict`.
- Tenant obtido exclusivamente da claim assinada `tenant_id`; o cliente nunca escolhe o tenant de uma consulta.
- Filtro global para toda entidade que implementa `ITenantEntity` e bloqueio adicional de escrita no `SaveChanges`.
- **Row Level Security no PostgreSQL** em todas as 31 tabelas com `TenantId`, como segunda camada independente da aplicacao.
- `Admin` gerencia usuarios e revisoes contabeis, `Admin` e `Vendedor` podem escrever, `Contador` usa somente endpoints de leitura e download.

O cadastro inicial cria um tenant e seu usuario `Admin`. O endpoint administrativo de usuarios permite criar acessos `Vendedor` ou `Contador` dentro do mesmo tenant.

### Isolamento por tenant no banco (RLS)

Os filtros globais do EF Core protegem o que passa pelo `AppDbContext`. A RLS protege o resto:
SQL cru, script de manutencao, job externo ou uma consulta que esqueca o filtro. Sao duas
camadas independentes, e a de baixo nao depende de ninguem lembrar de nada.

Cada tabela com `TenantId` tem a policy `tenant_isolation`, que compara a coluna com a
variavel de sessao `app.tenant_id`. O `TenantRlsConnectionInterceptor` publica essa variavel
a cada abertura de conexao. Sem tenant no contexto a variavel vai vazia e nenhuma linha casa:
o padrao e negar.

**A aplicacao precisa conectar com um role que nao seja dono do schema.** O dono ignora as
policies — se a app conectar como `postgres`, a RLS fica inerte. Dai a separacao:

| Conexao | Credencial | Para que |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Avallo_app` | Runtime. Sem DDL, sujeita as policies. |
| `ConnectionStrings:MigrationConnection` | dono do schema (`postgres`) | Migrations e DDL. Ignora as policies, como deve. |

Local, o `setup-local.ps1` ja cria o role. Manualmente:

```powershell
Get-Content scripts/sql/create-app-role.sql -Raw |
  docker exec -i avallo-postgres psql -U postgres -d Avallo -v app_password='Avallo_app' -f -
```

Em producao, rode o mesmo script uma vez com uma senha forte e defina os dois segredos:

```text
ConnectionStrings__DefaultConnection    -> Username=Avallo_app
ConnectionStrings__MigrationConnection  -> credencial dona do schema
```

Enquanto `MigrationConnection` nao existir, migrations caem na conexao padrao (comportamento
anterior). Enquanto a aplicacao continuar conectando como dona, a RLS fica inerte e a protecao
segue sendo so a do EF — **a migration nao quebra nada, mas tambem nao protege nada ate a
credencial ser trocada.**

`AspNetUsers` e `RefreshTokens` ficam fora da RLS de proposito: sao consultadas no login e no
refresh, antes de existir um tenant conhecido. Continuam cobertas pelos filtros do EF, e o
refresh token e protegido pela propria entropia de 512 bits. `Tenants` tambem fica fora, por
nao ter `TenantId` e ser usada na descoberta de tenants pelos workers.

Para conferir que a RLS esta valendo de fato:

```sql
-- como Avallo_app, sem tenant definido: deve retornar 0
SELECT count(*) FROM "FinancialEntries";

-- com um tenant: deve retornar apenas os lancamentos dele
SELECT set_config('app.tenant_id', '<uuid-do-tenant>', false);
SELECT count(*) FROM "FinancialEntries";
```

Se a primeira consulta retornar linhas, a aplicacao esta conectando como dona do schema.

## Executar localmente

### Pre-requisitos

- .NET SDK 10
- Docker Desktop com Docker Compose
- PowerShell 7 ou Windows PowerShell

O ambiente local usa PostgreSQL na porta `5432` e MinIO (object storage S3-compativel) nas portas `9000` (API) e `9001` (console web, login `avallo`/`avallo-dev-key`). Os dados ficam em volumes Docker e sobrevivem a reinicializacoes.

Com o Docker Desktop aberto, prepare todo o ambiente:

```powershell
.\scripts\setup-local.ps1
```

Para preparar e iniciar tudo em um único comando:

```powershell
.\scripts\dev.ps1 -TrustHttpsCertificate
```

Durante o desenvolvimento, use o modo watch:

```powershell
.\scripts\dev.ps1 -Watch
```

Para recriar o banco e o storage locais, removendo os volumes Docker:

```powershell
.\scripts\dev.ps1 -ResetData -TrustHttpsCertificate
```

Na primeira execucao HTTPS, confie no certificado local caso o navegador apresente um aviso:

```powershell
dotnet dev-certs https --trust
```

### Iniciar a aplicacao

Use o inicializador seguro abaixo. Ele verifica as portas antes de iniciar e evita abrir duas instancias simultaneas:

```powershell
.\scripts\run-local.ps1
```

O script aceita `-Profile http|https`, `-Watch` e `-NoBuild`. Para parar os
serviços sem apagar dados, use `.\scripts\stop-local.ps1`; para remover também
os volumes, use `.\scripts\stop-local.ps1 -RemoveData`.

O comando .NET executado pelo script e:

```powershell
dotnet run --project Avallo.Web --launch-profile https
```

Acesse `https://localhost:7128`. O Swagger fica em `https://localhost:7128/swagger`.

Para encerrar, pressione `Ctrl+C` no mesmo terminal antes de iniciar uma nova instancia. Isso libera as portas `7128` e `5152` e evita o erro `address already in use`.

As migrations sao aplicadas automaticamente em `Development`. O bucket `uploads` do MinIO e criado no startup e armazena notas fiscais, comprovantes e o key ring do Data Protection. Os perfis `http` e `https` fixam banco e storage locais, impedindo que um user-secret antigo direcione o F5 para recursos de producao. Google, Mercado Livre, Shopee e SMTP sao opcionais: a aplicacao inicia normalmente sem essas credenciais.

Comandos uteis:

```powershell
# Ver os servicos locais
docker compose -f compose.local.yml ps

# Parar sem apagar os dados
docker compose -f compose.local.yml stop

# Iniciar novamente
docker compose -f compose.local.yml start

# Acompanhar os logs
docker compose -f compose.local.yml logs -f
```

Para usar outro PostgreSQL ou storage, execute sem launch profile e sobrescreva os valores com user-secrets, sem alterar arquivos versionados:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=Avallo;Username=postgres;Password=SUA_SENHA" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "ObjectStorage:Provider" "azure" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "ObjectStorage:ConnectionString" "SUA_CONNECTION_STRING" --project "Avallo.Web/Avallo.Web.csproj"
dotnet run --project Avallo.Web --no-launch-profile
```

O object storage e selecionado por `ObjectStorage:Provider`: `s3` usa o MinIO local (ou qualquer endpoint S3-compativel via `ServiceUrl`/`AccessKey`/`SecretKey`); `azure` usa Azure Blob Storage via `ConnectionString` — recomendado para producao. `ObjectStorage:ContainerName` vale como nome do container Azure ou do bucket S3.

Em producao, use um job de deploy com `dotnet ef database update` e mantenha `Database__ApplyMigrations=false`.

Segredos de producao devem ser definidos pelo provedor de secrets:

```text
ConnectionStrings__DefaultConnection
Jwt__Key
Authentication__Google__ClientId
Authentication__Google__ClientSecret
```

Em hospedagem atrás de proxy ou ingress (ex.: Azure Container Apps), defina também `ForwardedHeaders__TrustAnyProxy=true`. Sem isso o app ignora `X-Forwarded-For`, o `RemoteIpAddress` fica sempre com o IP do proxy e o rate limiting por IP vira um bucket global para todos os usuários.

No Google Cloud Console, configure a URI autorizada de redirecionamento como `https://SEU_HOST/signin-google`.

## API

| Metodo | Rota | Acesso |
|---|---|---|
| POST | `/api/auth/register` | Publico |
| POST | `/api/auth/login` | Publico |
| POST | `/api/auth/refresh` | Cookie de refresh |
| POST | `/api/auth/logout` | Cookie de refresh |
| GET | `/api/auth/google?tenantName=...` | Publico |
| GET | `/api/auth/me` | Admin, Vendedor, Contador |
| POST | `/api/users` | Admin |

Para `/api/users`, informe `role` como `Vendedor` ou `Contador`. Endpoints de escrita de dados do negocio devem exigir a policy `CanWrite`; endpoints de leitura devem exigir `TenantMember`.

## Assistente Azure AI

O assistente conversacional fica disponível em `/assistant` e usa o Azure AI
somente para respostas de conversa. Nesta primeira versão ele não possui
ferramentas, não executa alterações e não acessa o banco diretamente.

Configure o endpoint e o deployment do modelo por user-secrets no ambiente local:

```powershell
az login
dotnet user-secrets set "AzureAI:Endpoint" "https://SEU-RECURSO.services.ai.azure.com/openai/v1" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "AzureAI:Deployment" "Phi-4-mini-instruct" --project "Avallo.Web/Avallo.Web.csproj"
```

Ou configure o endpoint e o deployment usando o login do Azure CLI:

```powershell
.\scripts\configure-ai-local.ps1
```

Em produção, configure a identidade gerenciada do Container App com permissão
de inferência no projeto/modelo e use:

```text
AzureAI__Endpoint
AzureAI__Deployment
```

O endpoint `POST /api/ai/chat` exige autenticação e mantém o isolamento pelo
tenant do JWT. O token é obtido pelo `DefaultAzureCredential` e nunca é enviado
ao navegador.

O administrador gerencia esses acessos em `/users`. Novos usuarios recebem uma senha temporaria e nao podem acessar os modulos do tenant antes de substitui-la no primeiro login. Ao desativar um acesso, JWTs sao invalidados pelo security stamp e refresh tokens ativos sao revogados.

## Validacao

```powershell
dotnet build Avallo.slnx -c Release
dotnet test Avallo.slnx
```

### Teste de carga (NBomber)

O projeto `Avallo.LoadTests` semeia N tenants pelo endpoint real `POST /api/auth/register` (cada registro cria tenant + admin isolado por RLS), autentica cada usuario virtual em `POST /api/auth/login` e navega nos endpoints de leitura (`/api/reports/dashboard`, `/api/reports/entries`, `/api/expenses`, `/api/auth/me`).

Com o stack local no ar (`docker compose -f compose.local.yml up -d`) e a aplicacao rodando em `http://localhost:5152` com o limite de autenticacao relaxado para o benchmark:

```powershell
$env:RateLimiting__AuthPermitLimit = "100000"   # nao usar em producao
dotnet run --project Avallo.Web --launch-profile http              # terminal 1
dotnet run --project Avallo.LoadTests -- --users 500 --minutes 2 --ramp-seconds 60   # terminal 2
```

Parametros: `--url` (padrao `http://localhost:5152`), `--users` (padrao 500), `--minutes` de carga sustentada, `--ramp-seconds` de subida e `--seed-parallel` (grau de paralelismo do seed). Relatorios HTML/CSV/MD sao gravados em `load-test-results/`. O seed e idempotente — em reexecucoes, usuarios ja existentes passam pelo login.

O modo `--rps N` usa injecao de taxa (modelo aberto) em vez de N usuarios concorrentes — util para achar o teto de throughput, mas o gerador esgota as ~16k portas efemeras do Windows em taxas altas com timeouts.

Em ambiente local o Postgres precisa de folga de conexoes: o compose sobe com `max_connections=500`, pois o default (100) rejeitava conexoes do pool Npgsql sob 500 usuarios (`53300`). Em producao, dimensione `Maximum Pool Size` na connection string ou use PgBouncer ao escalar replicas.

Capacidade observada nesta estacao (app + Postgres + gerador na mesma maquina, `Maximum Pool Size=300`): 500 VUs → 3,6k RPS com p99=308ms; 2.000 VUs → 2,9k RPS com p99=1,7s; 10.000 VUs → 1,75k RPS com p99=30s (saturado, porém sem erros). A zona confortavel por replica e ~500 VUs; alem disso o sistema enfileira sem falhar. Nota: `launchSettings.json` define a connection string no perfil e sobrescreve env vars herdadas — ajustes de pool devem ser feitos ali ou em `appsettings.Development.json`.

## Exportacao de relatorios

O endpoint `GET /api/reports/export` usa os mesmos filtros tenant-aware do painel e aceita:

- `format=pdf|xlsx|csv`
- `mode=consolidated|platform`
- `from`, `to`, `platform`, `paymentMethod` e `status`

O PDF inclui resumo mensal e layout para conferencia contabil. O Excel inclui uma aba consolidada e uma aba para cada marketplace. O CSV usa UTF-8 com BOM, separador `;` e valores monetarios em formato decimal invariavel.

Toda renderizacao passa por `IReportExportEngine`. Modulos de negocio devem montar um documento tipado e delegar a geracao ao motor; nao devem referenciar QuestPDF, ClosedXML ou implementar serializacao CSV diretamente. O fechamento mensal tambem utiliza essa fronteira para produzir seu PDF contabil imutavel.

O projeto configura `Reports:QuestPdfLicense` como `Community`. Antes de uso comercial, confirme a elegibilidade nos termos do QuestPDF ou altere essa configuracao para a licenca adquirida.

## Notificacoes e e-mail

A central usa uma outbox persistente por tenant. Fechamentos mensais, alertas de liberacao do Mercado Livre, novas vendas habilitadas pelo usuario e relatorios semanais do contador sao deduplicados e processados em background. Falhas SMTP usam retry exponencial e permanecem registradas.

Configure o SMTP por secrets ou variaveis de ambiente:

```text
Email__Enabled=true
Email__Host=smtp.seu-provedor.com
Email__Port=587
Email__Username=...
Email__Password=...
Email__FromEmail=notificacoes@seu-dominio.com
Email__FromName=Nucleo
Email__Security=StartTls
```

Valores aceitos para `Email__Security` incluem `StartTls`, `SslOnConnect` e `None`. Mantenha `StartTls` ou `SslOnConnect` em producao. Com o envio desabilitado, notificacoes e e-mails continuam na outbox e nao sao perdidos.

O worker de notificacoes exige `Notifications__Worker__Enabled=true` e
`AzureCommunicationEmail__Enabled=true`; caso contrario, permanece ocioso. Novos
anexos de e-mail sao gravados no object storage quando `ObjectStorage__Enabled`
esta ativo (MinIO/S3 em dev, Azure Blob em producao), e linhas antigas com
`AttachmentContent` continuam compativeis.

Para escalar workers, execute varias replicas da aplicacao: o processamento da
outbox usa leases no PostgreSQL e a sincronizacao de marketplace usa leases por
conexao e mensagens do Azure Service Bus. Ajuste `Notifications__Worker__BatchSize`
e `IntervalMinutes` conforme a vazao; para marketplace, use `MaxConcurrentCalls`
no codigo do processor e o controle de concorrencia/retentativa do Service Bus.
Mantenha uma unica fila compartilhada e monitore idade da outbox, mensagens
dead-letter e expiracao de leases.

Alem de webhooks e do endpoint manual, a sincronizacao de marketplace tem um
agendador interno (`MarketplaceSyncSchedulerWorker`): a cada
`MarketplaceSyncScheduler__IntervalMinutes` (padrao 15) ele varre as conexoes
ativas de todos os tenants cuja `LastSyncAt` e mais antiga que o intervalo —
ou nula, usando `InitialLookbackHours` (padrao 24h) como janela inicial — e
dispara o sync pela fila do Service Bus quando configurada, ou inline caso
contrario. `OverlapMinutes` ressincroniza alguns minutos anteriores ao ultimo
`LastSyncAt` para absorver atrasos de ordenacao do marketplace. O lease por
conexao evita disparo duplo entre replicas e o `TriggerId` deduplica a
mensagem na fila dentro do mesmo intervalo. Desative com
`MarketplaceSyncScheduler__Enabled=false`.

A listagem `GET /api/expenses` aceita `page` e `pageSize` (padrao 500, maximo
500) e devolve o total em `X-Total-Count`; sem parametros continua retornando
o array completo, mas limitado a 500 itens por pagina.

## SDK de conectores

`Avallo.Connectors.Abstractions` e o unico projeto que um modulo de marketplace deve referenciar. O Core nao possui referencia para Mercado Livre, Shopee, Amazon ou qualquer implementacao concreta.

Um plugin deve implementar:

- `IMarketplaceConnector` com autenticacao, refresh, pedidos, detalhes, pagamentos, taxas, NF opcional, sincronizacao e status.
- `IConnectorModule` para registrar o conector e suas dependencias no DI.
- `ConnectorDescriptor.CredentialFields` para a interface montar o formulario sem conhecer a plataforma.
- `ConnectorDescriptor.Presentation` com tagline, logo, textos de banner, cores e som. E o que permite a interface renderizar um marketplace que ela nao conhece.
- `ConnectorDescriptor.IsConfigured` e `ConfigurationHint`, calculados a partir das options do proprio plugin. A tela mostra o conector como pendente e explica o que falta, sem nenhuma regra por plataforma.

O plugin, nomeado como `Avallo.Connector.{Marketplace}.dll`, e publicado na pasta configurada em `Connectors:PluginPath`, por padrao `Avallo.Web/connectors`. No startup, o loader isolado descobre o modulo automaticamente.

Para um projeto novo no repositorio, basta declarar `<IsConnectorPlugin>true</IsConnectorPlugin>`: o alvo `PublishConnectorPlugin` em `Directory.Build.targets` copia o DLL, o pdb e o deps.json para a pasta a cada build. O `Avallo.Web.csproj` referencia os conectores apenas com `ReferenceOutputAssembly="false"`, o que garante ordem de build sem trazer nenhum tipo concreto de marketplace para o assembly do Core. **Nao adicione um `ProjectReference` normal do plugin ao Core** — o teste `Core_assembly_has_no_compile_time_dependency_on_any_marketplace` falha se isso acontecer.

Somente o assembly do plugin vai para a pasta. Contrato, DI, configuracao e demais pacotes que o Core ja possui sao resolvidos a partir do host: duplica-los na pasta quebraria a identidade de tipo e o modulo deixaria de implementar `IConnectorModule`. Se o seu plugin depender de um pacote que o Core nao tem, publique tambem o DLL dessa dependencia.

A pasta `connectors/` e saida de build e esta no `.gitignore`. Se voce tem DLLs antigas versionadas ali, remova-as (`git rm -r --cached Avallo.Web/connectors` e apague a pasta) antes do primeiro build: um plugin compilado contra outra versao de `Avallo.Connectors.Abstractions` faz o startup falhar em `Development` com a mensagem do `ConnectorPluginExtensions`.

Todos os pedidos devem retornar `StandardOrder`. Na API, o contrato usa `order_id`, `platform`, `date`, `gross_value`, `platform_fee`, `net_value`, `payment_method`, `payment_date`, `release_date`, `status`, `buyer_name`, `items`, `invoice_number`, `fulfillment_status` e `delivered_at`. Status sao serializados como `paid`, `pending` ou `cancelled`; `fulfillment_status` como `unknown`, `pending`, `shipped`, `delivered` ou `returned`.

`GetPaymentsAsync` devolve o split financeiro exato, cada vazamento em campo proprio:

| Campo | Natureza contabil |
|---|---|
| `gross_value` | Receita — valor bruto pago pelo cliente |
| `platform_fee` | Despesa de venda — comissao do marketplace |
| `payment_fee` | Despesa financeira — taxa de gateway/cartao |
| `shipping_cost` | Despesa comercial — frete retido do seller |
| `net_value` | Valor liquido liberado para repasse |

`split_residual` e calculado (`gross - platform - payment - shipping - net`) e deve fechar em zero quando a plataforma informa todos os campos; diferente de zero indica taxa nao mapeada pelo conector.

Esse split e o que a **plataforma declara**, guardado por pagamento para conciliacao e auditoria. O **razao contabil nao usa esses campos**: ele e montado a partir de `GetFeesAsync`, que traz cada taxa individualizada com a sua `StandardFeeCategory`. Lancar as duas fontes levaria a contagem dobrada — se voce adicionar um lancamento, adicione em `GetFeesAsync`.

Tokens sao criptografados com ASP.NET Core Data Protection usando finalidades separadas por tenant e conector. Em producao com mais de uma instancia, persista e compartilhe o key ring do Data Protection; perder essas chaves exige reconectar os marketplaces.

## Conciliacao financeira

O modulo em `/reconciliation` importa extratos OFX ou CSV de ate 5 MB. Para CSV, use cabecalhos equivalentes a `Data`, `Valor`, `Descricao` e, opcionalmente, `Id` ou `Referencia`. O formato monetario brasileiro e aceito.

O motor sugere pagamentos por referencia, valor e proximidade da data de liberacao. Admin e Contador podem confirmar um credito contra um ou varios pagamentos ou ignora-lo com justificativa. A confirmacao gera um lancamento contabil imutavel, debitando `Bancos (1.1.01)` e creditando `Valores a receber de marketplaces (1.1.02)`.

Arquivos repetidos sao bloqueados por SHA-256, os originais sao preservados no storage e creditos/repasses pendentes impedem o fechamento mensal. Transacoes de meses fechados so podem ser importadas depois da reabertura formal da competencia.

`syncAll` e idempotente por `tenant + platform + order_id`, salva pedido e itens normalizados e atualiza o lancamento financeiro que alimenta painel, relatorios, exportacoes e notificacoes.

## Conector Mercado Livre

O plugin `Avallo.Connector.MercadoLivre` implementa OAuth 2.0, refresh de token, pedidos, detalhes, pagamentos, taxas, status e sincronizacao completa. Datas recebidas em UTC sao normalizadas para `America/Sao_Paulo`. O rate limiter aplica no maximo 100 requisicoes por minuto para cada token, respeitando o limite de 6.000 por hora.

Crie um aplicativo em `developers.mercadolivre.com.br` e configure exatamente esta redirect URI no ambiente local:

```text
https://localhost:7128/api/connectors/oauth/callback
```

Armazene as credenciais com user-secrets:

```powershell
dotnet user-secrets set "Connectors:MercadoLivre:ClientId" "SEU_APP_ID" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "Connectors:MercadoLivre:ClientSecret" "SEU_CLIENT_SECRET" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "Connectors:MercadoLivre:RedirectUri" "https://localhost:7128/api/connectors/oauth/callback" --project "Avallo.Web/Avallo.Web.csproj"
```

Depois, acesse `/connectors` e selecione **Conectar** no Mercado Livre. Em producao, troque a redirect URI para o dominio publico HTTPS e configure corretamente os forwarded headers do proxy.

## Conector Shopee

O modulo `Avallo.Connector.Shopee` implementa autorizacao de loja, troca e renovacao de tokens, consulta de loja, pedidos, detalhes, pagamentos, taxas e sincronizacao completa. Todas as chamadas sao assinadas com HMAC-SHA256; o rate limiter local respeita o teto de 1.000 requisicoes por minuto por loja.

Solicite as credenciais de parceiro na Shopee Open Platform e cadastre a callback:

```text
https://Avallo-web.purplemeadow-42f09588.brazilsouth.azurecontainerapps.io/api/connectors/oauth/callback
```

Configuracao local, usando primeiro as credenciais e a URL base do sandbox fornecidas pela Shopee:

```powershell
dotnet user-secrets set "Connectors:Shopee:PartnerId" "SEU_PARTNER_ID" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "Connectors:Shopee:PartnerKey" "SUA_PARTNER_KEY" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "Connectors:Shopee:RedirectUri" "https://localhost:7128/api/connectors/oauth/callback" --project "Avallo.Web/Avallo.Web.csproj"
dotnet user-secrets set "Connectors:Shopee:BaseUrl" "URL_BASE_DO_SANDBOX" --project "Avallo.Web/Avallo.Web.csproj"
```

No Azure, use `Connectors__Shopee__PartnerId`, `Connectors__Shopee__PartnerKey`, `Connectors__Shopee__RedirectUri` e `Connectors__Shopee__BaseUrl`. Mantenha `PartnerKey` em um segredo do Container App.
