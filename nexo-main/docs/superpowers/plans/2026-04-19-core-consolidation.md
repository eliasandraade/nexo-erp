# Core Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all frontend mock data and connect every module to the real .NET backend, ensuring the entire system reflects a single source of truth in the database.

**Architecture:** All mock-backed services (users, settings, audit, sales history, reports) are replaced by `apiClient` calls. A `SaleDto → CompletedSale` adapter isolates UI components from DTO changes. Backend gains an `AuditController` and a `ReportsController`.

**Tech Stack:** React + TanStack Query, TypeScript, ASP.NET Core 8, EF Core, PostgreSQL — all existing infrastructure.

---

## Etapa 0 — Controlled DB Reset

### Task 0: Reset database to clean state

**Files:**
- No code changes — pure operations checklist

- [ ] **Step 1: Backup existing data**

```bash
# From the backend server / developer machine
pg_dump -U postgres nexo_db > nexo_backup_$(date +%Y%m%d_%H%M).sql
```

Expected: `.sql` file written with size > 0.

- [ ] **Step 2: Drop and recreate DB**

```sql
-- Connect as postgres superuser
DROP DATABASE IF EXISTS nexo_db;
CREATE DATABASE nexo_db OWNER postgres;
```

- [ ] **Step 3: Apply all migrations**

```bash
cd nexo-backend
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
```

Expected: "Done." with 15 migrations applied (last: `20260418223114_AddFeatureFlags`).

- [ ] **Step 4: Verify seeds run on startup**

Start the backend (`dotnet run --project src/Nexo.Api`) and check logs for:
- `Tenant "Andrade Systems" seeded`
- `Admin user seeded`
- `Module definitions seeded`

If seeds are not logged, run the seed command manually:
```bash
dotnet run --project src/Nexo.Api -- --seed
```

- [ ] **Step 5: Validate 8 critical flows**

Open the app and confirm each flow end-to-end:
1. Login → admin / nexo@2026 → redirects to /dashboard ✓
2. Products → create a product with stock min → appears in list ✓
3. Inventory → stock item visible with quantity ✓
4. Cash → open session → shows "Caixa Aberto" ✓
5. PDV → search product → add to cart → finalize → success modal ✓
6. Restaurante → open table → add items → close order → back to floor ✓
7. Settings → change company name → reload → persists ✓
8. Audit → page shows records for actions above ✓

- [ ] **Step 6: Commit checkpoint**

```bash
# No code changes in Etapa 0 — commit is skipped
echo "DB reset complete"
```

---

## Etapa 1 — Mock Elimination

### Task 1: Create `SaleDto → CompletedSale` adapter

**Files:**
- Create: `src/modules/sales/utils/saleAdapter.ts`

This adapter is used by Tasks 5, 6, 7, 8 so it must exist first.

- [ ] **Step 1: Write the adapter**

Create `src/modules/sales/utils/saleAdapter.ts`:

```typescript
import type { SaleDto, SaleItemDto, SalePaymentDto } from "../api/sales.api";
import type { CartItem, CompletedSale, PaymentEntry } from "../types";

/** Maps backend payment method casing → frontend method key */
function mapPaymentMethod(
  method: string
): PaymentEntry["method"] {
  const m = method.toLowerCase();
  if (m === "cash")     return "cash";
  if (m === "pix")      return "pix";
  if (m === "debit")    return "card";
  if (m === "credit")   return "card";
  return "cash";
}

function mapItem(dto: SaleItemDto): CartItem {
  return {
    productId:   dto.productId,
    code:        dto.productCode,
    description: dto.productName,
    unitPrice:   dto.unitPrice,
    quantity:    dto.quantity,
    totalPrice:  dto.total,
    unit:        "un",
    status:      "active",
  };
}

function mapPayment(dto: SalePaymentDto): PaymentEntry {
  return {
    method: mapPaymentMethod(dto.method),
    amount: dto.amount,
  };
}

function mapStatus(
  status: SaleDto["status"]
): CompletedSale["status"] {
  if (status === "Cancelled") return "cancelled";
  return "completed";
}

export function saleToLegacy(dto: SaleDto): CompletedSale {
  const subtotal     = dto.subtotal;
  const discountTotal = dto.discountAmount;
  const total        = dto.total;
  const payments     = dto.payments.map(mapPayment);

  const cashPaid = payments
    .filter((p) => p.method === "cash")
    .reduce((acc, p) => acc + p.amount, 0);
  const change = Math.max(0, cashPaid - total);

  return {
    id:               `#${dto.number}`,
    timestamp:        dto.confirmedAt ?? dto.paidAt ?? dto.createdAt,
    operator:         dto.soldByName,
    status:           mapStatus(dto.status),
    items:            dto.items.map(mapItem),
    subtotal,
    discountTotal,
    total,
    payments,
    change,
    customerName:     dto.customerName ?? undefined,
    cancelledAt:      dto.cancelledAt ?? undefined,
    // authorizedBy and cancellationReason not in SaleDto yet
  };
}
```

- [ ] **Step 2: Verify types compile**

```bash
cd nexo-main
npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/modules/sales/utils/saleAdapter.ts
git commit -m "feat: add SaleDto→CompletedSale adapter for API migration"
```

---

### Task 2: Connect Users module to `/api/users`

**Files:**
- Create: `src/modules/users/api/users.api.ts`
- Modify: `src/modules/users/services/userService.ts`
- Modify: `src/modules/users/pages/UserFormPage.tsx`

The backend `UserDto` differs from the frontend `User` type (no `store`, no `company`, no `createdBy`). We adapt in the service layer.

- [ ] **Step 1: Create `users.api.ts`**

Create `src/modules/users/api/users.api.ts`:

```typescript
import { apiClient } from "@/services/api-client";

export interface UserApiDto {
  id: string;
  tenantId: string;
  fullName: string;
  email: string;
  login: string;
  phone: string | null;
  role: string;
  status: string;
  requirePasswordChange: boolean;
  notes: string | null;
  lastAccessAt: string | null;
  passwordChangedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateUserPayload {
  fullName: string;
  email: string;
  login: string;
  password: string;
  role: string;
  phone?: string | null;
  notes?: string | null;
  requirePasswordChange?: boolean;
}

export interface UpdateUserPayload {
  fullName: string;
  email: string;
  role: string;
  phone?: string | null;
  notes?: string | null;
  status?: string | null;
}

export const listUsers = (): Promise<UserApiDto[]> =>
  apiClient.get<UserApiDto[]>("/users");

export const getUserById = (id: string): Promise<UserApiDto> =>
  apiClient.get<UserApiDto>(`/users/${id}`);

export const createUser = (payload: CreateUserPayload): Promise<UserApiDto> =>
  apiClient.post<UserApiDto>("/users", payload);

export const updateUser = (
  id: string,
  payload: UpdateUserPayload
): Promise<UserApiDto> =>
  apiClient.put<UserApiDto>(`/users/${id}`, payload);

export const changePassword = (
  id: string,
  currentPassword: string,
  newPassword: string
): Promise<void> =>
  apiClient.post<void>(`/users/${id}/change-password`, {
    currentPassword,
    newPassword,
  });

export const adminResetPassword = (
  id: string,
  newPassword: string
): Promise<void> =>
  apiClient.post<void>(`/users/${id}/admin-reset-password`, {
    newPassword,
  });
```

- [ ] **Step 2: Rewrite `userService.ts`**

Replace the contents of `src/modules/users/services/userService.ts`:

```typescript
import type { User, UserFormInput, PermissionMatrix, UserRole } from "../types";
import { rolePresets } from "../data/mockUsers";
import {
  listUsers,
  getUserById,
  createUser,
  updateUser,
  type UserApiDto,
} from "../api/users.api";

/** Maps the backend DTO to the frontend User shape */
function dtoToUser(dto: UserApiDto): User {
  return {
    id:                    dto.id,
    name:                  dto.fullName,
    email:                 dto.email,
    login:                 dto.login,
    phone:                 dto.phone ?? "",
    role:                  dto.role as UserRole,
    company:               "",       // not tracked per-user in this backend
    store:                 "",       // not tracked per-user in this backend
    status:                dto.status as User["status"],
    lastAccess:            dto.lastAccessAt,
    lastPasswordChange:    dto.passwordChangedAt,
    requirePasswordChange: dto.requirePasswordChange,
    notes:                 dto.notes ?? "",
    createdAt:             dto.createdAt,
    createdBy:             "",
    updatedAt:             dto.updatedAt,
  };
}

export const userService = {
  async list(): Promise<User[]> {
    const dtos = await listUsers();
    return dtos.map(dtoToUser);
  },

  async getById(id: string): Promise<User | undefined> {
    try {
      const dto = await getUserById(id);
      return dtoToUser(dto);
    } catch {
      return undefined;
    }
  },

  async create(input: UserFormInput): Promise<User> {
    const dto = await createUser({
      fullName:              input.name,
      email:                 input.email,
      login:                 input.login,
      password:              input.password ?? "nexo@temp",
      role:                  input.role,
      phone:                 input.phone || null,
      notes:                 input.notes || null,
      requirePasswordChange: input.requirePasswordChange,
    });
    return dtoToUser(dto);
  },

  async update(id: string, input: Partial<UserFormInput>): Promise<User> {
    const current = await this.getById(id);
    if (!current) throw new Error("Usuário não encontrado");
    const dto = await updateUser(id, {
      fullName: input.name    ?? current.name,
      email:    input.email   ?? current.email,
      role:     input.role    ?? current.role,
      phone:    input.phone   ?? current.phone || null,
      notes:    input.notes   ?? current.notes || null,
      status:   input.status  ?? current.status,
    });
    return dtoToUser(dto);
  },

  /** Returns store names. Backend has no store concept; returns empty list. */
  async listStores(): Promise<string[]> {
    return [];
  },

  async getPermissionsByRole(role: UserRole): Promise<PermissionMatrix> {
    const preset = rolePresets.find((p) => p.role === role);
    return preset ? { ...preset.permissions } : {};
  },

  async updatePermissions(role: UserRole, permissions: PermissionMatrix): Promise<void> {
    const preset = rolePresets.find((p) => p.role === role);
    if (preset) {
      preset.permissions = { ...permissions };
    }
  },

  /**
   * Manager authorization — still uses in-memory mock until Etapa 3 adds
   * a backend validate-manager endpoint. Returns a lightweight shape for POS.
   */
  validateManagerAuthorization(
    login: string,
    _password: string
  ): { success: true; user: User } | { success: false; error: string } {
    // TODO (Etapa 3): replace with POST /api/users/validate-manager
    return { success: false, error: "Autorização gerencial requer backend (Etapa 3)." };
  },
};
```

- [ ] **Step 3: Update `UserFormPage.tsx` password handling**

In `UserFormPage.tsx` (line ~30), the form's `save` handler calls `userService.create` or `userService.update`. Find the block that calls `userService.update` and add a password reset call when a new password was provided:

```typescript
// In UserFormPage, add after the successful update (around line 80-100):
// If password changed and it's an edit, use admin-reset-password
if (!isNew && form.password && form.password === form.passwordConfirm) {
  const { adminResetPassword } = await import("../api/users.api");
  await adminResetPassword(id!, form.password);
}
```

Read the full file first to find the exact location before editing.

- [ ] **Step 4: Verify no TypeScript errors**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 5: Smoke-test manually**

1. Navigate to /usuarios → list loads from API (no more mock data)
2. Create a user → appears in DB (check via backend logs)
3. Edit a user → updates persist on refresh

- [ ] **Step 6: Commit**

```bash
git add src/modules/users/api/users.api.ts src/modules/users/services/userService.ts src/modules/users/pages/UserFormPage.tsx
git commit -m "feat: connect users module to /api/users (drop mock data)"
```

---

### Task 3: Connect Settings module to `/api/settings`

**Files:**
- Create: `src/modules/settings/api/settings.api.ts`
- Modify: `src/modules/settings/services/settingsService.ts`

- [ ] **Step 1: Create `settings.api.ts`**

Create `src/modules/settings/api/settings.api.ts`:

```typescript
import { apiClient } from "@/services/api-client";
import type { AppSettings } from "../types";

export interface BackendSettingsDto {
  company: {
    name: string;
    tradeName: string;
    cnpj: string;
    email: string;
    phone: string;
  };
  operation: {
    defaultStore: string;
    defaultOperator: string;
  };
  inventory: {
    noMovementAlertDays: number;
    minStockBehavior: "alert" | "block" | "ignore";
    enableLowStockAlerts: boolean;
    enableZeroStockAlerts: boolean;
    enableHighRotationAlerts: boolean;
  };
  commission: {
    defaultCommissionRate: number;
    enableProductCommission: boolean;
    policyNotes: string;
  };
  pos: {
    allowValueDiscount: boolean;
    allowPercentDiscount: boolean;
    requireManagerAuth: boolean;
    maxDiscountPercent: number;
  };
  system: {
    language: string;
    dateFormat: string;
    currencySymbol: string;
  };
}

export const fetchSettings = (): Promise<BackendSettingsDto> =>
  apiClient.get<BackendSettingsDto>("/settings");

export const saveSettings = (settings: AppSettings): Promise<BackendSettingsDto> =>
  apiClient.put<BackendSettingsDto>("/settings", settings);
```

- [ ] **Step 2: Rewrite `settingsService.ts`**

Replace the contents of `src/modules/settings/services/settingsService.ts`:

```typescript
import type { AppSettings } from "../types";
import { fetchSettings, saveSettings } from "../api/settings.api";

/**
 * Settings service — now backed by /api/settings.
 * localStorage is no longer used.
 */
export const settingsService = {
  async getSettings(): Promise<AppSettings> {
    return fetchSettings() as Promise<AppSettings>;
  },

  async updateSettings(partial: Partial<AppSettings>): Promise<AppSettings> {
    // Merge partial with current, then persist full object
    const current = await this.getSettings();
    const merged: AppSettings = {
      company:    { ...current.company,    ...(partial.company    ?? {}) },
      operation:  { ...current.operation,  ...(partial.operation  ?? {}) },
      inventory:  { ...current.inventory,  ...(partial.inventory  ?? {}) },
      commission: { ...current.commission, ...(partial.commission ?? {}) },
      pos:        { ...current.pos,        ...(partial.pos        ?? {}) },
      system:     { ...current.system,     ...(partial.system     ?? {}) },
    };
    return saveSettings(merged) as Promise<AppSettings>;
  },
};
```

- [ ] **Step 3: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 4: Smoke-test**

1. Navigate to /configuracoes → settings load from DB
2. Change company name → save → refresh → name persists in DB (not localStorage)

- [ ] **Step 5: Commit**

```bash
git add src/modules/settings/api/settings.api.ts src/modules/settings/services/settingsService.ts
git commit -m "feat: connect settings module to /api/settings (drop localStorage)"
```

---

### Task 4: Add `AuditController` to backend + wire frontend

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Features/Audit/AuditDtos.cs`
- Create: `nexo-backend/src/Nexo.Application/Features/Audit/AuditService.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/AuditController.cs`
- Create: `src/modules/audit/api/audit.api.ts`
- Modify: `src/modules/audit/services/auditService.ts`
- Modify: `src/modules/audit/pages/AuditoriaPage.tsx`

The backend already writes `AuditRecord` entities to the DB via `AuditWriterService`. We only need to add a read endpoint.

- [ ] **Step 1: Create backend DTOs**

Create `nexo-backend/src/Nexo.Application/Features/Audit/AuditDtos.cs`:

```csharp
namespace Nexo.Application.Features.Audit;

public record AuditRecordDto(
    string Id,
    string Timestamp,         // ISO 8601
    string ActionType,
    string Severity,          // "info" | "warning" | "critical"
    string? ActorName,
    string ActorType,
    string EntityType,
    string EntityId,
    string Description,
    string? MetadataJson,
    string? IpAddress);

public record AuditListFilters(
    string? ActionType,   // null = all
    string? Severity,     // null = all
    string? Actor,        // substring search on ActorName
    string? From,         // ISO date yyyy-MM-dd
    string? To            // ISO date yyyy-MM-dd
);
```

- [ ] **Step 2: Create backend `AuditService`**

Create `nexo-backend/src/Nexo.Application/Features/Audit/AuditService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Application.Features.Audit;

public class AuditQueryService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public AuditQueryService(NexoDbContext db, ICurrentTenant currentTenant)
    {
        _db            = db;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetAsync(
        AuditListFilters filters,
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;

        var query = _db.AuditRecords
            .Where(r => r.TenantId == tenantId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filters.ActionType))
            query = query.Where(r => r.ActionType == filters.ActionType);

        if (!string.IsNullOrWhiteSpace(filters.Severity))
            query = query.Where(r => r.Severity == filters.Severity);

        if (!string.IsNullOrWhiteSpace(filters.Actor))
            query = query.Where(r =>
                r.ActorName != null &&
                EF.Functions.ILike(r.ActorName, $"%{filters.Actor}%"));

        if (!string.IsNullOrWhiteSpace(filters.From))
        {
            var from = DateOnly.Parse(filters.From).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (!string.IsNullOrWhiteSpace(filters.To))
        {
            var to = DateOnly.Parse(filters.To).ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt <= to);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(500)
            .Select(r => new AuditRecordDto(
                r.Id.ToString(),
                r.CreatedAt.ToString("o"),
                r.ActionType,
                r.Severity,
                r.ActorName,
                r.ActorType,
                r.EntityType,
                r.EntityId,
                r.Description,
                r.MetadataJson,
                r.IpAddress))
            .ToListAsync(ct);
    }

    public async Task<AuditStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;

        var stats = await _db.AuditRecords
            .Where(r => r.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total    = g.Count(),
                Critical = g.Count(r => r.Severity == "critical"),
                Warning  = g.Count(r => r.Severity == "warning"),
                Info     = g.Count(r => r.Severity == "info"),
            })
            .FirstOrDefaultAsync(ct);

        return new AuditStatsDto(
            stats?.Total    ?? 0,
            stats?.Critical ?? 0,
            stats?.Warning  ?? 0,
            stats?.Info     ?? 0);
    }
}

public record AuditStatsDto(int Total, int Critical, int Warning, int Info);
```

- [ ] **Step 3: Register `AuditQueryService` in DI**

Find the DI registration file (likely `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs` or `Program.cs`) and add:

```csharp
services.AddScoped<AuditQueryService>();
```

Search for where `UserService` or `SettingsService` is registered and add the line in the same block.

- [ ] **Step 4: Create `AuditController.cs`**

Create `nexo-backend/src/Nexo.Api/Controllers/AuditController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Audit;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly AuditQueryService _service;

    public AuditController(AuditQueryService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns audit records for the current tenant.
    /// Query params: actionType, severity, actor (substring), from (yyyy-MM-dd), to (yyyy-MM-dd)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditRecordDto>>> GetAll(
        [FromQuery] string? actionType,
        [FromQuery] string? severity,
        [FromQuery] string? actor,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var filters = new AuditListFilters(actionType, severity, actor, from, to);
        var records = await _service.GetAsync(filters, ct);
        return Ok(records);
    }

    /// <summary>Returns aggregated counts for the stats bar.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AuditStatsDto>> GetStats(CancellationToken ct)
    {
        var stats = await _service.GetStatsAsync(ct);
        return Ok(stats);
    }
}
```

- [ ] **Step 5: Build the backend to check for errors**

```bash
cd nexo-backend
dotnet build src/Nexo.Api
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Create frontend `audit.api.ts`**

Create `src/modules/audit/api/audit.api.ts`:

```typescript
import { apiClient } from "@/services/api-client";
import type { AuditRecord, AuditFilters } from "../types";

export interface AuditStats {
  total: number;
  critical: number;
  warning: number;
  info: number;
}

interface AuditApiRecord {
  id: string;
  timestamp: string;
  actionType: string;
  severity: string;
  actorName: string | null;
  actorType: string;
  entityType: string;
  entityId: string;
  description: string;
  metadataJson: string | null;
  ipAddress: string | null;
}

function mapRecord(r: AuditApiRecord): AuditRecord {
  return {
    id:          r.id,
    timestamp:   r.timestamp,
    actionType:  r.actionType as AuditRecord["actionType"],
    severity:    r.severity as AuditRecord["severity"],
    actor:       r.actorName ?? r.actorType,
    entityType:  r.entityType,
    entityId:    r.entityId,
    description: r.description,
    metadata:    r.metadataJson ? JSON.parse(r.metadataJson) : undefined,
  };
}

export async function fetchAuditRecords(
  filters?: Partial<AuditFilters>
): Promise<AuditRecord[]> {
  const params = new URLSearchParams();
  if (filters?.actionType && filters.actionType !== "all")
    params.set("actionType", filters.actionType);
  if (filters?.severity && filters.severity !== "all")
    params.set("severity", filters.severity);
  if (filters?.actor && filters.actor !== "all")
    params.set("actor", filters.actor);

  const url = `/audit${params.toString() ? `?${params}` : ""}`;
  const records = await apiClient.get<AuditApiRecord[]>(url);
  return records.map(mapRecord);
}

export async function fetchAuditStats(): Promise<AuditStats> {
  return apiClient.get<AuditStats>("/audit/stats");
}

export async function fetchAuditActors(): Promise<string[]> {
  // Compute from the full record list since there's no dedicated endpoint
  const records = await fetchAuditRecords();
  const actors = new Set(records.map((r) => r.actor).filter(Boolean));
  return Array.from(actors).sort();
}
```

- [ ] **Step 7: Rewrite `auditService.ts`**

Replace the contents of `src/modules/audit/services/auditService.ts`:

```typescript
import type { AuditRecord, AuditFilters } from "../types";
import {
  fetchAuditRecords,
  fetchAuditStats,
  fetchAuditActors,
  type AuditStats,
} from "../api/audit.api";

/**
 * Audit service — now backed by /api/audit.
 * Writing is handled entirely server-side (backend stages AuditRecord on every
 * business operation). The frontend never calls addAuditRecord anymore.
 */
export const auditService = {
  /**
   * @deprecated Writing audit records is handled by the backend.
   * Callers in userService/posService/cashService should be removed.
   */
  addAuditRecord(_input: unknown): void {
    // no-op — backend handles all audit writes
  },

  async listAuditRecords(filters?: AuditFilters): Promise<AuditRecord[]> {
    return fetchAuditRecords(filters);
  },

  async getAuditByEntity(
    entityType: string,
    entityId: string
  ): Promise<AuditRecord[]> {
    const all = await fetchAuditRecords();
    return all.filter(
      (r) => r.entityType === entityType && r.entityId === entityId
    );
  },

  async listActors(): Promise<string[]> {
    return fetchAuditActors();
  },

  async getStats(): Promise<AuditStats> {
    return fetchAuditStats();
  },
};
```

- [ ] **Step 8: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 9: Smoke-test**

1. Perform a business action (create a product)
2. Navigate to /auditoria
3. Confirm the record appears (written by backend, read via API)

- [ ] **Step 10: Commit**

```bash
git add \
  nexo-backend/src/Nexo.Application/Features/Audit/AuditDtos.cs \
  nexo-backend/src/Nexo.Application/Features/Audit/AuditService.cs \
  nexo-backend/src/Nexo.Api/Controllers/AuditController.cs \
  src/modules/audit/api/audit.api.ts \
  src/modules/audit/services/auditService.ts
git commit -m "feat: add AuditController + connect audit module to real DB"
```

---

### Task 5: Connect VendasPage to real sales API

**Files:**
- Modify: `src/modules/sales/pages/VendasPage.tsx`

`VendasPage` currently calls `salesHistoryService.listSales()` which internally calls `posService.getRecentSales()` (mock). We replace the data source with `listSales` from `sales.api.ts` + the adapter from Task 1.

- [ ] **Step 1: Rewrite VendasPage query**

Open `src/modules/sales/pages/VendasPage.tsx`. Find:

```typescript
import { salesHistoryService } from "../services/salesHistoryService";
```

Replace that import with:

```typescript
import { listSales } from "../api/sales.api";
import { saleToLegacy } from "../utils/saleAdapter";
```

Find the `useQuery` block:

```typescript
const { data: sales = [], isLoading, isError } = useQuery({
  queryKey: ["sales", filters],
  queryFn: () => salesHistoryService.listSales(filters),
});
```

Replace with:

```typescript
const { data: allSales = [], isLoading, isError } = useQuery({
  queryKey: ["sales"],
  queryFn:  () => listSales().then((dtos) => dtos.map(saleToLegacy)),
  staleTime: 30_000,
});

// Client-side filter (same logic as before)
const sales = useMemo(() => {
  let result = allSales;
  if (filters.search?.trim()) {
    const q = filters.search.trim().toLowerCase();
    result = result.filter(
      (s) =>
        s.id.toLowerCase().includes(q) ||
        s.operator.toLowerCase().includes(q) ||
        s.items.some(
          (i) =>
            i.description.toLowerCase().includes(q) ||
            i.code.toLowerCase().includes(q)
        ) ||
        (s.customerName ?? "").toLowerCase().includes(q)
    );
  }
  if (filters.paymentMethod && filters.paymentMethod !== "all") {
    result = result.filter((s) =>
      s.payments.some((p) => p.method === filters.paymentMethod)
    );
  }
  if (filters.status && filters.status !== "all") {
    result = result.filter((s) => s.status === filters.status);
  }
  return result;
}, [allSales, filters]);
```

Add `useMemo` to the imports at the top of the file.

- [ ] **Step 2: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/modules/sales/pages/VendasPage.tsx
git commit -m "feat: connect VendasPage to real sales API (drop posService mock)"
```

---

### Task 6: Connect VendaDetailPage to real sales API + fix cancel flow

**Files:**
- Modify: `src/modules/sales/pages/VendaDetailPage.tsx`

`VendaDetailPage` calls `salesHistoryService.getSaleById()` (mock) and `posService.cancelSale()` (mock). Replace both with real API calls.

- [ ] **Step 1: Rewrite VendaDetailPage**

Open `src/modules/sales/pages/VendaDetailPage.tsx`.

Replace the import block at the top:

```typescript
// Remove these:
import { salesHistoryService } from "../services/salesHistoryService";
import { posService } from "../services/posService";

// Add these:
import { getSale, cancelSale } from "../api/sales.api";
import { saleToLegacy } from "../utils/saleAdapter";
```

Replace the `useQuery` block:

```typescript
// Old:
const { data: sale, isLoading, isError } = useQuery({
  queryKey: ["sale", id],
  queryFn: () => salesHistoryService.getSaleById(id!),
  enabled: !!id,
});

// New:
const { data: sale, isLoading, isError } = useQuery({
  queryKey: ["sale", id],
  queryFn: () => getSale(id!).then(saleToLegacy),
  enabled: !!id,
});
```

Replace the `cancelSaleMutation`:

```typescript
// Old:
const cancelSaleMutation = useMutation({
  mutationFn: (payload: CancellationConfirmPayload) =>
    posService.cancelSale(
      id!,
      sale?.operator ?? "Operador",
      payload.authorizedBy,
      payload.reason
    ),
  ...
});

// New:
const cancelSaleMutation = useMutation({
  mutationFn: (_payload: CancellationConfirmPayload) =>
    cancelSale(id!),
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: ["sale", id] });
    queryClient.invalidateQueries({ queryKey: ["sales"] });
    toast.success("Venda cancelada com sucesso.");
    setCancelDialogOpen(false);
  },
  onError: () => {
    toast.error("Erro ao cancelar venda. Tente novamente.");
  },
});
```

Note: `toast` here refers to `sonner`'s `toast` — add the import if not present:
```typescript
import { toast } from "sonner";
```

- [ ] **Step 2: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 3: Smoke-test**

1. Navigate to /vendas → click a sale → details show correctly
2. Click "Cancelar venda" → confirm → sale status updates in real time

- [ ] **Step 4: Commit**

```bash
git add src/modules/sales/pages/VendaDetailPage.tsx
git commit -m "feat: connect VendaDetailPage to real API + fix cancel to use backend"
```

---

### Task 7: Connect Reports module to real data

**Files:**
- Modify: `src/modules/reports/services/reportService.ts`

The `reportService` is a pure computation layer. All its data comes from `posService.getRecentSales()` (mock). Replace it with `listSales()` from the real API, using the adapter.

- [ ] **Step 1: Replace data source in reportService**

Open `src/modules/reports/services/reportService.ts`.

Replace the import block:

```typescript
// Remove:
import { posService } from "@/modules/sales/services/posService";
import { cashService } from "@/modules/cash/services/cashService";
import { inventoryService } from "@/modules/inventory/services/inventoryService";
import { commissionService } from "@/modules/commissions/services/commissionService";
import type { CompletedSale } from "@/modules/sales/types";

// Add:
import { listSales } from "@/modules/sales/api/sales.api";
import { saleToLegacy } from "@/modules/sales/utils/saleAdapter";
import { cashService } from "@/modules/cash/services/cashService";
import { inventoryService } from "@/modules/inventory/services/inventoryService";
import { commissionService } from "@/modules/commissions/services/commissionService";
import type { CompletedSale } from "@/modules/sales/types";
```

Add a helper at the top of the service:

```typescript
async function getAllSales(): Promise<CompletedSale[]> {
  const dtos = await listSales();
  return dtos.map(saleToLegacy);
}
```

Replace every `posService.getRecentSales()` call in the file with `getAllSales()`.

There are 5 occurrences (in `getOperationalSummary`, `getSalesByOperator`, `getTopProducts`, `getCancellationSummary`, `listOperators`).

- [ ] **Step 2: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/modules/reports/services/reportService.ts
git commit -m "feat: connect reports module to real sales API (drop posService mock)"
```

---

## Etapa 2 — Domain Consistency Verification

### Task 8: Verify sale → stock → cash pipeline end-to-end

**Files:**
- Read: `nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs` (verify stock decrement)
- No code changes — verification only

- [ ] **Step 1: Check SaleService for stock decrement**

```bash
grep -n "stock\|Stock\|inventory\|Inventory" \
  "nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs"
```

Expected: Lines referencing stock item update or movement creation on `ConfirmAsync`.

If stock decrement is MISSING, add it in the `ConfirmAsync` method following the same pattern as inventory adjustments. If present, continue.

- [ ] **Step 2: Check SaleService for cash movement**

```bash
grep -n "cash\|Cash\|session\|Session" \
  "nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs"
```

Expected: Lines creating a `CashMovement` of type `Sale` with the total amount on `ConfirmAsync`.

If cash movement is MISSING, add it. If present, continue.

- [ ] **Step 3: Check restaurante order close → sale creation**

```bash
grep -rn "Sale.Create\|openOrder\|SalesController\|createSale" \
  "nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante"
```

Expected: The restaurante `OrdersController.Close` calls `SalesService.ConfirmAsync` or creates a `Sale` entity.

If MISSING, this is a critical gap — note it for a separate task.

- [ ] **Step 4: Manual end-to-end test**

1. In PDV: add product (say, 5 units of "Produto A"), finalize sale
2. Check Estoque: quantity of "Produto A" should decrease by 5
3. Check Caixa: cash session "Movimentações" should show a sale entry
4. Check Auditoria: should show `sale_completed` record

If all pass: domain consistency is confirmed. If any fail: document the gap and fix before proceeding.

- [ ] **Step 5: Commit findings**

No code changes needed if tests pass. If fixes were required:

```bash
git add nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs
git commit -m "fix: ensure sale confirm decrements stock and creates cash movement"
```

---

## Etapa 3 — PDV Gaps

### Task 9: Backend — add `POST /api/users/validate-manager` endpoint

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Features/Users/UserDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Features/Users/UserService.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/UsersController.cs`
- Modify: `src/modules/users/api/users.api.ts`
- Modify: `src/modules/users/services/userService.ts`

- [ ] **Step 1: Add request/response DTOs**

In `UserDtos.cs`, append:

```csharp
public record ValidateManagerRequest(string Login, string Password);
public record ValidateManagerResponse(bool Success, string? ErrorMessage, string? FullName, string? Role);
```

- [ ] **Step 2: Add `ValidateManagerAsync` to UserService**

In `UserService.cs`, add the method:

```csharp
public async Task<ValidateManagerResponse> ValidateManagerAsync(
    ValidateManagerRequest request,
    CancellationToken ct = default)
{
    var user = await _users.GetByLoginAsync(request.Login, ct);
    if (user is null)
        return new ValidateManagerResponse(false, "Usuário não encontrado.", null, null);

    if (user.Status != UserStatus.Active)
        return new ValidateManagerResponse(false, "Usuário inativo ou bloqueado.", null, null);

    if (user.Role != UserRole.Gerente && user.Role != UserRole.Diretoria)
        return new ValidateManagerResponse(false, "Usuário não possui autorização gerencial.", null, null);

    var valid = _hasher.Verify(request.Password, user.PasswordHash);
    if (!valid)
    {
        _audit.Stage(
            actionType:  AuditActions.ManagerAuthorization,
            severity:    "critical",
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"Failed manager authorization attempt for '{request.Login}'.",
            tenantId:    _currentTenant.Id,
            actorId:     user.Id,
            actorName:   user.FullName);
        await _users.SaveChangesAsync(ct);
        return new ValidateManagerResponse(false, "Senha incorreta.", null, null);
    }

    _audit.Stage(
        actionType:  AuditActions.ManagerAuthorization,
        severity:    "warning",
        entityType:  "User",
        entityId:    user.Id.ToString(),
        description: $"Manager authorization granted to '{user.FullName}' ({user.Role}).",
        tenantId:    _currentTenant.Id,
        actorId:     user.Id,
        actorName:   user.FullName);
    await _users.SaveChangesAsync(ct);

    return new ValidateManagerResponse(
        true, null, user.FullName, user.Role.ToString().ToLower());
}
```

If `GetByLoginAsync` does not exist in `IUserRepository`, add it:
```csharp
// In IUserRepository:
Task<User?> GetByLoginAsync(string login, CancellationToken ct = default);
// In UserRepository:
public async Task<User?> GetByLoginAsync(string login, CancellationToken ct)
    => await _db.Set<User>().FirstOrDefaultAsync(u => u.Login == login, ct);
```

If `AuditActions.ManagerAuthorization` constant does not exist, add it to wherever `AuditActions` is defined.

- [ ] **Step 3: Add endpoint to UsersController**

In `UsersController.cs`, add:

```csharp
/// <summary>
/// Validates manager-level credentials for authorizing sensitive operations
/// such as sale cancellations. Does NOT require Gerente/Diretoria calling role —
/// the POS is typically run by a Vendedor who is asking for manager approval.
/// </summary>
[HttpPost("validate-manager")]
public async Task<ActionResult<ValidateManagerResponse>> ValidateManager(
    [FromBody] ValidateManagerRequest request,
    CancellationToken ct)
{
    var result = await _userService.ValidateManagerAsync(request, ct);
    return Ok(result);
}
```

- [ ] **Step 4: Build backend**

```bash
cd nexo-backend && dotnet build src/Nexo.Api
```

Expected: 0 errors.

- [ ] **Step 5: Add API function to `users.api.ts`**

Append to `src/modules/users/api/users.api.ts`:

```typescript
export interface ValidateManagerResult {
  success: boolean;
  errorMessage: string | null;
  fullName: string | null;
  role: string | null;
}

export const validateManager = (
  login: string,
  password: string
): Promise<ValidateManagerResult> =>
  apiClient.post<ValidateManagerResult>("/users/validate-manager", {
    login,
    password,
  });
```

- [ ] **Step 6: Update `validateManagerAuthorization` in `userService.ts`**

Replace the stub:

```typescript
validateManagerAuthorization(
  login: string,
  _password: string
): { success: true; user: User } | { success: false; error: string } {
  // TODO (Etapa 3): replace with POST /api/users/validate-manager
  return { success: false, error: "Autorização gerencial requer backend (Etapa 3)." };
},
```

With an async real call. However, since `validateManagerAuthorization` is called synchronously from POS, refactor callers to be async:

```typescript
async validateManagerAuthorizationAsync(
  login: string,
  password: string
): Promise<{ success: true; user: User } | { success: false; error: string }> {
  const result = await validateManager(login, password);
  if (!result.success) {
    return { success: false, error: result.errorMessage ?? "Autorização negada." };
  }
  // Build minimal user shape (caller may need name/role only)
  const user: User = {
    id: "", name: result.fullName ?? login, email: "", login,
    phone: "", role: result.role as UserRole, company: "", store: "",
    status: "active", lastAccess: null, lastPasswordChange: null,
    requirePasswordChange: false, notes: "", createdAt: "", createdBy: "", updatedAt: "",
  };
  return { success: true, user };
},
```

Find all callers of `userService.validateManagerAuthorization` (search in `posService.ts` and `SaleCancellationDialog`) and make them async.

- [ ] **Step 7: Commit**

```bash
git add \
  nexo-backend/src/Nexo.Application/Features/Users/UserDtos.cs \
  nexo-backend/src/Nexo.Application/Features/Users/UserService.cs \
  nexo-backend/src/Nexo.Api/Controllers/UsersController.cs \
  src/modules/users/api/users.api.ts \
  src/modules/users/services/userService.ts
git commit -m "feat: add validate-manager endpoint for POS cancellation authorization"
```

---

### Task 10: Verify cash integration with PDV

**Files:**
- Read: `nexo-backend/src/Nexo.Application/Features/Cash/CashService.cs`

- [ ] **Step 1: Verify `use-pos-sale.ts` passes cashSessionId**

Open `src/modules/sales/hooks/use-pos-sale.ts`. Confirm it passes `cashSessionId` in the sale request.

Expected: something like:
```typescript
cashSessionId: items.cashSessionId
```

If missing, check what the `useCompleteSale` mutation sends and compare to `SalesController.Confirm`.

- [ ] **Step 2: Verify backend creates cash movement on confirm**

```bash
grep -n "CashMovement\|cashSession\|Cash" \
  "nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs" | head -20
```

If no CashMovement creation found in `ConfirmAsync`, add:

```csharp
// In ConfirmAsync, after confirming sale:
if (sale.CashSessionId.HasValue)
{
    var movement = CashMovement.Create(
        sessionId:     sale.CashSessionId.Value,
        type:          CashMovementType.Sale,
        amount:        sale.Total,
        description:   $"Venda #{sale.Number}",
        paymentMethod: request.Payments.FirstOrDefault()?.Method.ToString() ?? "Cash",
        referenceId:   sale.Id.ToString());
    _db.Set<CashMovement>().Add(movement);
}
```

- [ ] **Step 3: Commit any fixes**

```bash
git add nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs
git commit -m "fix: create cash movement when sale is confirmed via PDV"
```

---

## Etapa 4 — Core Reports

### Task 11: Backend `ReportsController` with sales, inventory, customers summaries

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Features/Reports/ReportsDtos.cs`
- Create: `nexo-backend/src/Nexo.Application/Features/Reports/ReportsService.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/ReportsController.cs`

- [ ] **Step 1: Create DTOs**

Create `nexo-backend/src/Nexo.Application/Features/Reports/ReportsDtos.cs`:

```csharp
namespace Nexo.Application.Features.Reports;

public record SalesReportDto(
    int TotalSales,
    int CancelledSales,
    decimal TotalRevenue,
    decimal AverageTicket,
    decimal CancelledValue,
    string From,
    string To);

public record InventoryReportDto(
    int TotalProducts,
    int ZeroStockCount,
    int LowStockCount,
    int NormalCount,
    decimal TotalStockValue,
    int AlertCount);

public record CustomerReportDto(
    int TotalCustomers,
    int NewThisMonth,
    int WithPurchases,
    decimal AveragePurchaseValue);
```

- [ ] **Step 2: Create `ReportsService`**

Create `nexo-backend/src/Nexo.Application/Features/Reports/ReportsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Application.Features.Reports;

public class ReportsService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public ReportsService(NexoDbContext db, ICurrentTenant currentTenant)
    {
        _db            = db;
        _currentTenant = currentTenant;
    }

    public async Task<SalesReportDto> GetSalesReportAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;
        var fromUtc  = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc    = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var sales = await _db.Set<Nexo.Domain.Entities.Sale>()
            .Where(s => s.TenantId == tenantId
                     && s.CreatedAt >= fromUtc
                     && s.CreatedAt <= toUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        var active    = sales.Where(s => s.Status != SaleStatus.Cancelled).ToList();
        var cancelled = sales.Where(s => s.Status == SaleStatus.Cancelled).ToList();

        var revenue       = active.Sum(s => s.Total);
        var avgTicket     = active.Count > 0 ? revenue / active.Count : 0;
        var cancelledVal  = cancelled.Sum(s => s.Total);

        return new SalesReportDto(
            TotalSales:     sales.Count,
            CancelledSales: cancelled.Count,
            TotalRevenue:   Math.Round(revenue, 2),
            AverageTicket:  Math.Round(avgTicket, 2),
            CancelledValue: Math.Round(cancelledVal, 2),
            From:           from.ToString("yyyy-MM-dd"),
            To:             to.ToString("yyyy-MM-dd"));
    }

    public async Task<InventoryReportDto> GetInventoryReportAsync(
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;

        var items = await _db.Set<Nexo.Domain.Entities.StockItem>()
            .Include(s => s.Product)
            .Where(s => s.TenantId == tenantId && s.Product.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        var zero   = items.Count(i => i.CurrentQuantity == 0);
        var low    = items.Count(i => i.CurrentQuantity > 0
                                   && i.Product.MinStockQuantity.HasValue
                                   && i.CurrentQuantity < i.Product.MinStockQuantity);
        var normal = items.Count - zero - low;
        var alerts = zero + low;
        var totalVal = items.Sum(i => i.CurrentQuantity * i.Product.CostPrice);

        return new InventoryReportDto(
            TotalProducts:   items.Count,
            ZeroStockCount:  zero,
            LowStockCount:   low,
            NormalCount:     normal,
            TotalStockValue: Math.Round(totalVal, 2),
            AlertCount:      alerts);
    }

    public async Task<CustomerReportDto> GetCustomerReportAsync(
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;
        var now      = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalCustomers = await _db.Set<Nexo.Domain.Entities.Customer>()
            .CountAsync(c => c.TenantId == tenantId && c.IsActive, ct);

        var newThisMonth = await _db.Set<Nexo.Domain.Entities.Customer>()
            .CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= monthStart, ct);

        // Customers with at least one paid sale
        var withPurchases = await _db.Set<Nexo.Domain.Entities.Sale>()
            .Where(s => s.TenantId == tenantId
                     && s.Status == SaleStatus.Paid
                     && s.CustomerId != null)
            .Select(s => s.CustomerId)
            .Distinct()
            .CountAsync(ct);

        // Average sale value across all paid sales with a customer
        var avgPurchase = await _db.Set<Nexo.Domain.Entities.Sale>()
            .Where(s => s.TenantId == tenantId
                     && s.Status == SaleStatus.Paid
                     && s.CustomerId != null)
            .Select(s => (decimal?)s.Total)
            .AverageAsync(ct) ?? 0m;

        return new CustomerReportDto(
            TotalCustomers:      totalCustomers,
            NewThisMonth:        newThisMonth,
            WithPurchases:       withPurchases,
            AveragePurchaseValue: Math.Round(avgPurchase, 2));
    }
}
```

- [ ] **Step 3: Register ReportsService in DI**

Find the DI setup file and add:

```csharp
services.AddScoped<ReportsService>();
```

- [ ] **Step 4: Create `ReportsController.cs`**

Create `nexo-backend/src/Nexo.Api/Controllers/ReportsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Reports;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ReportsService _service;

    public ReportsController(ReportsService service)
    {
        _service = service;
    }

    /// <summary>
    /// Sales summary for a date range.
    /// Query params: from (yyyy-MM-dd), to (yyyy-MM-dd). Defaults to current month.
    /// </summary>
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportDto>> GetSales(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = from is not null ? DateOnly.Parse(from) : new DateOnly(today.Year, today.Month, 1);
        var toDate   = to   is not null ? DateOnly.Parse(to)   : today;

        var report = await _service.GetSalesReportAsync(fromDate, toDate, ct);
        return Ok(report);
    }

    /// <summary>Inventory health snapshot.</summary>
    [HttpGet("inventory")]
    public async Task<ActionResult<InventoryReportDto>> GetInventory(CancellationToken ct)
    {
        var report = await _service.GetInventoryReportAsync(ct);
        return Ok(report);
    }

    /// <summary>Customer activity summary.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<CustomerReportDto>> GetCustomers(CancellationToken ct)
    {
        var report = await _service.GetCustomerReportAsync(ct);
        return Ok(report);
    }
}
```

- [ ] **Step 5: Build backend**

```bash
cd nexo-backend && dotnet build src/Nexo.Api
```

Expected: 0 errors.

- [ ] **Step 6: Commit backend**

```bash
cd nexo-backend && git add \
  src/Nexo.Application/Features/Reports/ReportsDtos.cs \
  src/Nexo.Application/Features/Reports/ReportsService.cs \
  src/Nexo.Api/Controllers/ReportsController.cs
git commit -m "feat: add ReportsController with sales/inventory/customers summaries"
```

---

### Task 12: Frontend — connect Reports page and Dashboard to real API

**Files:**
- Create: `src/modules/reports/api/reports.api.ts`
- Modify: `src/modules/reports/services/reportService.ts` (sales, inventory summaries)
- Modify: `src/modules/reports/pages/RelatoriosPage.tsx`

- [ ] **Step 1: Create `reports.api.ts`**

Create `src/modules/reports/api/reports.api.ts`:

```typescript
import { apiClient } from "@/services/api-client";

export interface SalesReportDto {
  totalSales:     number;
  cancelledSales: number;
  totalRevenue:   number;
  averageTicket:  number;
  cancelledValue: number;
  from:           string;
  to:             string;
}

export interface InventoryReportDto {
  totalProducts:   number;
  zeroStockCount:  number;
  lowStockCount:   number;
  normalCount:     number;
  totalStockValue: number;
  alertCount:      number;
}

export interface CustomerReportDto {
  totalCustomers:       number;
  newThisMonth:         number;
  withPurchases:        number;
  averagePurchaseValue: number;
}

export const fetchSalesReport = (
  from?: string,
  to?: string
): Promise<SalesReportDto> => {
  const params = new URLSearchParams();
  if (from) params.set("from", from);
  if (to)   params.set("to", to);
  return apiClient.get<SalesReportDto>(`/reports/sales${params.toString() ? `?${params}` : ""}`);
};

export const fetchInventoryReport = (): Promise<InventoryReportDto> =>
  apiClient.get<InventoryReportDto>("/reports/inventory");

export const fetchCustomerReport = (): Promise<CustomerReportDto> =>
  apiClient.get<CustomerReportDto>("/reports/customers");
```

- [ ] **Step 2: Update `RelatoriosPage.tsx`**

Open `src/modules/reports/pages/RelatoriosPage.tsx`.

Add the import:
```typescript
import { fetchSalesReport } from "../api/reports.api";
```

Find the `getOperationalSummary` query (uses `reportService`) and add a new enrichment query below it:

```typescript
const { data: apiReport } = useQuery({
  queryKey: ["api-sales-report"],
  queryFn:  () => fetchSalesReport(),
  staleTime: 5 * 60_000,
});
```

Use `apiReport` data to complement or replace the existing `operational` summary. At minimum show `apiReport?.totalRevenue` alongside the existing display.

The existing `reportService` queries can stay since they also use real data now (Task 7). The API report adds precise server-side aggregation as a second source.

- [ ] **Step 3: Verify TypeScript**

```bash
cd nexo-main && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add \
  src/modules/reports/api/reports.api.ts \
  src/modules/reports/pages/RelatoriosPage.tsx
git commit -m "feat: add reports.api.ts + wire RelatoriosPage to /api/reports/sales"
```

---

### Task 13: Final cleanup — remove dead mock files and stale imports

**Files:**
- Delete: `src/modules/sales/services/posService.ts` (if no longer imported anywhere)
- Delete: `src/modules/sales/services/salesHistoryService.ts`
- Delete: `src/modules/sales/data/mockSales.ts` (if no references remain)
- Delete: `src/modules/sales/data/mockPosProducts.ts` (if no references remain)
- Modify: `src/modules/users/services/userService.ts` (remove auditService import)
- Modify: `src/modules/cash/services/cashService.ts` (verify auditService.addAuditRecord calls are no-ops or can be removed)

- [ ] **Step 1: Check for remaining consumers of mock services**

```bash
cd nexo-main
grep -rn "posService\|salesHistoryService\|mockSales\|mockPosProducts" src/ --include="*.ts" --include="*.tsx"
```

If any results appear outside of the dead files themselves, fix those imports first before deleting.

- [ ] **Step 2: Delete dead files**

Only delete a file if the grep above shows zero consumers outside of that file itself:

```bash
rm src/modules/sales/services/salesHistoryService.ts
# If posService has no real consumers:
rm src/modules/sales/services/posService.ts
rm src/modules/sales/data/mockSales.ts
rm src/modules/sales/data/mockPosProducts.ts
```

- [ ] **Step 3: Remove auditService calls from userService**

Open `src/modules/users/services/userService.ts`. Remove the import of `auditService` (no longer needed since backend handles audit writes).

- [ ] **Step 4: Verify nothing is broken**

```bash
cd nexo-main && npx tsc --noEmit && npm run build
```

Expected: 0 errors, build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove dead mock services and stale auditService.addAuditRecord calls"
```

---

## Self-Review

### Spec Coverage Check

| Requirement | Task |
|---|---|
| Etapa 0: DB reset + validation | Task 0 |
| Etapa 1: Users → real API | Task 2 |
| Etapa 1: Settings → real API | Task 3 |
| Etapa 1: Audit → real DB | Task 4 |
| Etapa 1: Sales history → real API | Tasks 5, 6 |
| Etapa 1: Reports → real data | Task 7 |
| Etapa 2: Domain consistency | Task 8 |
| Etapa 3: Cancel flow real | Task 6 |
| Etapa 3: Manager auth real | Task 9 |
| Etapa 3: Cash integration | Task 10 |
| Etapa 4: /api/reports/* endpoints | Task 11 |
| Etapa 4: Dashboard/Reports wired | Task 12 |
| Cleanup | Task 13 |

### Known Constraints

- **PermissoesPage** (`/usuarios/permissoes`): the permission matrix is a frontend-only concept (stored in-memory in `rolePresets`). The backend currently has no permissions storage. This page remains mock-backed. A future task would add a `RolePermissions` table to the backend.

- **manager authorization in POS cancel**: Task 9 adds the backend endpoint, but callers in `SaleCancellationDialog` and `posService` need to be made async. Read those files carefully before editing.

- **`posService.completeSale`**: `PdvPage` uses `useCompleteSale` hook (real API) — `posService.completeSale` is no longer called from there. After Task 13's cleanup grep, if `posService` is still imported anywhere (e.g., `SellerRanking`, `TopProducts`), update those first.
