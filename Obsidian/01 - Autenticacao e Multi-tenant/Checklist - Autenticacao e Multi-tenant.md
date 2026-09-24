---
tags:
  - avallo
  - autenticacao
  - multi-tenant
  - seguranca
status: concluido
ultima-revisao: 2026-08-03
---

# Autenticacao e Multi-tenant

> Cada vendedor possui uma conta isolada. Os dados de um cliente nunca devem ser visiveis para outro tenant.

## Checklist funcional

- [x] Cadastro com e-mail e senha
- [x] Login com e-mail e senha
- [x] OAuth com Google
- [x] Isolamento completo de dados por tenant (vendedor)
- [x] Controle de sessao com access token JWT
- [x] Refresh token com rotacao e revogacao
- [x] Criacao de acesso secundario para o contador
- [x] Restricao do Contador para operacoes somente de leitura
- [x] Controle de permissoes por papel: Admin, Vendedor e Contador

## Evidencias da implementacao

### Cadastro, login e Google

- ASP.NET Core Identity gerencia usuarios, senhas, lockout e papeis.
- `POST /api/auth/register` cria o tenant e seu primeiro usuario Admin.
- `POST /api/auth/login` autentica por e-mail e senha.
- `GET /api/auth/google` e o callback associado realizam cadastro/login pelo Google.
- As credenciais Google permanecem externas ao codigo e sao carregadas pela configuracao do ambiente.

### Isolamento por tenant

- Entidades de negocio implementam `ITenantEntity` e armazenam `TenantId`.
- O tenant ativo e obtido da claim JWT assinada `tenant_id`.
- O `AppDbContext` aplica filtros globais de consulta para entidades tenant-aware.
- O `SaveChanges` valida inclusoes e alteracoes para impedir escrita em outro tenant.
- Endpoints protegidos exigem a policy `TenantMember` ou uma policy mais restritiva.

### Sessao e tokens

- Access tokens usam JWT assinado e possuem validade curta.
- Refresh tokens sao aleatorios, persistidos como hash e enviados em cookie HttpOnly.
- A renovacao rotaciona o refresh token.
- Logout, desativacao de usuario e deteccao de reutilizacao revogam sessoes aplicaveis.

### Acessos secundarios e papeis

- Apenas Admin pode listar, criar, ativar ou desativar acessos do tenant.
- O Admin pode criar usuarios com papel `Vendedor` ou `Contador`.
- O novo usuario recebe senha temporaria e deve troca-la no primeiro acesso.
- `Admin`: administra usuarios e possui acesso operacional completo.
- `Vendedor`: pode operar os modulos, mas nao administra usuarios.
- `Contador`: pode consultar paineis, relatorios e documentos, sem criar, alterar ou excluir dados operacionais.

## Policies utilizadas

| Policy | Papeis | Objetivo |
|---|---|---|
| `TenantMember` | Admin, Vendedor, Contador | Leitura dos dados do proprio tenant |
| `CanWrite` | Admin, Vendedor | Criacao e alteracao de dados operacionais |
| `CanManageUsers` | Admin | Administracao de acessos secundarios |
| `CanReviewAccounting` | Admin | Revisoes e aprovacoes contabeis |

## Criterio de aceite

O modulo e considerado concluido quando usuarios de tenants diferentes nao conseguem consultar ou modificar dados entre si, o Contador recebe somente as permissoes de leitura previstas e os tres papeis sao validados no backend, independentemente da interface.

