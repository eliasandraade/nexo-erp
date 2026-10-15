# Orken Service — Encerramento, Onda 1 (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar o vertical Barbearias vendável — preset próprio, comanda/walk-in, comissão fim a fim — e ligar o dinheiro do Orken Service ao financeiro do ERP.

**Architecture:** Três PRs sequenciais sobre `master`. PR A é registry + limpeza (sem schema). PR B provisiona contas financeiras por tenant e liga `SvcPayment` → `FinancialTransaction`. PR C introduz o evento de comissão (`SvcCommissionEntry`) com três gatilhos e o fechamento de repasse (`SvcCommissionPayout`). Cada PR encerra sozinho e é deployável.

**Tech Stack:** .NET 9 / EF Core (Npgsql), xUnit + FluentAssertions, React 18 + TypeScript + React Query + Vitest.

**Spec:** `docs/superpowers/specs/2026-08-20-orken-service-encerramento-design.md`

---

## Mapa de arquivos

**PR A**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServiceCapabilities.cs` — remove 4 flags
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServicePresetRegistry.cs` — preset `barbearia`, `Orders` no salão
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Seed/DataSeeder.cs` — converte tenant legado, para de semear SKU por vertical
- Modify: `nexo-main/src/modules/service/api/service.api.ts` — tipo `ServiceCapabilities`
- Modify: `nexo-main/src/modules/service/lib/service-family.ts` — opção barbearia
- Modify: `nexo-main/src/modules/service/pages/ServiceOnboardingPage.tsx` — ícone
- Modify: `nexo-main/src/modules/service-portal/lib/portal-theme.ts` — tema `barbearia`
- Test: `nexo-backend/tests/Nexo.UnitTests/Service/ServicePresetRegistryTests.cs`
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServicePresetGateTests.cs`
- Test: `nexo-main/src/modules/service/lib/service-surfaces.test.ts`, `service-family.test.ts`
- Test: `nexo-main/src/modules/service-portal/lib/portal-theme.test.ts`

**PR B**
- Create: `nexo-backend/src/Nexo.Application/Features/Financial/DefaultFinancialAccountProvisioner.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/ServicePaymentPostingService.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Service/SvcPaymentService.cs` — posta e estorna
- Modify: `nexo-backend/src/Nexo.Api/Controllers/PlatformController.cs` — provisiona no create-tenant
- Modify: `nexo-backend/src/Nexo.Infrastructure/Auth/RegistrationService.cs` — provisiona no registro
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServicePaymentPostingTests.cs`

**PR C**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcCommissionEntry.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcCommissionPayout.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcCommissionSource.cs` (enum)
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcCommissionService.cs` + `SvcCommissionDtos.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Service/ServiceCommissionsController.cs`
- Modify: `SvcAppointment.cs`, `SvcPackageUsage.cs` — `CommissionPercentSnapshot`
- Modify: `NexoDbContext.cs` + configurations + `DependencyInjection.cs` (Application e Infrastructure)
- Create: migração `AddServiceCommissions`
- Create: `nexo-main/src/modules/service/components/CommissionPanel.tsx`
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServiceCommissionTests.cs`

---

## PR A — Preset barbearia, comanda e limpeza

### Task A1: Preset `barbearia` no registry

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServicePresetRegistry.cs`
- Test: `nexo-backend/tests/Nexo.UnitTests/Service/ServicePresetRegistryTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

Adicionar em `ServicePresetRegistryTests.cs`:

```csharp
[Fact]
public void Barbearia_preset_uses_barber_labels_and_enables_comanda()
{
    var preset = ServicePresetRegistry.GetByKey("barbearia");

    preset.Should().NotBeNull();
    preset!.Labels.Professional.Should().Be("Barbeiro");
    preset.Labels.Order.Should().Be("Comanda");
    preset.Capabilities.Appointments.Should().BeTrue();
    preset.Capabilities.Orders.Should().BeTrue();
    preset.Capabilities.Packages.Should().BeTrue();
    preset.Capabilities.Commissions.Should().BeTrue();
}

[Fact]
public void Presets_that_label_an_order_must_enable_orders()
{
    foreach (var p in ServicePresetRegistry.All.Where(p => p.Labels.Order == "Comanda"))
        p.Capabilities.Orders.Should().BeTrue($"{p.Key} promete comanda no label");
}
```

Adicionar `"barbearia"` aos `[InlineData]` existentes que enumeram presets válidos (linhas 31 e 128).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test nexo-backend/tests/Nexo.UnitTests --filter FullyQualifiedName~ServicePresetRegistryTests`
Expected: FAIL — `preset` é null.

- [ ] **Step 3: Implementar**

Em `BuildAll()`, inserir antes de `salao-beleza` (prioridade 7 vira 8 e os seguintes deslocam):

```csharp
new("barbearia", "Barbearias", 7,
    new ServiceLabels("Cliente", "Barbeiro", "Serviço", "Agendamento", "Comanda", "Registro"),
    off with { Appointments = true, Orders = true, Packages = true, Commissions = true }),
```

E ligar `Orders` no salão:

```csharp
new("salao-beleza", "Salões de Beleza", 8,
    new ServiceLabels("Cliente", "Profissional", "Serviço", "Agendamento", "Comanda", "Registro"),
    off with { Appointments = true, Orders = true, Packages = true, Commissions = true }),
```

`escola-idiomas` passa a prioridade 9.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test nexo-backend/tests/Nexo.UnitTests --filter FullyQualifiedName~ServicePresetRegistryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add nexo-backend/src/Nexo.Domain/Modules/Service/ServicePresetRegistry.cs nexo-backend/tests/Nexo.UnitTests/Service/ServicePresetRegistryTests.cs
git commit -m "feat(service): preset barbearia e comanda no salao"
```

### Task A2: Remover as capabilities fictícias

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServiceCapabilities.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServicePresetRegistry.cs`
- Modify: `nexo-main/src/modules/service/api/service.api.ts`

- [ ] **Step 1: Escrever o teste que falha**

Em `ServicePresetRegistryTests.cs`:

```csharp
[Fact]
public void Capabilities_expose_only_surfaces_the_product_actually_has()
{
    var names = typeof(ServiceCapabilities).GetProperties().Select(p => p.Name).ToArray();

    names.Should().BeEquivalentTo(
        "Appointments", "Orders", "Packages", "Commissions", "SubjectKind");
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test nexo-backend/tests/Nexo.UnitTests --filter FullyQualifiedName~Capabilities_expose_only`
Expected: FAIL — sobram `Quotes`, `Parts`, `SimpleRecord`, `Recurrence`.

- [ ] **Step 3: Implementar**

`ServiceCapabilities` passa a:

```csharp
public sealed record ServiceCapabilities(
    bool Appointments,
    bool Orders,
    bool Packages,
    bool Commissions,
    ServiceSubjectKind? SubjectKind);
```

Ajustar o baseline `off` e todos os 10 presets em `BuildAll()` — remover `Quotes`, `Parts`, `SimpleRecord`, `Recurrence` de cada `with`. No TypeScript, remover `quotes`, `parts`, `simpleRecord`, `recurrence` da interface `ServiceCapabilities`.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build nexo-backend/Nexo.sln` — deve compilar sem erro (nenhum consumidor referenciava as flags).
Run: `dotnet test nexo-backend/tests/Nexo.UnitTests --filter FullyQualifiedName~ServicePresetRegistryTests`
Run: `cd nexo-main && npx tsc --noEmit`
Expected: PASS nos três.

- [ ] **Step 5: Commit**

```bash
git add -A nexo-backend/src/Nexo.Domain/Modules/Service nexo-main/src/modules/service/api/service.api.ts nexo-backend/tests
git commit -m "refactor(service): remove capabilities que nunca foram consumidas"
```

### Task A3: Migração de tenant legado e remoção das SKUs por vertical

**Files:**
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Seed/DataSeeder.cs`
- Modify: `nexo-backend/src/Nexo.Api/Attributes/RequireServiceModuleAttribute.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Service/ServicePresetRegistry.cs`
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServicePresetGateTests.cs`

A conversão roda no seeder (idempotente, sem schema): todo `ModuleSubscription` com chave de vertical vira `service`, e o `SvcSettings` da loja recebe o preset correspondente se ainda não tiver um. Só depois as `ModuleDefinition` legadas são despublicadas.

- [ ] **Step 1: Escrever o teste que falha**

Em `ServicePresetGateTests.cs`:

```csharp
[Fact]
public async Task Legacy_vertical_key_is_converted_to_the_single_service_module()
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

    var legacy = await db.ModuleSubscriptions.IgnoreQueryFilters()
        .AnyAsync(s => ServicePresetRegistry.LegacyVerticalKeys.Contains(s.ModuleKey));

    legacy.Should().BeFalse("a conversão do seeder já rodou");
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test nexo-backend/tests/Nexo.IntegrationTests --filter FullyQualifiedName~Legacy_vertical_key`
Expected: FAIL — `LegacyVerticalKeys` não existe.

- [ ] **Step 3: Implementar**

Em `ServicePresetRegistry`, substituir `IsServiceFamilyKey`/`FamilyKeys` por uma lista explícita de legado (as chaves antigas não são mais presets vendáveis, e `barbearia` nunca foi SKU):

```csharp
/// <summary>Chaves de módulo por vertical vendidas antes do modelo de módulo único.</summary>
public static IReadOnlyList<string> LegacyVerticalKeys { get; } = new[]
{
    "clinica-medica", "salao-beleza", "pet-shop", "oficina-mecanica",
    "nutricionista", "personal-trainer", "autoescola", "escola-idiomas",
    "programador-autonomo",
};

public static bool IsLegacyVerticalKey(string? moduleKey) =>
    !string.IsNullOrWhiteSpace(moduleKey)
    && LegacyVerticalKeys.Any(k => string.Equals(k, moduleKey, StringComparison.OrdinalIgnoreCase));

public static bool IsServiceEntitlement(string? moduleKey) =>
    string.Equals(moduleKey, Family, StringComparison.OrdinalIgnoreCase);
```

`Resolve(...)` some (a resolução por chave de módulo morreu com o modelo v1.1 — o preset vem de `SvcSettings`). Remover também seus testes.

No `DataSeeder`, novo passo idempotente `ConvertLegacyServiceSubscriptionsAsync` chamado dentro de `SeedServiceModulesAsync`, antes do passo 3: para cada `ModuleSubscription` com chave legada, criar (se faltar) a assinatura `service` do mesmo tenant, aplicar o preset correspondente no `SvcSettings` de cada loja do tenant que ainda não tenha preset, e remover a assinatura legada. Em seguida, despublicar as `ModuleDefinition` legadas e parar de criar as 5 do passo 1.

No gate, atualizar o XML doc: não há mais fallback legado.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test nexo-backend/tests/Nexo.IntegrationTests --filter FullyQualifiedName~ServicePresetGate`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A nexo-backend
git commit -m "feat(service): converte tenant legado e aposenta SKUs por vertical"
```

### Task A4: Frontend — barbearia visível

**Files:**
- Modify: `nexo-main/src/modules/service/lib/service-family.ts`
- Modify: `nexo-main/src/modules/service/pages/ServiceOnboardingPage.tsx`
- Modify: `nexo-main/src/modules/service-portal/lib/portal-theme.ts`
- Test: `nexo-main/src/modules/service/lib/service-family.test.ts`, `service-surfaces.test.ts`, `nexo-main/src/modules/service-portal/lib/portal-theme.test.ts`

- [ ] **Step 1: Escrever os testes que falham**

```ts
// service-family.test.ts
it("oferece barbearia no onboarding", () => {
  expect(SERVICE_PRESET_OPTIONS.map((o) => o.key)).toContain("barbearia");
  expect(isValidPresetKey("barbearia")).toBe(true);
});

// portal-theme.test.ts — adicionar ao it.each existente
["barbearia", "barbearia"],

// service-surfaces.test.ts
it("barbearia (appointments + orders + packages) → agenda + comanda + pacotes + pagamentos", () => {
  const preset = makePreset({ appointments: true, orders: true, packages: true, commissions: true });
  expect(enabledSurfaces(preset).map((s) => s.key)).toEqual([
    "agenda", "orders", "packages", "payments", "professionals", "catalog",
  ]);
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `cd nexo-main && npx vitest run src/modules/service src/modules/service-portal`
Expected: FAIL nos três novos.

- [ ] **Step 3: Implementar**

`service-family.ts`: adicionar `{ key: "barbearia", label: "Barbearias" }` antes de `salao-beleza`.

`ServiceOnboardingPage.tsx`: importar `Scissors` já existe — usar um ícone distinto do salão. Adicionar ao mapa: `"barbearia": Scissors` e trocar `"salao-beleza"` para `Sparkles` (importar de lucide-react), para os dois cartões não ficarem idênticos.

`portal-theme.ts`: novo tema `barbearia` — barbearia pede algo mais escuro e masculino que o salão:

```ts
barbearia: {
  key: "barbearia", display: FRAUNCES, body: MANROPE, mood: "classic",
  bg: "#f4f3f1", bgSoft: "#e7e5e1", surface: "#fbfaf9", line: "#ddd9d3",
  ink: "#171513", muted: "#6b645c", accent: "#8c6239", accentInk: "#fdf8f3", accentSoft: "#eee2d3",
  heroFrom: "#e9e5df", heroTo: "#f7f5f2", radius: 14,
},
```

E o mapeamento `"barbearia": "barbearia"` no `PRESET_THEME`.

- [ ] **Step 4: Rodar e ver passar**

Run: `cd nexo-main && npx vitest run src/modules/service src/modules/service-portal`
Run: `cd nexo-main && npx tsc --noEmit`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A nexo-main/src
git commit -m "feat(service-fe): barbearia no onboarding e tema do portal"
```

### Task A5: Suíte completa e PR

- [ ] **Step 1: Rodar a suíte inteira**

Run: `dotnet test nexo-backend/Nexo.sln`
Run: `cd nexo-main && npx vitest run && npx tsc --noEmit && npm run build`
Expected: verde em ambos. `nexo-main/dist` é versionado — conferir se o build sujou a árvore e restaurar se o PR não for de frontend deployável.

- [ ] **Step 2: Push e PR**

```bash
git push -u origin feature/orken-service-closure-prA-barbearia
gh pr create --title "feat(service): Barbearias, comanda e limpeza do v1 (PR A)" --body-file -
```

- [ ] **Step 3: Esperar os gates** — backend-tests, SonarCloud, GitGuardian.

---

## PR B — Contas padrão por tenant e pagamento no financeiro

### Task B1: Provisionar contas financeiras por tenant

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Features/Financial/DefaultFinancialAccountProvisioner.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/PlatformController.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Auth/RegistrationService.cs`
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Financial/DefaultAccountProvisioningTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

```csharp
[Fact]
public async Task New_tenant_is_provisioned_with_the_four_default_accounts()
{
    var created = await CreateTenantViaPlatformAsync("barbearia-do-ze");

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
    var accounts = await db.FinancialAccounts.IgnoreQueryFilters()
        .Where(a => a.TenantId == created.Id).ToListAsync();

    accounts.Select(a => a.Type).Should().BeEquivalentTo(new[]
    {
        FinancialAccountType.Cash, FinancialAccountType.Bank,
        FinancialAccountType.Receivable, FinancialAccountType.Payable,
    });
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test ... --filter New_tenant_is_provisioned`. Expected: FAIL, lista vazia.

- [ ] **Step 3: Implementar** — `DefaultFinancialAccountProvisioner.EnsureAsync(Guid tenantId, CancellationToken)` cria só o que falta (idempotente, `IgnoreQueryFilters` para checar por tenant). Chamar na criação de tenant da plataforma e no registro self-service.

- [ ] **Step 4: Rodar e ver passar.**

- [ ] **Step 5: Commit** — `feat(financial): provisiona contas padrao por tenant`

### Task B2: `SvcPayment` → `FinancialTransaction`

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/ServicePaymentPostingService.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Service/SvcPaymentService.cs`
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServicePaymentPostingTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact]
public async Task Paying_a_service_order_creates_a_settled_receivable()
{
    var payment = await CreatePaymentForOrderAsync(amount: 80m);

    var tx = await FindTransactionAsync("SvcPayment", payment.Id);
    tx.Should().NotBeNull();
    tx!.TransactionType.Should().Be(TransactionType.Receivable);
    tx.Status.Should().Be(TransactionStatus.Paid);
    tx.Amount.Should().Be(80m);
}

[Fact]
public async Task Voiding_a_payment_creates_a_counter_entry_and_keeps_the_original()
{
    var payment = await CreatePaymentForOrderAsync(amount: 80m);
    await VoidPaymentAsync(payment.Id);

    (await FindTransactionAsync("SvcPayment", payment.Id)).Should().NotBeNull();
    var reversal = await FindTransactionAsync("SvcPaymentVoid", payment.Id);
    reversal!.Amount.Should().Be(80m);
    reversal.TransactionType.Should().Be(TransactionType.Payable);
}
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar** — `ServicePaymentPostingService.PostAsync(SvcPayment)` e `.ReverseAsync(SvcPayment)`, seguindo `SaleService.cs:248`: busca a conta padrão do tipo, cria a `FinancialTransaction` com `referenceType`/`referenceId`, e `MarkPaid()` na hora. Chamado por `SvcPaymentService` depois do `SaveChanges` do pagamento, na mesma unidade de trabalho.

- [ ] **Step 4: Rodar e ver passar.**

- [ ] **Step 5: Commit** — `feat(service): pagamento do Service lanca no financeiro`

### Task B3: Suíte, push, PR, gates. Mesmo procedimento do A5.

---

## PR C — Comissão fim a fim

### Task C1: Snapshots de comissão que faltam

**Files:** `SvcAppointment.cs`, `SvcPackageUsage.cs` + configurations + migração.

- [ ] Teste: agendamento criado a partir de item de catálogo com comissão guarda o percentual; consumo de pacote idem. Rodar, falhar, implementar (`decimal? CommissionPercentSnapshot`, capturado no `Create`), migração aditiva `AddServiceCommissions`, rodar, commitar.

### Task C2: `SvcCommissionEntry` e os três gatilhos

**Files:** `SvcCommissionEntry.cs`, `SvcCommissionSource.cs`, `SvcCommissionService.cs`.

Entidade append-only (`StoreEntity`): `ProfessionalId`, `Source` (`OrderItem`/`Appointment`/`PackageUsage`), `SourceId`, `BaseAmount`, `CommissionPercent`, `CommissionAmount`, `RecognizedAt`, `PayoutId` (nullable).

- [ ] Testes, nesta ordem — cada um é um step de escrever/falhar/implementar/passar/commitar:
  1. Pagamento que quita a comanda gera uma entrada por item com percentual.
  2. Item sem percentual (nem no catálogo, nem no profissional) **não** gera entrada.
  3. Agendamento concluído sem OS gera entrada sobre o `PriceSnapshot`.
  4. **Agendamento concluído cujo `SvcOrder.AppointmentId` aponta para ele gera uma entrada só** (a da comanda).
  5. Consumo de pacote gera entrada sobre o valor rateado.

### Task C3: `SvcCommissionPayout` e o repasse

- [ ] Fechamento por profissional/período soma as entradas com `PayoutId == null` e as carimba. Entrada já carimbada não entra noutro fechamento (teste dedicado). Marcar pago cria `FinancialTransaction` Payable quitada com `ReferenceType = "SvcCommissionPayout"` — reusa o `ServicePaymentPostingService` do PR B, generalizado.

### Task C4: Endpoints, tela e PR

- [ ] `ServiceCommissionsController` com listar entradas, fechar período e marcar pago — todos sob `[RequireServiceModule]`.
- [ ] `CommissionPanel.tsx` dentro de Profissionais, visível só quando `capabilities.commissions`.
- [ ] Suíte, push, PR, gates.

---

## Self-review

**Cobertura do spec:** §3.1 → B2; §3.2 → B1; §3.4 → A2; §4.2 → C2 (os 5 testes cobrem as 3 bases + o anti-dupla-contagem); §4.3 → C1; §4.4 → C3; §5 → B2 e C3; §6 → A1–A5, B1–B3, C1–C4; §9 → testes em cada task. §3.3 (scheduler) e §7 são da Onda 2 — fora deste plano por desenho.

**Tipos:** `LegacyVerticalKeys`/`IsLegacyVerticalKey` definidos em A3 e usados no teste de A3. `ServicePaymentPostingService` criado em B2 e generalizado em C3. `CommissionPercentSnapshot` criado em C1 e consumido em C2.

**Risco conhecido:** A3 remove `Resolve(...)` e `IsServiceFamilyKey`, que têm testes existentes em `ServicePresetRegistryTests.cs` (linhas 66, 94) e são citados no XML doc do gate. Esses testes saem junto — é remoção deliberada, não regressão.
