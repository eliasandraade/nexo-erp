# Orken Service — Encerramento do v1 (design)

**Data:** 2026-08-20
**Estado:** aprovado pelo owner (Elias) — pronto para `writing-plans`
**Base:** `master` @ `9a0c895` (merge do PR #32)

---

## 1. Objetivo

Encerrar o módulo Orken Service como **v1 congelado**: entregar o vertical
**Barbearias** de ponta a ponta, ligar o dinheiro do módulo ao financeiro do ERP,
remover o que é fictício, e declarar backlog explícito para todo o resto.

"Encerrado" = código completo + testes + deploy + QA visual do owner em produção
(decisão A1=b). Depois disso o módulo entra em congelamento: só correção de bug
(decisão A3).

---

## 2. Decisões do owner

| # | Decisão |
|---|---|
| A1 | Encerrado = código + testes + deploy + QA visual em prod |
| A2 | Apetite ~1 semana por onda |
| A3 | Congelar v1; resto vira backlog |
| B4 | Barbearia vira **preset próprio** (`barbearia`) |
| B5 | Termos: Cliente / Barbeiro / Serviço / Agendamento / Comanda |
| B6 | Ligar `Orders` (comanda/walk-in) para barbearia e salão |
| C7 | Comissão com fechamento pago / a pagar |
| C8 | Base = itens de comanda **+** agendamento concluído **+** consumo de pacote |
| C9 | Reconhecimento por regime de **caixa** |
| D10 | Pagamento do Service **integra** ao financeiro |
| D11 | Vale daqui pra frente; sem recálculo de histórico |
| D12 | Estorno/reversão de consumo de pacote entra |
| E13 | Apagar flags mortas de capability |
| E14 | Limpar SKUs legadas por vertical |
| E15 | Congelar os outros 8 verticais no nível atual |
| F16 | Bloqueio de agenda (folga/férias/feriado) entra |
| F17 | Cancelamento pelo cliente no portal entra — via **link com token único** |
| F18 | Lembrete entra — **e-mail agora (Resend), WhatsApp no backlog** |
| F19 | Dashboard financeiro do Service entra |
| — | Execução em **duas ondas** de ~3 PRs cada |
| — | Repasse de comissão: status no módulo + despesa no financeiro **ao pagar** |

---

## 3. Achados da auditoria que moldam o desenho

### 3.1 O alvo da integração financeira é `FinancialTransaction`, não `FinancialMovement`

`FinancialMovement` (`Nexo.Domain/Modules/Interpreter/`) é o ledger do Interpreter:
tenant-scoped, alimentado por LLM, com `MovementNature` só de despesa
(`Expense`/`Transfer`/`Reimbursement`/`Advance`). Não tem natureza de receita.

O financeiro real é `FinancialTransaction` — "Contas a pagar e a receber" — com
`TransactionType` (`Receivable`/`Payable`), `Status` (`Pending`/`Paid`/`Overdue`/`Cancelled`)
e o par `ReferenceType`/`ReferenceId`. O precedente a seguir é a venda:
`SaleService.cs:248` cria `FinancialTransaction.Create(..., referenceType: "Sale", ...)`
contra a conta padrão de `Receivable`.

**Decisão:** o Service usa `FinancialTransaction`. `FinancialContextType.Servico = 3`
permanece intocado (é do Interpreter, não deste fluxo).

### 3.2 Tenant novo nasce sem contas financeiras — P0

`SeedDefaultFinancialAccountsAsync` cria Caixa / Banco / Contas a Receber / Contas a Pagar
**apenas para o primeiro tenant**, e aborta se qualquer conta já existir. Nem o registro
nem o provisionamento de tenant na plataforma criam contas.

Consequência: com a integração do D10 ligada, a primeira barbearia a assinar quebraria
no primeiro pagamento com `"No active 'Contas a Receber' account found for this tenant."`

**Decisão:** provisionar as 4 contas padrão na criação de tenant, mais um ensure
idempotente para os tenants que já existem. É pré-requisito do PR B, não opcional.

### 3.3 Não existe scheduler no backend

Nenhum `BackgroundService`, `IHostedService`, Hangfire, Quartz ou Cronos.

**Decisão:** lembrete usa `IHostedService` + `PeriodicTimer` varrendo a janela das
próximas 24h. Instância única (o backend roda single-instance no Railway); marca de
envio no próprio agendamento evita duplicata se houver reinício.

### 3.4 Capabilities fictícias

`quotes`, `parts`, `recurrence` e `simpleRecord` existem em `ServiceCapabilities` e
**nunca são consumidas pelo frontend**. `salao-beleza` declara o label "Comanda" mas
tem `Orders = false` — label morto apontando para tela desligada.

**Decisão:** apagar as quatro flags; ligar `Orders` onde o label promete comanda.

---

## 4. Modelo de comissão

### 4.1 O conflito

C8 pede três bases; C9 pede regime de caixa. Mas pagamento (`SvcPayment`) só aponta
para **OS XOR pacote-do-cliente**. Agendamento nunca recebe pagamento, e o pacote já
foi pago no momento da venda. "Quando o pagamento entra" só se aplica a uma das bases.

### 4.2 A resolução

Evento único append-only `SvcCommissionEntry`, com três gatilhos e regra própria:

| Gatilho | Base do valor | Reconhece em |
|---|---|---|
| Pagamento quita o saldo da comanda | `SvcOrderItem.CommissionPercentSnapshot` | entrada do pagamento |
| Agendamento concluído **sem** OS vinculada | `SvcAppointment.PriceSnapshot` × % | conclusão do agendamento |
| Consumo de pacote | valor rateado do item do pacote × % | consumo |

**Anti-dupla-contagem:** a regra "sem OS vinculada" é normativa. Se o agendamento
originou uma comanda, apenas a comanda gera entrada. Teste dedicado cobre isso.

**Percentual aplicado** (primeira regra que casar): comissão do item de catálogo →
comissão padrão do profissional → nenhuma (não gera entrada).

### 4.3 Snapshots que faltam

`SvcAppointment` e `SvcPackageUsage` não têm snapshot de comissão. Ambos ganham
`CommissionPercentSnapshot` (aditivo, nullable), capturado no momento do evento —
mesma disciplina de imutabilidade já usada em `SvcOrderItem`.

### 4.4 Fechamento e repasse

`SvcCommissionPayout` congela um período por profissional: soma as entradas em aberto,
marca-as como incluídas, e nasce como a pagar dentro do módulo. Ao marcar o repasse
como pago, cria `FinancialTransaction` **Payable já quitada**,
`ReferenceType = "SvcCommissionPayout"`. Antes disso o financeiro não é tocado
(decisão do owner: despesa ao pagar, não provisionada).

---

## 5. Integração financeira

| Evento | Lançamento |
|---|---|
| `SvcPayment` confirmado | `FinancialTransaction` Receivable **quitada**, `ReferenceType = "SvcPayment"` |
| `SvcPayment` estornado (void) | Contra-lançamento `ReferenceType = "SvcPaymentVoid"` |
| Repasse de comissão pago | `FinancialTransaction` Payable **quitada**, `ReferenceType = "SvcCommissionPayout"` |

Estorno é sempre **contra-lançamento**, nunca deleção — espelha o padrão
`SaleCancellation` já existente. Sem recálculo de histórico: só pagamentos criados
a partir do deploy geram lançamento (D11).

---

## 6. Onda 1 — barbearia vendável

### PR A — Preset barbearia, comanda e limpeza
- Preset `barbearia` em `ServicePresetRegistry` (Cliente / Barbeiro / Serviço /
  Agendamento / Comanda), capabilities: `Appointments`, `Orders`, `Packages`, `Commissions`.
- Tema no `portal-theme.ts`; ícone no `ServiceOnboardingPage`; entrada em `service-family.ts`.
- `Orders = true` também para `salao-beleza` (resolve o label morto).
- Remove `quotes`, `parts`, `recurrence`, `simpleRecord` de `ServiceCapabilities`,
  DTOs e cliente TypeScript.
- Remove as SKUs legadas por vertical e o fallback legado do gate — precedido de
  migração que converte tenant com chave antiga para `service` + preset equivalente.
- Migração: conversão de módulo (dados), sem mudança de schema.

### PR B — Contas padrão por tenant e pagamento no financeiro
- Provisionamento das 4 contas padrão na criação de tenant + ensure idempotente.
- `SvcPayment` confirmado/estornado → `FinancialTransaction` conforme §5.
- Sem migração de schema.

### PR C — Comissão fim a fim
- `SvcCommissionEntry` (append-only) e `SvcCommissionPayout`.
- `CommissionPercentSnapshot` em `SvcAppointment` e `SvcPackageUsage`.
- Endpoints de listagem, fechamento e marcação de repasse pago.
- Tela de comissões dentro de Profissionais.
- Migração aditiva (2 tabelas + 2 colunas).

## 7. Onda 2 — agenda e portal

### PR D — Bloqueio de agenda
- `SvcTimeBlock` (StoreEntity; `ProfessionalId` nullable — nulo bloqueia a loja inteira;
  início, fim, motivo).
- Consumido pelo `AvailabilityCalculator`: some do portal público e bloqueia
  agendamento interno.
- Migração aditiva (1 tabela).

### PR E — Cancelamento público e estorno de pacote
- `SvcAppointment.PublicToken` opaco, gerado só na criação via portal; entregue na
  tela de sucesso e no `.ics`.
- Endpoint público de cancelamento por token, com janela configurável em `SvcSettings`.
- Reversão de consumo de pacote: devolve saldo, reabre status quando aplicável,
  registro append-only.
- Migração aditiva (1 coluna + 1 setting).

### PR F — Lembrete por e-mail e dashboard financeiro
- `IHostedService` + `PeriodicTimer` varrendo as próximas 24h.
- `IEmailService` estendido com lembrete de agendamento (transporte Resend já existente).
- Marca de envio no agendamento (idempotência).
- `/service/financeiro`: receita do período, comissão a pagar, ticket médio — leitura
  pura sobre o que B e C gravam.
- Migração aditiva (1 coluna).

---

## 8. Tratamento de erro

- **Conta financeira ausente:** o ensure do PR B roda antes; se ainda assim faltar, o
  pagamento falha com erro de domínio explícito, nunca com lançamento silenciosamente perdido.
- **Comissão sem percentual:** não gera entrada. Ausência de comissão é estado válido.
- **Fechamento concorrente:** entradas já incluídas em um repasse não entram noutro.
- **Token de cancelamento:** inválido, expirado, fora da janela e cancelamento em dobro
  retornam erro controlado, sem vazar existência de agendamento.
- **Lembrete:** falha de envio não derruba o loop nem marca como enviado.

## 9. Testes

Integração, no padrão dos arquivos existentes em `Nexo.IntegrationTests/Service/`:

- PR A: gate aceita `barbearia`; tenant legado convertido mantém acesso; comanda
  disponível para barbearia e salão.
- PR B: pagamento gera Receivable quitada; void gera contra-lançamento; tenant novo
  nasce com as 4 contas.
- PR C: as três bases geram entrada; agendamento com OS gera **uma só**; fechamento
  não reutiliza entrada; repasse pago gera Payable.
- PR D: bloqueio remove slot do portal e impede agendamento interno.
- PR E: token inválido / fora da janela / duplo cancelamento; reversão devolve saldo.
- PR F: lembrete não reenvia; dashboard soma o que B e C gravaram.

Mais unitários de registry, preset, cálculo de comissão e disponibilidade.

---

## 10. Fora de escopo — backlog congelado

WhatsApp; aprofundamento dos outros 8 verticais; recorrência/mensalidade; automação de
no-show; prontuário evoluído; comissão → folha de pagamento; multi-recurso
(salas/cadeiras); integração fiscal/NF-e; recálculo de histórico financeiro.

---

## 11. Riscos

| Risco | Severidade | Mitigação |
|---|---|---|
| PR B toca provisionamento de tenant (fora do Service) | Alta | Mudança aditiva; ensure idempotente; teste de tenant novo |
| Remoção de SKU legada quebra tenant em prod | Alta | Migração converte antes de remover; roda em qualquer ordem |
| Dupla contagem de comissão | Média | Regra "sem OS vinculada" + teste dedicado |
| Scheduler em múltiplas instâncias duplicaria lembrete | Média | Marca de envio persistida; hoje é single-instance |
| Onda 2 escorregar | Baixa | Onda 1 encerra sozinha e já é vendável |
