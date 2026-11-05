# Empresa sem assinatura: somente leitura

Enforcement por empresa, no servidor. A tela não é a barreira: quem chama a API direto recebe a mesma resposta.

## A regra

Vale a regra única `ModuleSubscription.HasAccessAt`: ativa ou em trial dentro do período (fim nulo = sem fim), ou
inadimplente há menos de 7 dias.

| Situação da empresa | `GET`, `HEAD` | Escrita (`POST`, `PUT`, `PATCH`, `DELETE`) |
|---|---|---|
| Pelo menos um módulo com acesso | Nada muda | Nada muda |
| Nenhum módulo com acesso | Funciona | **402** `subscription_required`, salvo as rotas isentas |

- O corpo do 402 é o formato de erro padrão: `{ "error", "details": [], "requestId", "code": "subscription_required" }`.
- Os gates por módulo continuam como estão. `[RequireModule]` e `[RequireServiceModule]` seguem respondendo 403, também
  no `GET`: sem acesso, a leitura de vendas (`SalesController`), PDV, listas de preço, Restaurante, Build e Service
  continua fechada. O somente leitura vale para o núcleo que não tem gate de módulo.
- Na impersonação a regra é a mesma, sem atalho.

**Por que somente leitura:** o dono ainda não decidiu o que fica acessível depois do trial. É o padrão conservador e não
destrutivo: o cliente mantém acesso aos próprios dados. Para mudar, a decisão inteira está em `TenantAccessPolicy`.

## Onde está

- `nexo-backend/src/Nexo.Api/Middleware/TenantAccessPolicy.cs`: o que é escrita, o que é isento e o interruptor.
- `nexo-backend/src/Nexo.Api/Middleware/TenantAccessMiddleware.cs`: aplica a política. Roda depois da autenticação e da
  autorização, então 401 e 403 continuam tendo precedência e a rota já está resolvida.
- `nexo-backend/tests/Nexo.IntegrationTests/Security/TenantAccessEnforcementTests.cs`: cada estado de assinatura, mais uma
  varredura de todas as rotas de escrita reais do host. Rota nova já nasce bloqueada e o teste confirma.

## Rotas isentas

A isenção compara o **modelo da rota**, não o caminho digitado. Maiúsculas e barra final não mudam a resposta.

- **Anônimas (`[AllowAnonymous]`), mesmo se o chamador mandar uma sessão:** login, refresh, MFA de login, cadastro,
  esqueci/redefinir senha, verificação de e-mail, webhook do Stripe, portais e rotas públicas (`api/public/*`).
- **Sessão:** `api/auth/logout`, `switch-store`, `impersonation/end`, `sessions/revoke-all`, `mfa/setup`, `mfa/enable`,
  `mfa/disable`.
- **Senha e MFA de usuários:** `api/users/{id:guid}/change-password`, `admin-reset-password`, `resend-invite`,
  `mfa/disable`. Os bloqueios próprios dessas rotas continuam valendo, como o 403 na impersonação.
- **Cobrança:** `api/billing/checkout`, `portal`, `subscriptions` (`GET`) e `webhook`. Sem isso o cliente não consegue
  pagar.
- **Identidade própria:** `api/platform/*` (e sessões de plataforma, que não têm empresa) e `api/orken/v1/*`.
- **Fora de `api/`:** `/health*` e o hub `/hubs/restaurant`. O `POST .../negotiate` só abre a conexão e o hub não grava
  dados.

## Interruptor de emergência

`Billing:EnforceTenantAccess` (variável `Billing__EnforceTenantAccess`), padrão `true`.

- `false`: nada é bloqueado, e cada escrita que seria bloqueada fica no log como `would be blocked`. Serve também para
  medir o impacto antes de ligar.
- Valor inválido conta como `true`.

Cada bloqueio gera um aviso no log com id da empresa, método e modelo da rota. Sem dados pessoais e sem ids do caminho.

## Cache

A decisão usa `ICurrentTenant.ActiveModules`, carregado pela resolução de tenant a partir de `tenant:{id}:info` (Redis, 5
minutos). Não há consulta nova por requisição.

- **Assinatura voltou ou saiu:** o webhook do Stripe e a concessão ou revogação na plataforma apagam a entrada. Vale na
  hora.
- **Período acabou sozinho (trial vencendo):** ninguém invalida. Vale em até 5 minutos.
- **Redis fora do ar:** `RedisCacheService` devolve vazio e a resolução de tenant lê o banco. A resposta não muda.

## Frontend

`api-client.ts` trata o 402 `subscription_required` nos três caminhos de envio: mantém a sessão, usa a mensagem
`SUBSCRIPTION_REQUIRED_MESSAGE` e avisa o app. `ProtectedRoute` leva Diretoria e Gerente para
`/assinatura?motivo=somente-leitura`, que explica o bloqueio e mostra o contato do suporte. Os outros papéis não abrem essa
tela, então ficam onde estão e veem a mensagem da chamada.

A mensagem não promete compra: hoje só `restaurante` e `build` têm preço, e quem testa `varejo` não tem o que comprar. A
decisão é do dono (`docs/audits/2026-11-04-auditoria-comercial-e-fiscal.md`, §2.11).
