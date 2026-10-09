# Auditoria — Orken Service (backend + frontend)

**Commit auditado:** `e8b4873` (branch `feature/orken-service-closure-prC-resume` = master `8f5c6ab` + PR #35).
**Escopo:** `nexo-backend/src/**/Modules/Service`, `Controllers/Modules/Service`, `Controllers/Public/PublicServiceController.cs`, `Application/Modules/Service/Public`, `nexo-main/src/modules/service`, `nexo-main/src/modules/service-portal` e testes.
**Referências:** `docs/superpowers/specs/2026-08-20-orken-service-encerramento-design.md` (spec) e `docs/superpowers/plans/2026-08-20-orken-service-closure-wave1.md` (plano da Onda 1).

> **Achado crítico (🚨):** nenhum. Não achei acesso cross-tenant, bypass de autenticação nem corrupção financeira silenciosa. Todo repositório do Service usa o filtro global tenant+store (`NexoDbContext.cs:163-208`), e os caminhos públicos passam tenant/store explicitamente (`PublicServicePortalService.ResolveAsync`). Os achados mais graves são **altos** e **médios**: falta de autorização por papel no backend, vazamento de nome de cliente pelo telefone no portal público e corridas de concorrência. Ver "Achados de segurança".

---

## Resumo executivo

| # | Área | Estado | Maturidade |
|---|---|---|---|
| 1 | Configurações (SvcSettings) | 🟡 FUNCIONAL / INCOMPLETO | 70% |
| 2 | Preset / labels / capabilities (módulo único `service`) | 🟡 FUNCIONAL / INCOMPLETO | 80% |
| 3 | Onboarding | 🟡 FUNCIONAL / INCOMPLETO | 60% |
| 4 | Branding do portal | 🟢 PRODUÇÃO | 85% |
| 5 | Configuração do portal (`/service/portal`) | 🟡 FUNCIONAL / INCOMPLETO | 80% |
| 6 | Portal público de agendamento | 🟡 FUNCIONAL / INCOMPLETO | 75% |
| 7 | Clientes e subjects (cadastros) | 🟡 FUNCIONAL / INCOMPLETO | 55% |
| 8 | Registros (records / prontuário) | 🟠 PARCIAL | 40% |
| 9 | Profissionais e horário de trabalho | 🟡 FUNCIONAL / INCOMPLETO | 80% |
| 10 | Catálogo | 🟢 PRODUÇÃO | 85% |
| 11 | Agendamentos / agenda | 🟡 FUNCIONAL / INCOMPLETO | 60% |
| 12 | Ordens / comanda | 🟡 FUNCIONAL / INCOMPLETO | 55% |
| 13 | Pagamentos e lançamento financeiro | 🟡 FUNCIONAL / INCOMPLETO | 65% |
| 14 | Pacotes, pacotes do cliente e consumo | 🟡 FUNCIONAL / INCOMPLETO | 65% |
| 15 | Comissões e repasses (PR #35, não mergeado) | 🟡 FUNCIONAL / INCOMPLETO | 70% |
| 16 | Dashboard / visão geral | 🟡 FUNCIONAL / INCOMPLETO | 50% |
| 17 | Permissões / papéis | 🟠 PARCIAL | 35% |
| 18 | Lembretes (PR F) | ⚪ SCAFFOLD (inexistente) | 0% |
| 19 | Bloqueios de agenda (PR D) | ⚪ SCAFFOLD (inexistente) | 0% |
| 20 | Cancelamento público (PR E) | ⚪ SCAFFOLD (inexistente) | 0% |
| 21 | Dashboard financeiro do Service (PR F) | ⚪ SCAFFOLD (inexistente) | 0% |

**Módulo como um todo:** 🟡 FUNCIONAL / INCOMPLETO, **~60%** contra a definição de "v1 encerrado" (A1 = código + testes + deploy + QA visual em produção).
- A Onda 1 está com o código completo: A e B foram mergeados; C está aberto, ainda sem merge, deploy e QA.
- A Onda 2 (PRs D, E e F) está em **0%**: não há uma linha de código dela.

### Status dos PRs planejados

| PR | Situação verificada |
|---|---|
| **A (#33)** | **MERGEADO** em 2026-08-20 (`6f11c14`). Evidências no código: preset `barbearia` (`ServicePresetRegistry.cs`); `salao-beleza` com `Orders=true`; capabilities reduzidas a 5 (`ServiceCapabilities.cs`); `LegacyVerticalKeys`; migração de dados `20260820093038_ConvertLegacyServiceSubscriptions`; tema `barbearia` no `portal-theme.ts`. Deploy e QA visual em produção **não verificáveis no repositório**. |
| **B (#34)** | **MERGEADO** em 2026-08-20 (`8f5c6ab`). Evidências: `DefaultFinancialAccountProvisioner`, chamado em `PlatformController.cs:316` e `RegistrationService.cs:120`; migração `20260820094836_BackfillDefaultFinancialAccounts`; `ServiceFinancialPostingService` (SvcPayment → `FinancialTransaction` Receivable quitada, void → contra-lançamento `SvcPaymentVoid`); guarda `IsServiceManaged` em `FinancialService.cs:114/146/153/168`. Testes: `ServicePaymentsTests` e `Financial/DefaultAccountProvisioningTests`. Deploy e QA **não verificáveis**. |
| **C (#35)** | **ABERTO, sem merge** (`gh pr view 35`: MERGEABLE, 8 commits). Checks: `backend-tests` *pending*, GitGuardian *pass*, SonarCloud não listado. Entrega: `SvcCommissionEntry`/`SvcCommissionPayout`, os três gatilhos, estorno no void, fechamento, pagamento e guardas financeiras. Migração `20261009200014_AddServiceCommissions` (2 tabelas + 5 colunas). Tela `CommissionPanel` dentro de Profissionais. 46 testes de integração + 14 unitários + 11 de frontend. Além do plano: vínculo `SvcPackageUsage.AppointmentId` e a regra de anti-dupla-contagem pacote×agendamento. |
| **D — bloqueio de agenda** | **NÃO INICIADO.** `grep TimeBlock` em `nexo-backend/src` e `nexo-main/src` não retorna nada. Não existe entidade, migração, endpoint nem consumo no `AvailabilityCalculator`. |
| **E — cancelamento público por token + estorno de consumo** | **NÃO INICIADO.** Não há `PublicToken`, endpoint público de cancelamento nem janela em `SvcSettings`. `SvcCustomerPackageService` não tem reversão de consumo: só `Assign`, `Cancel` e `Consume`. O `SuccessCard.tsx` do portal oferece apenas `.ics` e copiar. |
| **F — lembrete por e-mail + dashboard financeiro** | **NÃO INICIADO.** Nenhum `IHostedService`, `BackgroundService` ou `PeriodicTimer` no backend. `IEmailService` tem só `SendVerificationEmailAsync`. Não há marca de envio em `SvcAppointment` nem rota `/service/financeiro`. |

---

## 1. Configurações (SvcSettings)
**Objetivo:** configuração por loja do Service: preset, agendamento público, branding e fuso.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 70%. O backend está sólido. Falta uma tela de configurações geral, falta trocar o ramo pela UI e não há controle de papel.

### Backend existente
- `SvcSettings` (StoreEntity), com `UpdatePublicBooking` validando faixas e `UpdateBranding` com hex sem regex (`SvcSettings.cs`).
- `SvcSettingsService` e `ServiceController`:
  - `GET /api/v1/service/settings`
  - `PUT /api/v1/service/settings/preset`
  - `GET|PUT /api/v1/service/settings/public-booking`
  - `PUT /api/v1/service/settings/branding`
- Migrações `AddServiceSettings`, `AddServicePublicBooking` e `AddServicePortalBranding`.

### Frontend existente
- `useServiceSettings` e `usePublicBookingSettings`.
- Formulários dentro de `/service/portal` (`PortalBookingSettingsForm`, `PortalBrandingForm`).

### Funcionalidades concluídas
- Ler e gravar preset, configuração de booking e branding de ponta a ponta, com testes (`ServiceSettingsTests` 4, `SvcSettingsTests`/`SvcSettingsPublicBookingTests`/`SvcSettingsServiceTests` 16).

### Funcionalidades parciais
- **Troca de preset:** o backend aceita (`SetPresetAsync` faz update), mas `useSetServicePreset` só é usado no onboarding. Depois de escolher, a UI não oferece como mudar o ramo.
- **`TimeZoneId`:** só vale no portal público. A agenda interna usa o fuso do navegador (`AgendaPage.tsx:76-77`).

### Não desenvolvidas
- Página "Configurações do Service" (hoje tudo fica no Portal público).
- Janela de cancelamento (PR E).

### Problemas
- Qualquer usuário do tenant pode alterar preset, branding e booking público: não há `[Authorize(Roles)]` (ver §17).

### Próximos passos
1. Restringir escrita a Gerente/Diretoria.
2. Criar a seção "Ramo" com troca confirmada.

---

## 2. Preset / labels / capabilities
**Objetivo:** um módulo comercial (`service`) com 10 presets internos que adaptam rótulos e superfícies.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 80%. A arquitetura está correta e testada. A apresentação dos rótulos tem bugs.

### Backend existente
- `ServicePresetRegistry` (10 presets, `Family="service"`, `IsServiceEntitlement`, `LegacyVerticalKeys`).
- `ServiceCapabilities` com 5 flags.
- `RequireServiceModuleAttribute`, que lê `ICurrentTenant.ActiveModules`.
- `GET /api/v1/service/preset`.

### Frontend existente
- `ServicePresetContext`, `service-surfaces.ts` e `service-family.ts`.
- `AppSidebar.tsx:61-70` aplica `capability`/`capabilityAny`.

### Concluídas
- Gate por módulo único: `ServicePresetGateTests` (3).
- Registry: `ServicePresetRegistryTests` (15).
- Superfícies por capability: `service-surfaces.test.ts` (9).
- Remoção das 4 flags mortas (PR A).

### Problemas (UX)
- **Pluralização ingênua com `${term}s`** em vários arquivos: `ProfissionaisPage.tsx:63`, `AgendaPage.tsx:120`, `OrdensPage.tsx:56`, `OrderDetailPage.tsx:104`, `CatalogoPage.tsx:62,88`, `SubjectsPage.tsx:79-80`, `service-surfaces.ts:72,82,107,115`. Resultados reais:
  - "**Profissionals**" (clínica, salão, pet, programador);
  - "Personals";
  - "Sessãos" (personal trainer);
  - "Ordem de serviços" em vez de "Ordens de serviço" (clínica, oficina, pet);
  - "Ordems";
  - "Avaliaçãos".
- **Sidebar com rótulos fixos** (`routes.ts:100-109`): "Ordens", "Profissionais", "Cadastros". Na barbearia, o menu diz "Ordens" e a página diz "Comandas"; o menu diz "Profissionais" e a página diz "Barbeiros".
- O atalho "Equipe de … e comissões" aparece mesmo quando `commissions=false` (`service-surfaces.ts:107`).
- Texto fixo "Todos os profissionais" (`AgendaPage.tsx:140`).
- `ServiceCapabilities.cs` ainda cita no XML doc o spec antigo (cosmético).

### Próximos passos
1. Adicionar ao preset um campo `plural` por label (backend) ou um mapa de plurais no frontend.
2. Fazer a sidebar usar `labels`.

---

## 3. Onboarding
**Objetivo:** escolher o ramo na primeira entrada no Service.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%.

- **Backend:** `PUT /api/v1/service/settings/preset`; `GET /settings` devolve `isConfigured`.
- **Frontend:** `ServiceOnboardingPage.tsx` (95 linhas, seletor de 10 cards, ícone próprio para barbearia). `ServicePresetContext.tsx:69-79` intercepta loja não configurada.
- **Concluído:** seleção persistida e gate de tela cheia.
- **Faltando:**
  - setup guiado: o primeiro profissional, o primeiro serviço, o horário e o slug do portal não são pedidos;
  - estado "o que fazer agora" após escolher;
  - troca posterior do ramo.
- **Problema (descoberta):** depois do onboarding, o usuário cai em páginas vazias, sem roteiro. O checklist útil existe só em `/service/portal` (`portal-status.ts`).

---

## 4. Branding
**Objetivo:** identidade da loja no portal público.
**Estado:** 🟢 PRODUÇÃO   **Maturidade:** 85%. Fluxo completo, mergeado no PR #32 e validado em produção segundo o histórico do projeto.

- **Backend:**
  - `SvcSettings.UpdateBranding`;
  - `PUT /api/v1/service/settings/branding` separado, para que salvar o booking não apague o branding;
  - contextos de upload `service-portal-logo`/`service-portal-cover` (`Controllers/Integrations/StorageController.cs:25`);
  - campos expostos em `GET /api/public/service/{slug}`.
- **Frontend:** `PortalBrandingForm.tsx` com `ImageUploadButton`; `PortalThemeRoot` aplica `BrandColor`.
- **Testes:** `Branding_set_by_admin_appears_in_public_portal`, `Branding_invalid_color_is_rejected`.
- **Problemas:**
  - `LogoUrl`/`CoverImageUrl` aceitam qualquer string de até 500 caracteres, sem validar origem (risco baixo, usada em `<img src>`);
  - sem papel no backend.

---

## 5. Configuração do portal (`/service/portal`)
**Objetivo:** slug, ativação, regras de agendamento, horários dos profissionais e publicação.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 80%.

- **Backend:** slug via `Store.PublicSlug` (`PATCH /api/stores/{id}/public-slug`), `settings/public-booking` e `WorkingHoursJson` no profissional.
- **Frontend:** `ServicePortalPage.tsx` com checklist (`portal-status.ts`, 10 testes), `PortalSlugSection` (4 testes), `PortalBookingSettingsForm` (2 testes), `ProfessionalHoursDialog` + `WeeklyHoursEditor` (`working-hours.test.ts`, 13 testes).
- **Problema (descoberta):** o horário de trabalho só pode ser editado em "Portal público". A página Profissionais não oferece horário. Quem não usa o portal não sabe que o horário existe, e a agenda interna não o usa.
- **Falta:** bloqueios/folgas (PR D) e prévia do portal com dados não publicados.

---

## 6. Portal público de agendamento
**Objetivo:** cliente final agenda sem login: serviço → profissional → horário → dados.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 75%. Funciona de ponta a ponta, mas tem lacunas de hardening e não tem cancelamento, bloqueio nem notificação.

### Backend
- `PublicServiceController` (`[AllowAnonymous]`):
  - `GET /api/public/service/{slug}`
  - `GET /api/public/service/{slug}/catalog`
  - `GET /api/public/service/{slug}/professionals`
  - `GET /api/public/service/{slug}/availability`
  - `POST /api/public/service/{slug}/appointments` (rate limit `public-booking`)
- `PublicServicePortalService`:
  - resolve a loja por slug e exige `SvcSettings`;
  - 403 se o booking estiver desligado;
  - preço e comissão tirados do catálogo (snapshot);
  - revalida lead time, janela, horário de trabalho e overlap.
- `AvailabilityCalculator` (puro, com fuso) e `ServiceWorkingHours`.

### Frontend
- `/agendar/:slug` → `service-portal/pages/BookingPage.tsx`.
- Componentes: `ServiceGrid`, `ProfessionalChooser` ("sem preferência" via união no cliente, `usePortalAvailability`), `Scheduler`, `GuestForm`, `SuccessCard` (`.ics` + copiar).
- Tema adaptativo por ramo (`portal-theme.ts`, 5 testes).

### Concluídas (com teste)
- 18 testes em `ServicePublicPortalTests`: slug 404, 403 com portal desligado, só itens e profissionais ativos, slots, overlap 409, `Booking_reuses_an_existing_customer_by_phone`, pet exige subject, sem pagamento/OS, sem vazamento de campos internos.
- Unitários: `AvailabilityCalculatorTests` (9) e `ServiceWorkingHoursTests` (4).

### Problemas
- **Vazamento de nome (médio):**
  - `CreateAppointmentAsync` resolve o cliente só pelo telefone (`ResolveOrCreateCustomerAsync`, `PublicServicePortalService.cs:275-297`);
  - devolve `CustomerName: customer.Name` (linha 218), que é o nome **já cadastrado**, não o digitado;
  - o `SuccessCard.tsx:64` mostra "Em nome de: …";
  - **Cenário:** quem conhece o telefone de alguém e faz uma reserva válida descobre o nome registrado e ainda pendura um agendamento no cadastro da vítima.
- **Corrida de double-booking (médio):** `HasOverlapPublicAsync` e o `INSERT` não são atômicos. Não há constraint de exclusão em `svc_appointments`: a configuração tem só índices comuns (`SvcAppointmentConfiguration.cs:39-41`).
- **Rate limit contornável (médio, transversal):** a partição usa o primeiro valor de `X-Forwarded-For`, que o cliente controla (`Program.cs:218-221`).
- **Entitlement não checado:**
  - o portal não verifica se o tenant ainda tem o módulo `service` nem se o tenant/assinatura está ativo;
  - só exige que `SvcSettings` exista (`ResolveAsync`);
  - tenant cancelado continua recebendo agendamentos.
- **Dados de cliente:**
  - cliente criado com `DocumentType.Cpf` e o telefone como "CPF" (`BuildUniqueDocumentAsync`), o que suja a base;
  - um `SvcSubject` novo é criado a cada reserva (duplicatas).
- **Horário sem alinhamento:** `StartsAt` não é validado contra `SlotIntervalMinutes`, então dá para reservar 10:07 (baixo).
- **Sem notificação:** nada avisa a loja nem o cliente (sem e-mail ou WhatsApp).
- **Falta:** cancelamento (PR E) e bloqueios (PR D).

### Próximos passos
1. Não devolver o nome do cadastro existente; devolver o nome digitado ou mascarado.
2. Criar constraint de exclusão (`tstzrange` + `EXCLUDE USING gist`) ou lock por profissional.
3. Checar entitlement e status do tenant.
4. Corrigir o IP do rate limit (`ForwardedHeaders` com proxies conhecidos).

---

## 7. Clientes e subjects
**Objetivo:** cliente do ERP (núcleo) e subject específico do ramo (pet, veículo, aluno).
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55%.

- **Backend:** `SvcSubject` (TenantEntity, `MetadataJson`), `SubjectsController` (CRUD + activate/deactivate), migração `AddServiceSubjectsAndRecords`. Testes: `ServiceSubjectsTests` (9) e `SvcSubjectTests` (7).
- **Frontend:**
  - `/service/subjects` (`SubjectsPage`, `SubjectDialog`), visível só quando o preset tem `subjectKind` (oficina, pet);
  - clientes vêm do núcleo `/clientes` (`useCustomers`, que chama `GET /customers` **sem paginação**: `customers.api.ts:21`).
- **Parciais / faltando:**
  - não existe visão de cliente do Service (histórico de agendamentos, pacotes, pagamentos, comissões);
  - não há criação rápida de cliente nos diálogos de agendamento e comanda: é preciso sair para `/clientes`;
  - a lista completa de clientes é carregada em cada diálogo e página (`AgendaPage`, `OrdensPage`, `PagamentosPage`, `SubjectDialog`…): problema de performance com base grande.
- **UX:** na barbearia, "Clientes" fica no grupo "Operação" do núcleo, junto com Vendas, Caixa e Fornecedores (varejo), e não dentro de "Serviços".

---

## 8. Registros (records)
**Objetivo:** linha do tempo, ou prontuário simples, por cliente, subject ou OS, com anexos.
**Estado:** 🟠 PARCIAL   **Maturidade:** 40%.

- **Backend:**
  - `SvcRecordEntry` com `AttachmentsJson`;
  - `RecordsController`: `GET /api/v1/service/records?contextType&contextId` (Customer, Subject, Order), `POST`, `DELETE /{id}` (**hard delete**);
  - contexto de storage `service-record`.
  - Testes: `ServiceRecordsTests` (7) e `SvcRecordEntryTests` (6).
- **Frontend:** só `SubjectRecordsDialog.tsx`, somente texto. O próprio arquivo diz que o upload de anexo ficou para depois (linhas 24-25). Não há UI de registros para Cliente nem para OS.
- **Backend sem frontend:** contextos Customer e Order; anexos.
- **Problemas:**
  - o domínio se descreve como append-only, mas o endpoint faz delete físico sem auditoria;
  - os presets de clínica e nutricionista, que mais precisariam de prontuário, não têm `subjectKind`, então **não veem registro nenhum**.

---

## 9. Profissionais e horário de trabalho
**Objetivo:** equipe, comissão padrão, cor e horário semanal.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 80%.

- **Backend:** `SvcProfessional` (`DefaultCommissionPercent`, `WorkingHoursJson`, `Color`), `ProfessionalsController` (CRUD + activate/deactivate). Testes: `SvcProfessionalTests` (10) e `ServiceFoundationTests` (8).
- **Frontend:** `/service/profissionais` (`ProfissionaisPage`, `ProfessionalDialog`), com estado vazio e erro; o `CommissionPanel` fica embaixo quando `commissions=true`.
- **Problemas:**
  - o horário só é editado em `/service/portal` (§5);
  - título "Profissionals" (§2);
  - não há vínculo profissional ↔ usuário do sistema, então o profissional não consegue ver a própria agenda ou comissão.

---

## 10. Catálogo
**Objetivo:** serviços com duração, preço, comissão e exigência de subject.
**Estado:** 🟢 PRODUÇÃO   **Maturidade:** 85%.

- **Backend:** `SvcCatalogItem`, `CatalogController` (CRUD + activate/deactivate). Testes: `SvcCatalogItemTests` (10) + integração em `ServiceFoundationTests`.
- **Frontend:** `/service/catalogo` (`CatalogoPage`, `CatalogItemDialog`, com campo de comissão só se `commissions`), estados vazio e erro.
- **Problemas menores:**
  - sem flag "oculto no portal" (todo item ativo aparece em público);
  - "Sessãos" no título do personal trainer.

---

## 11. Agendamentos / agenda
**Objetivo:** agenda interna com máquina de estados e snapshot de preço e comissão.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%.

### Backend
- `SvcAppointment` (máquina `Scheduled→Confirmed→InProgress→Completed`, saídas `Cancelled` e `NoShow`; `PriceSnapshot`; `CommissionPercentSnapshot`).
- `SvcAppointmentService`: valida referências e checa overlap por profissional (409).
- Endpoints: `GET/POST/PUT /api/v1/service/appointments`, `PATCH /{id}/status`.
- Concluir o agendamento reconhece a comissão na mesma transação.
- Testes: `ServiceAppointmentsTests` (16) e `SvcAppointmentTests` (11).

### Frontend
- `/service/agenda` (`AgendaPage`, `AppointmentDialog`, `AppointmentStatusDialog`), com `appointment-status.ts` espelhando a máquina (5 testes).

### Problemas
- **A agenda é uma tabela de um único dia** (`AgendaPage.tsx:64-78`): não há visão semanal, de calendário ou por profissional em colunas. Para barbearia e salão, é a principal falha de UX.
- **Concluir exige três transições:** Agendado → Confirmado → Em atendimento → Concluído. Não há atalho `Scheduled→Completed` (`SvcAppointment.CanTransition`).
- **Abrir comanda a partir do agendamento:** o backend existe (`POST /api/v1/service/orders/from-appointment/{id}`) e o hook `useCreateOrderFromAppointment` também, mas **não há UI**: o hook não é usado em lugar nenhum.
- **Agendamento interno ignora horário de trabalho** (e ainda não existem bloqueios).
- **Corrida de overlap**, como em §6.
- **Comissão sem dinheiro:** "agendamento concluído sem OS" gera comissão mesmo sem nenhum pagamento registrado. Está no desenho do spec (§4.2), mas fere o "regime de caixa" quando o dinheiro nunca entra no sistema.
- **Sem lembrete** (PR F).

---

## 12. Ordens / comanda
**Objetivo:** OS ou comanda com itens, total recalculado no servidor e pagamento.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55%.

### Backend
- `SvcOrder`/`SvcOrderItem`, com `CommissionPercentSnapshot` no item.
- `SvcOrderService` (lock `FOR UPDATE` em toda edição; item já comissionado fica congelado).
- `OrdersController`: CRUD, `from-appointment`, `PATCH status`, `POST/PUT/DELETE items`.
- Testes: `ServiceOrdersTests` (19), `SvcOrderTests` (10) e `SvcOrderItemTests` (5).

### Frontend
- `/service/ordens` (`OrdensPage`, `OrderCreateDialog` com profissional opcional).
- `/service/ordens/:id` (`OrderDetailPage`: adicionar e remover item, mudar status, `PaymentsPanel`).

### Problemas
- **Endpoints sem consumidor no frontend:** `PUT /orders/{id}` (`useUpdateOrder` sem uso), `PUT /orders/{id}/items/{itemId}` (`useUpdateOrderItem` sem uso) e `from-appointment`. Consequências:
  - não dá para atribuir o profissional depois de criar a comanda;
  - adicionar item não deixa escolher profissional (`OrderDetailPage.tsx:89` envia só `catalogItemId` e `quantity`);
  - **comanda criada sem profissional nunca gera comissão** e a UI não oferece como corrigir (`SvcCommissionService.cs:96`).
- **Cancelar comanda paga (médio, financeiro):**
  - `SvcOrderService.ChangeStatusAsync` não trava a linha nem checa pagamentos;
  - `SvcOrder.CanTransition` permite `InProgress→Cancelled` com pagamentos `Paid`;
  - resultado: a receita continua lançada no financeiro e a comissão continua ativa. O pagamento ainda pode ser estornado depois, mas nada obriga a isso. É um estado inconsistente.
- **Pacote + comanda podem cobrar duas vezes:** item da comanda consumido de pacote continua somando no `TotalAmount` (consumo é "histórico apenas"). Na prática, cobra-se duas vezes ou a comanda fica com saldo "em aberto" para sempre.
- **Lista sem filtro de período:** `GET /orders` sem filtros traz tudo, incluindo o dashboard (§16).

---

## 13. Pagamentos e lançamento financeiro
**Objetivo:** registrar o recebimento por OS ou pacote e espelhá-lo no financeiro do ERP (PR B).
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65%. O backend é robusto, mas o lançamento financeiro é **invisível ao usuário**.

### Backend
- `SvcPaymentService.CreateAsync` e `VoidAsync`: transação, lock na ordem e no pagamento, saldo restante (422).
- `ServiceFinancialPostingService` (Receivable quitada; void → Payable quitada `SvcPaymentVoid`; repasse → Payable `SvcCommissionPayout`).
- Guarda `IsServiceManaged` bloqueia create, update, pay e cancel pelo módulo financeiro.
- Provisionamento das 4 contas padrão e backfill.
- Testes: `ServicePaymentsTests` (24, com bloco "Financeiro (PR B)" na linha 229) e `SvcPaymentTests` (6).

### Frontend
- `/service/pagamentos` (`PagamentosPage`) e `PaymentDialog`/`PaymentsPanel` em OS e pacote.

### Problemas
- **O financeiro do ERP não tem frontend:** nenhuma chamada a `/api/financial` em `nexo-main/src` e nenhuma rota de financeiro fora de `/restaurante/financeiro`.
  - Os lançamentos `SvcPayment`, `SvcPaymentVoid` e `SvcCommissionPayout` só existem no banco.
  - O texto do `CommissionPanel` ("lança a despesa… em Contas a Pagar no Financeiro") aponta para uma tela que não existe.
- **Contra-lançamento como Payable:** o estorno vira uma "despesa" em vez de anular a receita, o que infla receita e despesa nos relatórios. Segue o spec §5, mas é dívida contábil.
- **Sem marcação de loja:** `FinancialTransaction` é TenantEntity, então em tenant com várias lojas a receita do Service não identifica a loja.
- **`PaidAt` retroativo:** o cliente informa `PaidAt` e o lançamento sai com essa data (retroativo permitido). A comissão usa o relógio do servidor, o que está correto.
- **Sem papel:** qualquer usuário do tenant estorna pagamento (§17).
- **Agendamento sem comanda:** não existe pagamento direto por agendamento. Na barbearia, atendimento sem comanda não gera receita no sistema.

---

## 14. Pacotes, pacotes do cliente e consumo
**Objetivo:** modelo de pacote, venda ao cliente com snapshot, consumo com histórico append-only.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65%.

### Backend
- `SvcPackage`/`SvcPackageItem`, `SvcCustomerPackage`/`SvcCustomerPackageItem` e `SvcPackageUsage`. No PR C, `SvcPackageUsage` ganhou `ProfessionalId`, `BaseAmountSnapshot`, `CommissionPercentSnapshot` e `AppointmentId`.
- `PackagesController` e `CustomerPackagesController` (`assign`, `cancel`, `consume`, `usages`).
- Testes: `ServicePackagesTests` (8), `ServiceCustomerPackagesTests` (19), `SvcPackageTests` (8) e `SvcCustomerPackageTests` (7).

### Frontend
- `/service/pacotes`, `/service/customer-packages`, `/service/customer-packages/:id`.
- Diálogos: `PackageDialog`, `AssignPackageDialog`, `ConsumePackageDialog` (pré-seleciona o agendamento candidato, commit `b344f38`).

### Problemas
- **Sem reversão de consumo:** é o PR E, não iniciado. Consumo errado é irreversível.
- **Corrida no consumo (médio):** `ConsumeAsync` não trava a linha e não há concurrency token (`grep Concurrency|xmin` em `Configurations/Modules/Service` vazio). Dois consumos simultâneos podem passar do saldo (lost update).
- **Comissão de pacote sem pagamento:** a comissão de consumo é reconhecida mesmo que o pacote nunca tenha sido pago (`RecognizePackageUsageAsync` não olha `SvcPayment`). O spec assume "pacote pago na venda", mas a venda (`AssignAsync`) não exige pagamento.
- **Rótulo genérico:** "Pacotes de clientes" não usa o label do preset.

---

## 15. Comissões e repasses (PR #35 — nesta branch, não mergeado)
**Objetivo:** ledger append-only com três fontes, estorno por void, fechamento por período e repasse pago no financeiro.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 70%. É o backend mais robusto do módulo. A nota cai porque o PR não foi mergeado nem deployado, não passou por QA, e há lacunas de fluxo.

### Backend
- **Domínio:** `SvcCommissionEntry` (Earning/Reversal, `ReversedAt`, `PayoutId`), `SvcCommissionPayout` (Pending/Paid), `SvcCommissionPolicy` (catálogo → padrão do profissional; 0 explícito não é sobrescrito), `SvcCommissionSource`.
- **`SvcCommissionService`:**
  - `RecognizeOrderIfSettledAsync`: só com a OS quitada (regime de caixa);
  - `RecognizeAppointmentAsync`: só se não houver OS vinculada nem pacote cobrindo;
  - `RecognizePackageUsageAsync`;
  - `ReverseOrderIfUnsettledAsync`: no void; earning ainda em aberto deixa de contar, earning já fechada gera entrada negativa;
  - `ClosePayoutAsync`: `UPDATE` condicional, 409 em concorrência;
  - `MarkPayoutPaidAsync`: idempotente, posta a Payable uma única vez.
- **Persistência:** índice único parcial `ux_svc_commission_entries_source` (`kind='Earning' AND reversed_at IS NULL`) e `ux_..._reversal_of`.
- **`CommissionsController`:**
  - `GET /api/v1/service/commissions/entries|summary|payouts|payouts/{id}`
  - `POST /payouts` e `POST /payouts/{id}/pay`, com `[Authorize(Roles="Gerente,Diretoria")]`, os únicos endpoints do Service com restrição de papel.

### Frontend
- `CommissionPanel.tsx` dentro de `/service/profissionais` (só com `capabilities.commissions`): resumo por profissional, período, entradas (incluindo "Estornada", "A descontar"), fechar e pagar; botões condicionados ao papel.
- Hooks em `useCommissions.ts`; `commission.ts` (8 testes); `CommissionPanel.test.tsx` (3).

### Concluídas (com teste)
- 46 testes de integração em `ServiceCommissionTests.cs`, cobrindo:
  - as três bases;
  - anti-dupla-contagem agendamento×OS e pacote×OS;
  - estorno no void;
  - fechamento sem reutilizar entradas;
  - repasse que gera Payable (fechar não toca o financeiro: linha 796).
- Mais 14 unitários (`SvcCommissionTests`).

### Problemas / lacunas
- **Cancelar OS paga** não reverte comissão (§12).
- **Comanda sem profissional** não gera comissão e a UI não deixa corrigir (§12).
- **Comissão sem pagamento:** agendamento concluído e consumo de pacote geram comissão sem pagamento correspondente (§11, §14).
- **Estorno sobre repasse fechado:** estorno de earning já fechada em repasse *Pending* não ajusta o repasse pendente; vira desconto no próximo. Se o profissional sair, o valor é perdido.
- **Descoberta:** não há item "Comissões" na sidebar; fica no rodapé de Profissionais.
- **Sem extrato próprio:** o profissional não tem acesso à própria comissão (não há vínculo usuário↔profissional).
- **Lista truncada:** `MaxEntriesPerQuery = 1000` sem paginação na UI.

### Próximos passos
1. Esperar `backend-tests` e Sonar, mergear, deployar e fazer QA.
2. Bloquear o cancelamento de OS com pagamento `Paid` (exigir void antes) ou reverter a comissão.
3. UI para profissional por item e na comanda.

---

## 16. Dashboard / visão geral
**Objetivo:** visão do dia e atalhos.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 50%.

- **Frontend:** `ServiceOverviewPage.tsx`, `useServiceDashboard.ts` e `lib/dashboard.ts` (4 testes). KPIs reais calculados no cliente: agenda de hoje, recebido hoje, ordens abertas, pacotes ativos.
- **Backend:** não há endpoint agregado; o próprio código registra a lacuna (`dashboard.ts:8-9`).
- **Problemas:**
  - `fetchOrders({})` busca **todas** as ordens da loja só para contar as abertas: performance;
  - sem período nem comparação;
  - sem receita, ticket médio ou comissão a pagar (PR F).

---

## 17. Permissões / papéis
**Objetivo:** limitar quem opera o quê.
**Estado:** 🟠 PARCIAL   **Maturidade:** 35%.

- **Backend:**
  - todos os controllers usam `[Authorize]` + `[RequireServiceModule]` (só tenant e entitlement);
  - o **único** controle de papel está em `CommissionsController.cs:73,84` (fechar e pagar repasse);
  - papéis do sistema: Diretoria, Gerente, Vendedor, Estoquista, Cozinha (`UserRole.cs`).
- **Frontend:** todas as rotas `/service/*` exigem `MGMT = ["diretoria","gerente"]` (`routes.ts:70,100-109`).
- **Problema (alto):** a restrição é só de UI. Um usuário Vendedor, Estoquista ou Cozinha de um tenant com Service, usando o token dele direto na API, consegue:
  - `POST /api/v1/service/payments/{id}/void`, que gera contra-lançamento financeiro e reverte comissões;
  - criar pagamentos (receita);
  - trocar o preset;
  - ligar ou desligar o portal público e alterar o branding;
  - desativar profissionais;
  - apagar registros (`DELETE /records/{id}`);
  - cancelar comandas.
- **Não desenvolvido:**
  - papel "Profissional/Atendente" (ver só a própria agenda);
  - auditoria (`IAuditWriter`) nas ações financeiras do Service.

---

## 18. Lembretes (PR F)
**Estado:** ⚪ SCAFFOLD — **inexistente**   **Maturidade:** 0%.
- Nenhum hosted service ou scheduler no backend.
- `IEmailService` só tem verificação de e-mail (`IEmailService.cs`); o transporte Resend existe (`ResendEmailService.cs`).
- Sem coluna de marca de envio em `SvcAppointment`.

## 19. Bloqueios de agenda (PR D)
**Estado:** ⚪ SCAFFOLD — **inexistente**   **Maturidade:** 0%.
- Sem `SvcTimeBlock`.
- `AvailabilityCalculator.GenerateSlots` considera só `busy` vindo de agendamentos (`PublicServicePortalService.cs:128-130`).
- A agenda interna não checa horário nem bloqueio.

## 20. Cancelamento público (PR E)
**Estado:** ⚪ SCAFFOLD — **inexistente**   **Maturidade:** 0%.
- Sem `PublicToken`, sem endpoint público além dos 5 listados em §6, sem janela em `SvcSettings`.
- `SuccessCard` não oferece link de cancelamento.
- O cliente só cancela pelo WhatsApp da loja, se ela tiver cadastrado um.

## 21. Dashboard financeiro do Service (PR F)
**Estado:** ⚪ SCAFFOLD — **inexistente**   **Maturidade:** 0%.
- Sem rota `/service/financeiro` (`routes.ts`).
- Sem endpoint agregado de receita, ticket médio ou comissão a pagar.
- Os dados de base já existem: `SvcPayment`, `FinancialTransaction` com `ReferenceType Svc*`, `SvcCommissionEntry` e `SvcCommissionPayout`.

---

## Lacunas para "Service v1 encerrado" (decisão A1)

### Bloqueia o encerramento da Onda 1
1. PR #35: merge (com `backend-tests`/Sonar verdes), deploy e QA visual em produção.
2. QA visual em produção de A e B: não há evidência no repositório.
3. Fluxo barbearia ainda furado na UI:
   - abrir comanda a partir do agendamento;
   - profissional por item ou na comanda;
   - agenda só em lista diária.
4. Lançamentos financeiros sem tela onde o dono os veja: o PR B "liga o dinheiro ao financeiro", mas o financeiro não tem frontend.

### Onda 2 inteira (0%)
- PR D (bloqueios)
- PR E (cancelamento por token + reversão de consumo)
- PR F (lembrete por e-mail + `/service/financeiro`)

### Hardening recomendado antes de congelar o v1
- Papéis no backend (§17).
- Vazamento de nome no portal (§6).
- Constraint anti-overlap.
- Lock ou concurrency token no consumo de pacote.
- Bloquear cancelamento de OS paga.

### Backlog congelado pelo spec §10 (fora do v1)
- WhatsApp
- Aprofundamento dos outros 8 verticais
- Recorrência
- No-show automático
- Prontuário evoluído
- Folha de pagamento
- Multi-recurso
- NF-e

### UX
- Pluralização ("Profissionals", "Sessãos", "Ordem de serviços").
- Sidebar com rótulos fixos.
- Comissões e horário de trabalho difíceis de descobrir.
- Onboarding sem setup guiado.
- Clientes fora do grupo "Serviços", junto com Vendas e Caixa de varejo.
- Estados vazios: estão presentes nas listas principais (Profissionais, Catálogo, Subjects, Agenda, Ordens, CommissionPanel), mas não há orientação de "primeiros passos" pós-onboarding.

---

## Backend sem frontend
- `PUT /api/v1/service/orders/{id}`: `useUpdateOrder` existe, mas não é usado.
- `PUT /api/v1/service/orders/{id}/items/{itemId}`: `useUpdateOrderItem` não é usado.
- `POST /api/v1/service/orders/from-appointment/{appointmentId}`: `useCreateOrderFromAppointment` não é usado.
- `PUT /api/v1/service/settings/preset` depois do onboarding: não há tela para trocar o ramo.
- Records com contexto `Customer` e `Order`, e anexos (`AttachmentsJson`, storage `service-record`).
- `/api/financial/*` (lançamentos `SvcPayment*`/`SvcCommissionPayout`): o ERP não tem UI de financeiro.
- `GET /api/v1/service/payments/customer-package/{id}/summary`: provavelmente consumido via `PaymentsPanel` (não confirmado linha a linha).

## Frontend sem backend/fluxo real
- Nenhum mock, array fixo ou localStorage encontrado (`grep mock|fake|TODO|localStorage` limpo).
- O dashboard calcula KPIs no cliente a partir de listas completas, sem endpoint agregado. É real, mas não escala.
- A promessa de despesa "em Contas a Pagar no Financeiro" no `CommissionPanel` não tem tela que a mostre.

## Código legado/morto
- `useCreateOrderFromAppointment`, `useUpdateOrder` e `useUpdateOrderItem` (`hooks/useOrders.ts`) não são usados.
- XML doc de `ServiceCapabilities.cs` cita o spec de junho e "validated during P1" (desatualizado).
- `ModuleDefinition` das SKUs legadas foram despublicadas pela migração do PR A. `LegacyVerticalKeys` continua apenas para reconhecimento: é intencional, não é morto.

## Achados de segurança

| # | Severidade | Achado | Evidência |
|---|---|---|---|
| S1 | **Alta** | Sem autorização por papel no backend do Service. Vendedor, Estoquista ou Cozinha estornam pagamentos (com efeito no financeiro), cancelam comandas, trocam o preset, ligam o portal público, alteram o branding e apagam registros. Só o fechamento e o pagamento de repasse checam papel. | `Controllers/Modules/Service/*.cs` (só `[Authorize]`); `CommissionsController.cs:73,84`; frontend `routes.ts:70` |
| S2 | **Média** | O portal público devolve o **nome cadastrado** do cliente achado pelo telefone e anexa o agendamento ao cadastro dele sem verificação. Permite enumerar telefone → nome e poluir o histórico de terceiros. | `PublicServicePortalService.cs:182,218,282-283`; `SuccessCard.tsx:64` |
| S3 | **Média** | O rate limit do POST público usa o 1º valor de `X-Forwarded-For` (controlado pelo cliente), o que permite contorná-lo. Problema transversal, também no login. | `Program.cs:218-221` |
| S4 | **Média** | Double-booking por corrida: o overlap é checado na aplicação, sem constraint de exclusão nem lock. | `SvcAppointmentRepository.HasOverlap*`; `SvcAppointmentConfiguration.cs:39-41` |
| S5 | **Média** | Consumo de pacote sem lock nem concurrency token: consumos simultâneos podem passar do saldo. | `SvcCustomerPackageService.ConsumeAsync`; sem `IsConcurrencyToken` nas configurations |
| S6 | **Média** (integridade financeira) | É possível cancelar OS com pagamentos `Paid`: a receita continua no financeiro e a comissão continua ativa. | `SvcOrderService.ChangeStatusAsync`; `SvcOrder.CanTransition` |
| S7 | **Baixa** | O portal público não checa se o tenant ainda tem o módulo `service` nem se está ativo; só exige `SvcSettings`. | `PublicServicePortalService.ResolveAsync` |
| S8 | **Baixa** | `LogoUrl`/`CoverImageUrl` aceitam URL arbitrária (usada em `<img>`). Cliente público é criado com o telefone como "CPF". | `SvcSettings.UpdateBranding`; `BuildUniqueDocumentAsync` |
| S9 | **Baixa** | `DELETE /records/{id}` faz delete físico sem trilha de auditoria. | `RecordsController.cs:60`; `SvcRecordEntryService.DeleteAsync` |

## Testes existentes para esta área

**Integração** (`nexo-backend/tests/Nexo.IntegrationTests/Service/`): **~181 testes**.

| Arquivo | Testes |
|---|---|
| `ServiceCommissionTests` | 46 |
| `ServicePaymentsTests` | 24 |
| `ServiceOrdersTests` | 19 |
| `ServiceCustomerPackagesTests` | 19 |
| `ServicePublicPortalTests` | 18 |
| `ServiceAppointmentsTests` | 16 |
| `ServiceSubjectsTests` | 9 |
| `ServiceFoundationTests` | 8 |
| `ServicePackagesTests` | 8 |
| `ServiceRecordsTests` | 7 |
| `ServiceSettingsTests` | 4 |
| `ServicePresetGateTests` | 3 |

Mais `Financial/DefaultAccountProvisioningTests` (PR B).

**Unitários** (`nexo-backend/tests/Nexo.UnitTests/Service/`): **~138 testes**.

| Arquivo | Testes |
|---|---|
| `ServicePresetRegistryTests` | 15 |
| `SvcCommissionTests` | 14 |
| `SvcAppointmentTests` | 11 |
| `SvcCatalogItemTests` | 10 |
| `SvcOrderTests` | 10 |
| `SvcProfessionalTests` | 10 |
| `AvailabilityCalculatorTests` | 9 |
| `SvcPackageTests` | 8 |
| `SvcCustomerPackageTests` | 7 |
| `SvcSettingsPublicBookingTests` | 7 |
| `SvcSubjectTests` | 7 |
| `SvcPaymentTests` | 6 |
| `SvcRecordEntryTests` | 6 |
| `SvcOrderItemTests` | 5 |
| `SvcSettingsTests` | 5 |
| `ServiceWorkingHoursTests` | 4 |
| `SvcSettingsServiceTests` | 4 |

**Frontend (Vitest):** **~84 casos** (sem contar a expansão de `it.each`).
- `service/lib/*.test.ts`: working-hours 13, portal-status 10, service-surfaces 9, commission 8, appointment-status 5, dashboard 4, service-family 4, order-status 3.
- `service/hooks/serviceKeys.test.ts`: 4.
- `service/components/*.test.tsx`: PortalSlugSection 4, CommissionPanel 3, PortalBookingSettingsForm 2.
- `service-portal/lib/*.test.ts`: booking-format 6, portal-theme 5, ics 4.

**E2E (Playwright):** **nenhum** para o Service. `nexo-main/e2e/` contém só `auth.e2e.spec.ts`.

**Sem teste:** UI do portal público (BookingPage), páginas operacionais (Agenda, Ordens, Pagamentos), concorrência (double-booking, consumo simultâneo) e cancelamento de OS paga.
