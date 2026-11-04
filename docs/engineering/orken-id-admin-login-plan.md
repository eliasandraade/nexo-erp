# Plano: login administrativo do ERP pelo Orken ID

Estado: **desenho, nada implementado**. Este documento também registra a auditoria do vínculo entre tenants do ERP e
organizações da Orken Platform (seção 6) e propõe o mapeamento explícito.

Contexto do lado da Platform: `orken-platform/docs/architecture/cockpit.md` (matriz SuperAdmin do ERP → Cockpit e plano
de desativação, passos 2 e 5).

## 1. Como é hoje (auditado em 2026-11-04, `master` a170ff1)

- **Contas:** tabela `platform_users` (`PlatformUser`): e-mail, hash de senha, papel `super_admin` ou `support`,
  `security_stamp` e segundo fator TOTP próprio (segredo, último passo usado, códigos de recuperação). Nenhuma coluna
  liga a conta a uma conta do Orken ID.
- **Login:** `POST /api/platform/auth/login`, depois `POST /api/platform/auth/mfa/verify` quando há segundo fator
  (`PlatformAuthController`, `PlatformAuthService`).
- **Sessão:** `platform_sessions`, refresh rotativo com janela de 30 s, vida de `Platform:SessionHours` (padrão 12 h,
  de 1 a 24). Access token do ERP de 15 min, com `type=platform`, `sid`, `security_stamp` e `platform_mfa`.
- **Revogação:** `SecurityStampValidationMiddleware` confere sessão e carimbo no banco a cada requisição; logout,
  "revogar sessões" e reset de MFA valem na hora.
- **Autorização:** política `Platform` (qualquer conta de plataforma) e `PlatformAdmin` (`super_admin` e, com
  `Platform:RequireMfaForAdmin` = true, `platform_mfa=true`). Tokens de plataforma só alcançam `/api/platform/*` e
  poucas rotas de autenticação.
- **Primeira conta:** criada pelo `DataSeeder` a partir de configuração.
- **Orken ID no ERP:** nenhuma referência. O frontend não usa o cliente `orken-erp-web` que a Platform documenta, e o
  backend não valida tokens do Orken ID, exceto, a partir do contrato de operações, na rota de serviço
  `/api/orken/v1/operations` (esquema `OrkenService`, só tokens de serviço, ver `orken-cockpit-contract.md`).

## 2. Objetivo

Quem administra o ERP entra com a conta do Orken ID (senha e segundo fator do Orken ID), e não com uma senha guardada no
ERP. O papel dentro do ERP continua sendo decidido pelo ERP. O login atual fica até a equivalência ser comprovada, e
depois só como acesso de emergência.

## 3. Papéis: o que cada um decide

| Papel | Onde vive | O que decide | Relação com os outros |
|---|---|---|---|
| PLATFORM_ADMIN | Platform (`platform_role_assignments`) | autoridade global: organizações, produtos, assinaturas, contas do Orken ID, Cockpit | **não** dá acesso ao SuperAdmin do ERP |
| `super_admin` do ERP | ERP (`platform_users.role`) | tenants do ERP, módulos, impersonação, catálogo tributário, provedores e limites do interpretador | **não** dá acesso ao Cockpit |
| `support` do ERP | ERP (`platform_users.role`) | leitura e suporte no SuperAdmin do ERP, sem as ações de `PlatformAdmin` | idem |

Nenhum papel vira outro por e-mail igual. O Orken ID só prova **quem** é a pessoa e **como** ela entrou (`amr`, `acr`,
`auth_time`); o papel no ERP vem da conta do ERP vinculada a ela.

## 4. Desenho

### 4.1 Cliente OIDC

Novo cliente no seed do Orken ID (`orken-platform`, migrator), por exemplo `orken-erp-admin`:

- confidencial, Authorization Code + PKCE (S256), sem refresh token;
- redirect exato: `https://api.erp.orken.com.br/api/platform/auth/orken/callback`;
- escopos: `openid profile email`. **Nunca** `platform.admin`: o ERP não deve receber um token que valha na Platform API;
- `acr_values=urn:orken:acr:mfa` em todo pedido, e `max_age` curto (ou 0) para ações sensíveis;
- segredo só no ambiente (`Oidc__Clients__N__Secret` na Platform; no ERP, uma variável própria, por exemplo
  `OrkenId__AdminClient__Secret`).

O fluxo é feito pelo backend do ERP (BFF): o navegador nunca recebe o ID token nem o código; `state`, `nonce` e o
verificador PKCE ficam num cookie cifrado e de vida curta.

### 4.2 Callback e sessão

1. Valida o ID token (issuer, audiência `orken-erp-admin`, assinatura pela descoberta, `nonce`) e exige `acr` =
   `urn:orken:acr:mfa` com `mfa` em `amr`; senão, recusa e audita.
2. Procura `platform_users.orken_sub = sub`. Sem vínculo: recusa com "conta do Orken ID não vinculada" e audita. Nunca
   procura por e-mail.
3. Com vínculo e conta ativa: abre a mesma sessão de hoje (`platform_sessions`, mesmo access token com `type=platform`),
   com `platform_mfa=true` porque o segundo fator foi feito no Orken ID, e `auth_method=orken_id` para a auditoria.
4. Audita `platform.login_orken_id` com `sub`, `amr`, IP e user agent.

Como a sessão é a do ERP, nada muda para o resto do sistema: políticas, revogação por carimbo, impersonação auditada.

**Lacuna conhecida:** o Orken ID ainda não tem back-channel logout. Uma conta desativada no Orken ID continua com a
sessão do ERP até ela vencer. Mitigação: sessões abertas pelo Orken ID com vida menor (por exemplo, 8 h, sem renovação
além disso) e o vínculo pode ser removido no ERP, o que revoga as sessões na hora (troca do carimbo).

### 4.3 Vínculo `platform_users` ↔ `sub` do Orken ID (explícito e confirmado)

Colunas novas em `platform_users` (migração aditiva): `orken_sub` (único quando não nulo), `orken_linked_at`,
`orken_linked_by`. O vínculo nunca é deduzido de e-mail igual.

Fluxo:

1. A pessoa entra no SuperAdmin do ERP **pelo login atual** (senha e segundo fator do ERP) e escolhe "Vincular ao Orken
   ID". Isso prova que ela controla a conta do ERP.
2. O ERP manda ao Orken ID (mesmo cliente, `max_age=0` para exigir nova autenticação, MFA exigido). Isso prova que ela controla a conta do Orken ID.
3. Na volta, o ERP mostra o que será ligado (e-mail e nome do Orken ID, e-mail e papel do ERP) e pede uma confirmação
   digitada. Só então grava `orken_sub` e audita `platform_user.orken_linked` (quem, `sub`, quando, de onde).
4. Um `super_admin` vê a lista de vínculos e pode desfazer qualquer um (`platform_user.orken_unlinked`, com motivo),
   o que troca o carimbo e encerra as sessões daquela conta.

Um `sub` só pode estar em uma conta; uma conta só tem um `sub`. Reset de senha ou de segundo fator no Orken ID não mexe
no vínculo; desativar a conta no Orken ID impede novos logins.

### 4.4 Configuração e chave de liga/desliga

- `Platform:OrkenIdLogin:Enabled` (padrão `false`): mostra o botão e liga o callback.
- `Platform:PasswordLogin:Enabled` (padrão `true`): só passa a `false` na fase 4, e mesmo então as contas marcadas como
  emergência continuam entrando com senha.

## 5. Fases

| Fase | O que muda | Critério para seguir |
|---|---|---|
| 0 | Este plano | revisão |
| 1 | Cliente `orken-erp-admin` no Orken ID; callback e vínculo no ERP atrás de `Platform:OrkenIdLogin:Enabled=false` | testes de integração: sem MFA recusa; sem vínculo recusa; vínculo só com as duas provas; nenhuma busca por e-mail; sessão igual à atual |
| 2 | Liga em produção. Cada operador vincula a própria conta. As duas formas de login funcionam | todas as contas ativas vinculadas, e cada operador entrou pelo Orken ID pelo menos uma vez |
| 3 | Equivalência comprovada por uma pessoa | checklist abaixo |
| 4 | Login por senha só para contas de emergência (uma ou duas, guardadas fora do uso diário), com alerta a cada uso | uma semana sem uso de senha fora da emergência |
| 5 | `platform_users` fica só como cadastro de papéis do ERP e acesso de emergência | regra permanente |

**Checklist de equivalência (fase 3):** login e logout; MFA exigido; revogar sessões; reset de MFA (agora no Orken ID);
impersonação continua auditada com o operador real; políticas `Platform` e `PlatformAdmin` iguais; auditoria mostra
`auth_method`; desvincular encerra as sessões na hora.

**Reverter:** `Platform:OrkenIdLogin:Enabled=false` e, se a fase 4 já tiver sido feita, `Platform:PasswordLogin:Enabled=true`.
Os vínculos ficam guardados e não dão acesso a nada sozinhos. Nada é apagado.

## 6. Tenants do ERP e organizações da Platform

### 6.1 Auditoria (2026-11-04, `master` a170ff1)

- **Não há vínculo.** A tabela `tenants` tem `slug`, `company_name`, `trade_name`, `tax_id`, `email`, `phone`,
  `business_type`, `stripe_customer_id`, `status` e `trial_ends_at`: nenhuma coluna com o id de uma organização da
  Platform. Nenhuma outra tabela do ERP guarda esse id.
- **Não há chamada à Platform.** O ERP não usa `platform.tenancy.read` nem o feed de eventos; não existe cliente de
  serviço do ERP no seed do Orken ID (só GOV, PREC e COND têm). O único contato é o novo contrato de leitura, que é a
  Platform chamando o ERP e só com agregados.
- **Do lado da Platform:** o produto `ERP` está no catálogo e organizações podem ter acesso a ele, mas isso não aponta
  para nenhum tenant do ERP. Por isso o Cockpit não mostra nada do ERP por organização.

### 6.2 Proposta: mapeamento explícito, confirmado e auditado (só desenho)

Regras:

- O vínculo nunca é deduzido de nome, e-mail, CNPJ ou slug parecidos. O sistema também não sugere pares: a tela pode
  mostrar os dados dos dois lados para a pessoa conferir, mas quem escolhe é a pessoa.
- Um tenant ↔ uma organização (por produto `ERP`), com índice único nos dois sentidos.
- A Platform é a autoridade do vínculo (é ela que o Cockpit lê); o ERP guarda uma cópia para usar depois
  (entitlements por organização, feed de eventos).
- Criar e desfazer exigem operador com segundo fator recente nos dois sistemas, motivo escrito e auditoria nos dois.

Fluxo proposto (código de uso único, para que cada lado prove o que controla):

1. **Cockpit:** um PLATFORM_ADMIN abre a organização O, escolhe "Vincular tenant do ERP", informa o motivo (com step-up)
   e recebe um código de uso único (10 min). A Platform guarda só o hash, com O, quem pediu e quando, e audita
   `product_link.code_issued`.
2. **SuperAdmin do ERP:** um `super_admin` com segundo fator abre o tenant T, cola o código e confirma digitando o slug
   do tenant. O ERP chama a Platform com um cliente de serviço do ERP (novo `orken-erp-service`, produto `ERP`,
   escopo novo de escrita só para isso, por exemplo `platform.product_links.confirm`), enviando o código e uma
   referência opaca do tenant (o id do tenant, nunca nome ou CNPJ).
3. **Platform:** confere o código (hash, validade, uso único, produto do cliente = `ERP`), grava o vínculo
   (`product_tenant_links`: produto, organização, referência do tenant, quem pediu, quem confirmou, quando, motivo) e
   audita `product_link.confirmed`. Responde com o id da organização.
4. **ERP:** grava `tenants.orken_organization_id` e audita `tenant.orken_linked`.
5. **Desfazer:** pelo Cockpit (motivo, step-up) ou pelo ERP; o outro lado sabe pelo feed de eventos
   (`product_link.revoked.v1`). Desfeito, o ERP limpa a coluna e o Cockpit para de mostrar o tenant na organização.

Só depois disso o contrato de operações pode ganhar números por organização (por exemplo, módulos e MRR estimado da
organização O), sempre marcados como do ERP e nunca somados aos da Platform.
