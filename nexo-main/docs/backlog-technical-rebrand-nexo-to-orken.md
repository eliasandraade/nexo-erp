# Backlog técnico — Rebrand interno Nexo → Orken

> **Escopo:** rebrand **não-visível** (código interno). Separado de propósito da revisão
> visual `ui/orken-frontend-polish`, que só tocou no que aparece para o usuário final.
> Nada aqui muda a UI; tudo aqui tem **risco de sessão/compatibilidade** e exige migração.

## Por que não foi feito agora

As ocorrências abaixo são chaves de armazenamento, nomes de cookie e identificadores
internos. Renomear sem migração **desloga todos os usuários ativos** e quebra
impersonation, refresh de token e o onboarding em andamento. É um trabalho de
infraestrutura com janela de migração — não de polish visual.

## Itens

### 1. Chaves de `localStorage` (`nexo:*`)
`src/services/api-client.ts` (`nexo:access_token`, `nexo:refresh_token`, `nexo:session`),
`src/pages/ImpersonatePage.tsx` + `PlatformTenantDetailPage.tsx` (`nexo:impersonate:*`),
`src/modules/auth/services/authService.ts` + `OnboardingWizard.tsx` (`nexo:onboarding:*`),
`src/modules/dashboard/pages/DashboardPage.tsx` (`nexo:setup-dismissed:*`),
`src/modules/auth/pages/VerifyEmailPage.tsx` / `RegisterPage.tsx` (`nexo:pending_email`).

**Migração sugerida:** ler `nexo:*` como fallback, gravar `orken:*`, e fazer
`migrate-on-read` por algumas releases antes de remover as chaves antigas.

> Nota: o novo módulo de workspaces já nasce com a convenção correta
> (`orken:last-module:{tenantId}:{userId}`).

### 2. Cookies de autenticação (`nexo_access`, `nexo_refresh`)
Definidos no **backend** (`Nexo.Api`). Renomear exige mudar backend + frontend juntos,
com janela em que ambos os nomes são aceitos. Coordenar com `nexo-backend`.

### 3. Classe/keyframe do app-shell (`.nexo-spinner` / `@keyframes nexo-spin`)
`index.html`. Invisível ao usuário (só nome de classe). Trivial, mas faz parte do
shell crítico de performance — trocar junto de uma mudança já planejada no `index.html`.

### 4. Senha temporária `"nexo@temp"`
`src/modules/users/services/userService.ts`. Valor placeholder de criação de usuário.

### 5. Namespaces/pastas internas (`nexo-main`, `nexo-backend`, `Nexo.*`)
Nome do projeto frontend (`package.json` → `vite_react_shadcn_ts`), pastas raiz e
namespaces .NET (`Nexo.Api`, `Nexo.Domain`, `Nexo.Infrastructure`). **Maior esforço**,
puramente técnico, sem impacto de UX. Fazer isolado, com build verde a cada passo.

## Recomendação
Tratar como épico próprio "Technical rebrand: internal namespaces/packages from Nexo to
Orken", priorizando 1 e 2 (sessão) atrás de uma flag de migração, e deixando 5
(namespaces) por último.
