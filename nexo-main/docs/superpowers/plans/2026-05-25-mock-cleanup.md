# Mock Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove every mock service, fake data file, and dead route from the NexoERP frontend so every visible feature either calls the real backend or is completely absent from the UI.

**Architecture:** Three types of work: (1) modify live routed pages to remove mock-dependent features, (2) remove dead modules (not routed, zero live imports), (3) delete orphaned mock data/service files. Changes are ordered so no deleted file is still imported when it disappears.

**Tech Stack:** React 18, TypeScript, Vite, TanStack Query, React Router v6

---

## Findings Summary

### Live routed pages with mock dependencies (must fix, not just delete)

| Page | Route | Mock used | Fix |
|------|-------|-----------|-----|
| `VendaDetailPage.tsx` | `/vendas/:id` | `posService.cancelSaleItem` (in-memory) | Remove item-cancel mutation; full-sale cancel (real API) stays |
| `PermissoesPage.tsx` | `/usuarios/permissoes` | `userService.getPermissionsByRole` / `updatePermissions` → `rolePresets` in memory | Remove route + nav button |
| `TopProducts.tsx` | Dashboard component | `<Link to="/relatorios">` → 404 (no route) | Remove the link |

### Dead modules (not routed, safe to delete entirely)

| Module path | Why deletable |
|-------------|---------------|
| `src/modules/commissions/` | Not in AppRouter, not imported by any live file |
| `src/modules/insights/` | Not in AppRouter, not imported by any live file |
| `src/modules/reports/pages/RelatoriosPage.tsx` | Not in AppRouter (only restaurante one is routed) |
| `src/modules/reports/services/reportService.ts` | Only used by deleted RelatoriosPage + insightService |
| `src/modules/reports/components/` (4 files) | Only used by deleted RelatoriosPage |
| `src/modules/reports/types/index.ts` | Only used by deleted RelatoriosPage/reportService |
| `src/modules/reports/api/reports.api.ts` | Only used by deleted RelatoriosPage |
| `src/modules/sales/pages/OrcamentosPage.tsx` | Not in AppRouter |
| `src/modules/sales/pages/OrcamentoFormPage.tsx` | Not in AppRouter |
| `src/modules/sales/services/quotationService.ts` | Only used by deleted Orcamento pages |
| `src/modules/sales/data/mockQuotations.ts` | Only used by quotationService |
| `src/modules/sales/types/quotation.ts` | Only used by deleted files |
| `src/modules/sales/components/Quotation*.tsx` (5 files) | Only used by deleted Orcamento pages |
| `src/modules/inventory/pages/TransferenciasPage.tsx` | Not in AppRouter |
| `src/modules/inventory/components/InventoryTransferForm.tsx` | Only used by TransferenciasPage |

### Mock services/data deletable after live fixes

| File | Condition |
|------|-----------|
| `src/modules/sales/services/posService.ts` | After removing from VendaDetailPage (Task 1) |
| `src/modules/sales/data/mockSales.ts` | Only used by posService |
| `src/modules/sales/data/mockPosProducts.ts` | Only used by posService |
| `src/modules/cash/services/cashService.ts` | Only used by posService + reportService (both deleted) |
| `src/modules/cash/data/mockCash.ts` | Only used by cashService |
| `src/modules/inventory/services/inventoryService.ts` | Only used by posService + InventoryTransferForm (both deleted) |
| `src/modules/inventory/data/mockInventory.ts` | Only used by inventoryService |
| `src/modules/customers/services/customerService.ts` | Empty (`export {}`), only used by deleted Orcamento pages |
| `src/modules/customers/data/mockCustomers.ts` | Zero imports outside own file |
| `src/modules/products/data/mockProducts.ts` | Zero imports outside own file |
| `src/modules/suppliers/data/mockSuppliers.ts` | Zero imports outside own file |
| `src/modules/audit/data/mockAuditRecords.ts` | Zero imports outside own file |
| `src/modules/users/data/mockUsers.ts` | After removing rolePresets from userService (Task 5) |
| `src/modules/users/pages/PermissoesPage.tsx` | After removing route (Task 2) |

---

## Task 1: Fix VendaDetailPage — remove mock item-cancel

**Files:**
- Modify: `nexo-main/src/modules/sales/pages/VendaDetailPage.tsx`

- [ ] **Step 1: Remove posService import and cancelItemMutation**

Replace the top of the file — remove `import { posService }` line and remove the `cancelItemMutation` useMutation block entirely.

```tsx
// Remove this line:
// import { posService } from "../services/posService";

// Remove entire block (lines ~51-78):
// const cancelItemMutation = useMutation({
//   mutationFn: ({ itemProductId, payload }) =>
//     posService.cancelSaleItem(id!, itemProductId, ...),
//   onSuccess: () => { ... },
// });
```

- [ ] **Step 2: Remove onCancelItem prop from SaleItemsTable call**

```tsx
// Change this:
<SaleItemsTable
  items={sale.items}
  sale={sale}
  onCancelItem={async (itemProductId, payload) => {
    await cancelItemMutation.mutateAsync({ itemProductId, payload });
  }}
/>

// To this:
<SaleItemsTable
  items={sale.items}
  sale={sale}
/>
```

(`canCancelItems = !!onCancelItem && ...` in SaleItemsTable — button simply won't render when prop is absent.)

- [ ] **Step 3: Verify tsc**

```bash
cd nexo-main && npx tsc --noEmit
```
Expected: 0 errors

---

## Task 2: Remove PermissoesPage from router and nav

**Files:**
- Modify: `nexo-main/src/app/router/AppRouter.tsx`
- Modify: `nexo-main/src/modules/users/pages/UsuariosPage.tsx`

- [ ] **Step 1: Remove from AppRouter**

```tsx
// Remove this lazy import:
// const PermissoesPage = lazy(() => import("@/modules/users/pages/PermissoesPage"));

// Remove this route:
// <Route path="/usuarios/permissoes" element={<PermissoesPage />} />
```

- [ ] **Step 2: Remove "Permissões" button from UsuariosPage**

In `UsuariosPage.tsx` around line 52, remove:
```tsx
// Remove:
<Button variant="outline" onClick={() => navigate("/usuarios/permissoes")}>
  <UserCog className="h-4 w-4 mr-2" /> Permissões
</Button>
```

- [ ] **Step 3: Verify tsc**

```bash
cd nexo-main && npx tsc --noEmit
```

---

## Task 3: Remove dead link to /relatorios from TopProducts

**Files:**
- Modify: `nexo-main/src/modules/dashboard/components/TopProducts.tsx`

- [ ] **Step 1: Remove the Link import + link element**

Remove `import { Link } from "react-router-dom"` if it's only used for this link.
Remove:
```tsx
{products.length > 0 && (
  <Link
    to="/relatorios"
    className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
  >
    Ver relatório
  </Link>
)}
```

- [ ] **Step 2: Verify tsc**

```bash
cd nexo-main && npx tsc --noEmit
```

---

## Task 4: Delete dead modules — commissions, insights, reports (general), quotations, inventory dead files

**Files to delete:**

```
src/modules/commissions/  (entire directory)
src/modules/insights/     (entire directory)
src/modules/reports/pages/RelatoriosPage.tsx
src/modules/reports/services/reportService.ts
src/modules/reports/components/ReportFilterBar.tsx
src/modules/reports/components/ReportKpiCards.tsx
src/modules/reports/components/SalesByOperatorTable.tsx
src/modules/reports/components/TopProductsTable.tsx
src/modules/reports/types/index.ts
src/modules/reports/api/reports.api.ts
src/modules/sales/pages/OrcamentosPage.tsx
src/modules/sales/pages/OrcamentoFormPage.tsx
src/modules/sales/services/quotationService.ts
src/modules/sales/data/mockQuotations.ts
src/modules/sales/types/quotation.ts
src/modules/sales/components/QuotationFilters.tsx
src/modules/sales/components/QuotationItemsEditor.tsx
src/modules/sales/components/QuotationStatusBadge.tsx
src/modules/sales/components/QuotationTable.tsx
src/modules/sales/components/QuotationTotalsCard.tsx
src/modules/inventory/pages/TransferenciasPage.tsx
src/modules/inventory/components/InventoryTransferForm.tsx
```

- [ ] **Step 1: Delete dead module directories and files**

```bash
cd nexo-main
rm -rf src/modules/commissions
rm -rf src/modules/insights
rm -f src/modules/reports/pages/RelatoriosPage.tsx
rm -f src/modules/reports/services/reportService.ts
rm -f src/modules/reports/components/ReportFilterBar.tsx
rm -f src/modules/reports/components/ReportKpiCards.tsx
rm -f src/modules/reports/components/SalesByOperatorTable.tsx
rm -f src/modules/reports/components/TopProductsTable.tsx
rm -f src/modules/reports/types/index.ts
rm -f src/modules/reports/api/reports.api.ts
rm -f src/modules/sales/pages/OrcamentosPage.tsx
rm -f src/modules/sales/pages/OrcamentoFormPage.tsx
rm -f src/modules/sales/services/quotationService.ts
rm -f src/modules/sales/data/mockQuotations.ts
rm -f src/modules/sales/types/quotation.ts
rm -f src/modules/sales/components/QuotationFilters.tsx
rm -f src/modules/sales/components/QuotationItemsEditor.tsx
rm -f src/modules/sales/components/QuotationStatusBadge.tsx
rm -f src/modules/sales/components/QuotationTable.tsx
rm -f src/modules/sales/components/QuotationTotalsCard.tsx
rm -f src/modules/inventory/pages/TransferenciasPage.tsx
rm -f src/modules/inventory/components/InventoryTransferForm.tsx
```

- [ ] **Step 2: Verify tsc**

```bash
cd nexo-main && npx tsc --noEmit
```
Expected: 0 errors (none of these files were imported by live code)

---

## Task 5: Delete mock services and data — posService, cashService, inventoryService, mock data

**Files:**
- Modify: `nexo-main/src/modules/users/services/userService.ts` (remove rolePresets import + 2 methods)

- [ ] **Step 1: Clean userService.ts**

Remove from `userService.ts`:
```ts
// Remove this import:
import { rolePresets } from "../data/mockUsers";

// Remove these two methods:
async getPermissionsByRole(role: UserRole): Promise<PermissionMatrix> {
  const preset = rolePresets.find((p) => p.role === role);
  return preset ? { ...preset.permissions } : {};
},

async updatePermissions(role: UserRole, permissions: PermissionMatrix): Promise<void> {
  const preset = rolePresets.find((p) => p.role === role);
  if (preset) preset.permissions = { ...permissions };
},
```

Also remove the `PermissionMatrix` type import from `../types` if it's only used by these methods. Check: `import type { User, UserFormInput, PermissionMatrix, UserRole }` — remove `PermissionMatrix` from this import.

- [ ] **Step 2: Delete orphaned mock services and data files**

```bash
cd nexo-main
rm -f src/modules/sales/services/posService.ts
rm -f src/modules/sales/data/mockSales.ts
rm -f src/modules/sales/data/mockPosProducts.ts
rm -f src/modules/cash/services/cashService.ts
rm -f src/modules/cash/data/mockCash.ts
rm -f src/modules/inventory/services/inventoryService.ts
rm -f src/modules/inventory/data/mockInventory.ts
rm -f src/modules/customers/services/customerService.ts
rm -f src/modules/customers/data/mockCustomers.ts
rm -f src/modules/products/data/mockProducts.ts
rm -f src/modules/suppliers/data/mockSuppliers.ts
rm -f src/modules/audit/data/mockAuditRecords.ts
rm -f src/modules/users/data/mockUsers.ts
rm -f src/modules/users/pages/PermissoesPage.tsx
```

- [ ] **Step 3: Verify tsc**

```bash
cd nexo-main && npx tsc --noEmit
```
Expected: 0 errors

---

## Task 6: Final build + commit

- [ ] **Step 1: Full build**

```bash
cd nexo-main && npm run build
```
Expected: clean build, no errors

- [ ] **Step 2: Commit**

```bash
cd nexo-main
git add -A
git commit -m "chore: remove all mock services, dead modules, and fake data

- Remove dead modules: commissions, insights, reports/general
- Remove unrouted pages: OrcamentosPage, OrcamentoFormPage, PermissoesPage
- Remove dead inventory files: TransferenciasPage, InventoryTransferForm
- Delete mock services: posService, cashService, inventoryService, quotationService, reportService, insightService, commissionService
- Delete all mock data files: mockSales, mockPosProducts, mockCash, mockInventory, mockQuotations, mockCustomers, mockProducts, mockSuppliers, mockUsers, mockAuditRecords
- VendaDetailPage: remove item-cancel (no backend endpoint); full-sale cancel stays
- PermissoesPage: removed from router and UsuariosPage nav (no backend persistence)
- TopProducts: remove dead link to /relatorios
- userService: remove getPermissionsByRole/updatePermissions (mock only)"
```

- [ ] **Step 3: Deploy**

```bash
cd nexo-main && railway up --detach
```

---

## Risks / future work

| Item | Risk | Notes |
|------|------|-------|
| Item-level sale cancel | Feature removed from UI | Backend `POST /sales/{id}/items/{itemId}/cancel` not implemented; can restore when backend is ready |
| Permissões | Feature removed | No backend for RBAC management; re-add when `/api/permissions` endpoint exists |
| Orçamentos / Comissões | Code removed | No backend; start fresh when implementing |
| `inventoryService.applySale` | Removed | POS mock stock deduction gone; real backend handles stock on sale confirm |
| `commissionRates.ts` in commissions | Deleted | Static config file; can be recreated as backend config |
