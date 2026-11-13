# Empresa sem assinatura: somente leitura

Enforcement por empresa e por módulo, no servidor. A tela não é a barreira: quem chama a API direto recebe a mesma resposta.

**Decisão do dono:** depois do vencimento, a empresa fica em somente leitura, incluindo consultas e exportação. Escritas não
autorizadas ficam bloqueadas, sem impedir contratação ou administração legítima.

## A regra

Vale a regra única `ModuleSubscription.HasAccessAt`: ativa ou em trial dentro do período (fim nulo = sem fim), ou
inadimplente há menos de 7 dias. Um módulo com linha de assinatura e sem acesso agora está **vencido**
(`ModuleSubscription.ExpiredKeys`). Sem linha nenhuma, a empresa **nunca teve** o módulo.

### No módulo (`[RequireModule]` e `[RequireServiceModule]`)

| Situação do módulo | `GET`, `HEAD` | Escrita (`POST`, `PUT`, `PATCH`, `DELETE`) |
|---|---|---|
| Com acesso | Nada muda | Nada muda |
| Vencido (trial, período, `PastDue` fora da carência, `Canceled`, `Suspended`) | Funciona | **402** `subscription_required` |
| A empresa nunca teve | **403** | **403** |

- Vale também com outro módulo ativo: `varejo Canceled` com `build Active` lê `varejo` e não escreve lá.
- Revogar um módulo na plataforma deixa a linha `Canceled`, então ele fica vencido: a leitura continua e a escrita para na hora.
- O portão de ação (`RequireModuleFilter`) roda antes da validação automática do corpo: quem não pode escrever ouve isso, e
  não que o corpo está errado.

### Na empresa inteira (`TenantAccessMiddleware`)

| Situação da empresa | `GET`, `HEAD` | Escrita |
|---|---|---|
| Pelo menos um módulo com acesso | Nada muda | Nada muda (nas rotas do módulo vencido vale a tabela acima) |
| Nenhum módulo com acesso | Funciona | **402** `subscription_required`, salvo as rotas isentas |

Sem módulo nenhum com acesso, o middleware responde antes do portão. Por isso a escrita em um módulo que a empresa nunca teve é
402 nesse caso, e 403 quando ela tem outro módulo ativo. A leitura desse módulo é 403 nos dois casos. Nada é gravado em nenhum.

- O corpo do 402 é o formato de erro padrão: `{ "error", "details": [], "requestId", "code": "subscription_required" }`.
- Na impersonação a regra é a mesma, sem atalho.

## Onde está

- `nexo-backend/src/Nexo.Domain/Entities/ModuleSubscription.cs`: `HasAccessAt` e `ExpiredKeys`.
- `nexo-backend/src/Nexo.Api/Middleware/TenantAccessPolicy.cs`: o que é escrita, o que é isento e o interruptor.
- `nexo-backend/src/Nexo.Api/Middleware/TenantAccessMiddleware.cs`: a empresa inteira. Roda depois da autenticação e da
  autorização, então 401 e 403 continuam tendo precedência e a rota já está resolvida.
- `nexo-backend/src/Nexo.Api/Middleware/ExpiredModuleGate.cs`: o que os três portões fazem sem acesso ao módulo.
- `nexo-backend/tests/Nexo.IntegrationTests/Security/TenantAccessEnforcementTests.cs`: cada estado de assinatura, mais uma
  varredura de todas as rotas de escrita reais do host. Rota nova já nasce bloqueada e o teste confirma.
- `nexo-backend/tests/Nexo.IntegrationTests/Security/ExpiredModuleReadOnlyTests.cs`: cada módulo em cada estado vencido, sozinho e
  com outro ativo, exportação de verdade, e uma varredura de todas as rotas com portão.

## Rotas isentas

A isenção compara o **modelo da rota**, não o caminho digitado. Maiúsculas e barra final não mudam a resposta. O portão de
módulo respeita a mesma lista.

- **Anônimas (`[AllowAnonymous]`), mesmo se o chamador mandar uma sessão:** login, refresh, MFA de login, cadastro,
  esqueci/redefinir senha, verificação de e-mail, webhook do Stripe, portais e rotas públicas (`api/public/*`), relatórios de
  violação de CSP (`api/csp-reports`, que não grava nada no banco).
- **Sessão:** `api/auth/logout`, `switch-store`, `impersonation/end`, `sessions/revoke-all`, `mfa/setup`, `mfa/enable`,
  `mfa/disable`.
- **Senha e MFA de usuários:** `api/users/{id:guid}/change-password`, `admin-reset-password`, `resend-invite`,
  `mfa/disable`. Os bloqueios próprios dessas rotas continuam valendo, como o 403 na impersonação.
- **Administração de usuários:** `PUT api/users/{id:guid}`. A empresa precisa conseguir bloquear um funcionário desligado. A rota
  é só da Diretoria, e isso cobre a edição do próprio cadastro dela. Os outros papéis não têm rota de edição de perfil: a tela de
  perfil só troca senha, encerra sessões e cuida do MFA, que já são isentos. Criar usuário continua bloqueado.
- **Cobrança:** `api/billing/checkout`, `portal`, `subscriptions` (`GET`) e `webhook`. Sem isso o cliente não consegue
  pagar.
- **Leitura por `POST`, porque a pergunta tem corpo:** `api/tax/calculations/coverage` e `api/tax/calculations/{id:guid}/verify`.
  Rodam as regras em memória e não gravam nada. A isenção não liga o Tax Intelligence.
- **Identidade própria:** `api/platform/*` (e sessões de plataforma, que não têm empresa) e `api/orken/v1/*`.
- **Fora de `api/`:** `/health*` e o hub `/hubs/restaurant`. O `POST .../negotiate` só abre a conexão e o hub não grava
  dados.

### Auditadas e mantidas bloqueadas

Toda exportação de hoje é `GET` (`sales/export`, `receipt.pdf`, `close-report.pdf`, `sheet.pdf`, `budgets/{id}/pdf`, relatórios) e
não precisou de isenção. Estas parecem leitura e não são:

| Rota | Por que continua 402 |
|---|---|
| `POST api/users/validate-manager` | Grava auditoria a cada chamada e só serve para autorizar uma escrita, que seria recusada em seguida |
| `POST api/auth/verify-manager` | Conta tentativa errada e só serve para autorizar uma escrita. A tela não usa |
| `POST api/tax/simulations/supplier-comparison` | Grava a recusa na auditoria quando o Tax está desligado, e não consulta dado da empresa |
| `POST api/tax/simulations`, `POST api/tax/calculations`, `from-sale`, `opportunities/detect` | Gravam |
| `POST api/v1/build/.../ai/*`, `risk/explain`, `documents/{id}/analyze`, `POST api/v1/interpreter/analyze` | Consomem provedor pago e gravam a sugestão |
| `POST api/v1/build/projects/{id}/forecast`, `POST api/v1/build/materials/{id}/quote` | Gravam |

## GET com efeito colateral

Um `GET` aberto não pode gravar nem consumir recurso de fora. Auditoria dos controladores com portão e do que eles abrem:

| Rota | Efeito | Tratamento |
|---|---|---|
| `GET api/restaurante/settings` | Criava a linha padrão | Módulo vencido: responde o padrão e não grava |
| `GET api/v1/build/projects/{id}/weather`, `forecast` | Chamam a Open-Meteo | Módulo vencido: `available: false`, `reason: module_read_only`, sem chamar. A previsão de entrega sai sem o fator clima |
| `GET api/financial/ledger`, `cash-flow` (sem portão, mas a tela agora abre) | Geravam os títulos recorrentes | Empresa em somente leitura: não gera. O que ficou pendente é gerado quando o acesso volta |
| `GET api/v1/build/models/versions/{id}/source` | Grava uma linha de auditoria do link gerado | Mantido: é o registro de quem baixou o arquivo, não dado de negócio |
| `GET api/v1/build/budgets/{id}/pdf` | Baixa o logo da própria empresa | Mantido: não grava, é o arquivo do próprio cliente |
| Listas e detalhes do Build e `GET api/v1/service/records/{id}` | Assinam URL de arquivo privado | Mantido: assinatura local, sem chamada de rede |

Fora do escopo e sem mudança: `GET api/settings` e `dashboard/summary` criam uma vez a linha padrão de configurações. As consultas
sem portão de CEP, CNPJ, código de barras e clima (`api/integrations/*`) chamam provedores gratuitos.

## Interruptor de emergência

`Billing:EnforceTenantAccess` (variável `Billing__EnforceTenantAccess`), padrão `true`. Vale para a regra da empresa inteira.

- `false`: o middleware não bloqueia nada, e cada escrita que seria bloqueada fica no log como `would be blocked`. Serve também
  para medir o impacto antes de ligar.
- Valor inválido conta como `true`.
- Os portões de módulo não seguem o interruptor: já recusavam essas escritas antes dele existir, e desligá-lo não pode abrir
  módulo vencido.

Cada bloqueio gera um aviso no log com id da empresa, método e modelo da rota. Sem dados pessoais e sem ids do caminho.

## Cache

A decisão usa `ICurrentTenant.ActiveModules` e `ICurrentTenant.ExpiredModules`, carregados pela resolução de tenant a partir de
`tenant:{id}:info` (Redis, 5 minutos). Não há consulta nova por requisição.

- **Assinatura voltou ou saiu:** o webhook do Stripe e a concessão ou revogação na plataforma apagam a entrada e os outros caches
  dos portões. Vale na hora.
- **Período acabou sozinho (trial vencendo):** ninguém invalida. Vale em até 5 minutos.
- **Redis fora do ar:** `RedisCacheService` devolve vazio e a resolução de tenant lê o banco. A resposta não muda.
- **Entrada antiga, sem a lista de vencidos (janela de deploy):** é lida de novo do banco.
- **Mais de uma instância da API:** o `RequireModuleFilter` usa cache em memória de cada instância, e a invalidação só alcança a
  instância que recebeu o webhook. Vale para o que já existia, com o mesmo limite de 5 minutos.

## Frontend

A sessão (`/auth/me`, login, troca de loja, impersonação) traz `readOnlyModules`. `readableModules(session)` junta os módulos ativos
e os vencidos, e é o que os guardas de rota, o menu e a escolha de área usam para decidir o que abre.

- **Navegação:** `ProtectedRoute` não redireciona mais. A empresa navega nas telas dos módulos ativos e vencidos.
- **Aviso:** `ReadOnlyNotice` fica em todos os layouts, enquanto houver módulo vencido ou nenhum módulo com acesso. Diretoria e
  Gerente têm o link para `/assinatura?motivo=somente-leitura`. Os outros papéis não abrem essa tela, então não têm link.
- **402:** `api-client.ts` trata o `subscription_required` nos três caminhos de envio: mantém a sessão, usa a mensagem
  `SUBSCRIPTION_REQUIRED_MESSAGE` e avisa o app. `ProtectedRoute` mostra um aviso e não tira ninguém da tela.
- **Destino:** `homeRoute` nunca aponta para uma rota que devolve a pessoa a ela mesma. Vendedor ou Cozinha sem o módulo da sua
  tela vão para `/perfil`, que sempre abre.

Os botões de escrita não são escondidos: o 402 cobre. A mensagem não promete compra: hoje só `restaurante` e `build` têm preço, e
quem testa `varejo` não tem o que comprar. A decisão é do dono (`docs/audits/2026-11-04-auditoria-comercial-e-fiscal.md`, §2.11).

## Portais públicos (`api/public/*`)

Quem usa um portal não tem login, então nenhum portão de módulo o alcança: cada portal confere sozinho. A conferência é
`ModuleAccessQueries`: a regra única, mais a empresa `Active`, sempre no banco. Vencimento e suspensão valem na hora, sem esperar
cache.

| Portal | Continua funcionando sem acesso | Recusado sem acesso |
|---|---|---|
| Agendamento (Service) | cabeçalho, catálogo, profissionais, horários, o link do cliente e o cliente desmarcar o próprio horário | agendar: **403** `portal_read_only` |
| Cliente da obra (Build) | todas as leituras, abrir e encerrar a sessão de um portal com senha | decidir aditivo, escolher opção, aceitar entrega, abrir chamado, enviar foto, avaliar: **403** `portal_read_only` |
| Cardápio (Menu) | cardápio, zonas de entrega, acompanhar pedido | novo pedido: **422**, como já era |

- **A mensagem é neutra.** Quem visita é cliente da empresa e não precisa saber da assinatura dela: por isso 403 com
  `portal_read_only`, e não o 402 `subscription_required` de quem tem login.
- **A tela sabe antes:** `isBookingEnabled` (Service), `acceptingOrders` (Menu), `readOnly` e os indicadores `can*` (Build) vêm
  falsos, então a ação nem é oferecida.
- **O cliente desmarca o próprio horário também em somente leitura.** Decisão do dono, que reverte a do PR #155: uma assinatura vencida
  não pode prender o cliente a um horário em que ele não vai poder ir. É a única escrita aberta sem acesso ao módulo. Nada mais muda:
  - Valem as regras de sempre: o link, o prazo (`MinLeadMinutes`), o que já tem comanda ou pacote (`handledByStore`) e o horário já
    iniciado ou encerrado.
  - A escrita é só a linha do agendamento. Não existe e-mail de cancelamento, e `ServiceCustomerEmailsJob` ignora horário cancelado,
    inclusive quando o acesso volta. Nada pago ou operacional é acionado.
  - Agendar continua recusado, então a tela do link não oferece outro horário.
- **Empresa suspensa ou cancelada e loja inativa não recebem o cancelamento:** **403** `portal_read_only`. O link continua legível,
  `canCancel` vem falso e `onlineChangesPaused` vem verdadeiro, o que diferencia esse caso de um prazo vencido. A suspensão é uma parada
  decidida pela plataforma, e a loja inativa foi fechada pela empresa: ninguém ali veria a mudança, então o cliente é orientado a falar
  com o estabelecimento. A assinatura vencida é outra situação: a empresa segue operando e o cliente ainda conta com o horário.
- **O portal só de consulta não chama provedor externo:** o bootstrap do Build não traz `weather`.
- **Suspensão da empresa e loja inativa** também recusam a escrita, nos três portais.
- **Rota nova:** `PublicPortalExpiredModuleTests` lista toda escrita anônima sob `api/public/`. Uma rota nova reprova o teste até
  alguém decidir o que ela faz sem acesso.

## Jobs de fundo

Os jobs rodam sem login, por todas as empresas, e nenhum portão os alcança. Cada um filtra pela mesma `ModuleAccessQueries`, sempre
no banco. Sem acesso agora, a empresa não grava dado de operação e não consome serviço pago.

| Job | Sem acesso ao módulo | Quando o acesso volta |
|---|---|---|
| `BuildProgressSnapshotJob`, snapshots | Não grava o ponto do dia. Nada muda na obra, então a curva não perde informação | Volta a gravar. Não recria os dias parados |
| `BuildProgressSnapshotJob`, previsão | Não grava e não chama a Open-Meteo | Volta a gravar |
| `BuildWeeklyDigestJob` | Não gera sugestão e não chama o provedor pago | Gera na semana seguinte em que rodar |
| `BuildModelProcessingJobRunner` | O modelo espera na fila, sem gastar tentativa | Processa normalmente |
| `ServiceCustomerEmailsJob` | Não envia e não reivindica o e-mail | Lembrete sai se o horário ainda não passou. Confirmação com mais de 1 dia não sai, como já era |
| `OverdueTitlesJob` | Fica de fora só a empresa sem **nenhum** módulo com acesso, porque os títulos são do núcleo | A próxima passada recupera o atraso |

- **O título vencido não some da tela:** as listagens calculam o atraso pela data, com ou sem o job.
- **Fica de fora de propósito:** `AiKeyUpgradeHostedService` só reescreve chaves com a criptografia nova. Não é dado de operação e
  não consome serviço pago.
- **Rotas com login:** `GET` de módulo vencido não chama a Open-Meteo (`module_read_only`). As consultas de CEP e CNPJ continuam,
  porque são do núcleo e os provedores atuais são gratuitos.
