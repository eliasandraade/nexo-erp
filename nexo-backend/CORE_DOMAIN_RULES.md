# NexoERP — CORE Domain Rules

> Authoritative reference for the business rules enforced by Domain and Application layers.
> Read this before modifying Sale, Stock, Cash, or Financial code.

---

## 1. Sale

### 1.1 State Machine

```
Draft ──► Confirmed ──► Paid
   │            │
   └────────────┴──► Cancelled
```

| Transition | Trigger | Pre-condition | Side-effects |
|---|---|---|---|
| `Draft → Confirmed` | `SaleService.ConfirmAsync` | Sale has ≥1 item; sum(payments) == total | Stock deducted; SalePayments created; CashMovements / FinancialTransactions generated |
| `Confirmed → Paid` | Auto during `ConfirmAsync` | No `Credit` payment line | Sets `PaidAt` |
| `Confirmed → Cancelled` | `SaleService.CancelAsync` | Status is Confirmed | Stock reversed (ReturnEntry); CashMovements reversed (Withdrawal); open FinancialTransactions cancelled |
| `Draft → Cancelled` | `SaleService.CancelAsync` | Status is Draft | No side-effects |
| Any → Paid | Not directly cancellable | — | Once `Paid`, a sale **cannot** be cancelled. Refunds must be separate transactions. |

**Removed states:** `Open`, `PartiallyPaid` — do not reintroduce.

### 1.2 Totals Calculation

- `Subtotal = Σ(item.UnitPrice × item.Quantity − item.DiscountAmount)`
- `Total = Subtotal − DiscountAmount + TaxAmount`
- Recalculated automatically on `AddItem`, `RemoveItem`, and at `ConfirmAsync` (where final discount/tax are applied).
- `RecalculateTotals` throws `DomainException` if the sale is not in Draft.

### 1.3 Sale Items

- Items may only be added/removed while status = `Draft`.
- `CostPrice` on `SaleItem` is a **snapshot** captured from `product.CostPrice` at the moment of adding the item. It never changes even if the product price is later updated. This preserves margin history.

---

## 2. Payments

### 2.1 PaymentMethod vs PaymentType

| Concept | Purpose | Values |
|---|---|---|
| `PaymentMethod` | **How** the customer pays | Cash, Debit, Credit, Pix, Transfer, Check, Mixed, Other |
| `PaymentType` | **When** the money enters | `Cash` (à vista, now) · `Credit` (a prazo, future) |

These are independent. A `Pix` payment can be `Cash` (received immediately) or `Credit` (scheduled for a future date).

### 2.2 Confirmation Rules

- At least one `PaymentInput` is required.
- `Σ(payment.Amount)` must equal `sale.Total` exactly.
- Each `payment.Amount` must be > 0.
- `PaymentType.Credit` requires `DueDate`. `PaymentType.Cash` must not have a `DueDate`.
- Multiple payment lines are allowed (mixed payment). Each line is recorded as a `SalePayment` record.

### 2.3 SalePayment Immutability

`SalePayment` records are write-once. The `TenantSaveChangesInterceptor` throws `InvalidOperationException` if any `SalePayment` enters `Modified` or `Deleted` state. Corrections require cancelling the sale.

---

## 3. Stock

### 3.1 Deduction Rule

Stock is deducted only for products where `Product.TrackStock == true`. Non-tracked products (services, digital goods) pass through without stock checks.

### 3.2 Concurrency Control

`StockItem` uses PostgreSQL's `xmin` system column as an optimistic concurrency token (`IsRowVersion()`). If two sale confirmations attempt to update the same `StockItem` concurrently, EF raises `DbUpdateConcurrencyException`. `SaleService.ConfirmAsync` catches this and re-throws as `DomainException("Stock quantity changed concurrently. Please retry.")`. **The caller is responsible for retrying.**

### 3.3 Movement Types

| Type | When | Stock delta |
|---|---|---|
| `ManualEntry` | Manual adjustment (receiving goods) | + |
| `ManualExit` | Manual adjustment (loss, correction) | − |
| `Adjustment` | Inventory count correction | ± |
| `Loss` | Damage / expiry | − |
| `SaleOutput` | Sale confirmed | − |
| `ReturnEntry` | Sale cancelled (was Confirmed) | + |

### 3.4 StockMovement Immutability

`StockMovement` records are write-once (same interceptor rule as `SalePayment`). Stock corrections must be made with a compensating movement record, never by editing an existing one. This preserves audit history.

---

## 4. Cash Session

### 4.1 One Session Per User

Each user may have at most **one open** `CashSession` at a time (scoped per tenant). Multiple users can have simultaneous open sessions.

`CashService.OpenAsync` calls `GetOpenSessionByUserAsync(_currentUser.UserId)` and throws `ConflictException` if a session is already open for that user.

Index: `ix_cash_sessions_tenant_user_status (tenant_id, opened_by_user_id, status)`.

### 4.2 CashMovement Generation

For every `PaymentType.Cash` payment line on a confirmed sale, one `CashMovement(SaleReceipt)` is created in the linked cash session.

For `CancelAsync` of a confirmed sale, one compensating `CashMovement(Withdrawal)` is created per reversed cash payment line.

If `sale.CashSessionId` is null at confirmation time, the service auto-links the selling user's open session (if any). No error is raised if no session is open — the sale can be confirmed without a cash session (e.g. card-only sales tracked externally).

### 4.3 CashMovement Immutability

Same as `StockMovement` — write-once. Corrections use compensating entries.

---

## 5. Financial Transactions

### 5.1 Account Resolution

Financial accounts are never specified in API requests. They are resolved automatically:

| Payment | Account Type | Resolution method |
|---|---|---|
| `PaymentType.Credit` (a prazo) | `Receivable` | `GetDefaultByTypeAsync(FinancialAccountType.Receivable)` |

The default account is the first active account of the given type ordered by `Code`. Default accounts are seeded by `DataSeeder.SeedDefaultFinancialAccountsAsync`:

| Code | Name | Type |
|---|---|---|
| 1.1 | Caixa | Cash |
| 1.2 | Banco | Bank |
| 2.1 | Contas a Receber | Receivable |
| 3.1 | Contas a Pagar | Payable |

If no active Receivable account exists, `ConfirmAsync` throws `DomainException`. This is a configuration error that must be fixed in financial settings.

### 5.2 Cancellation

When a confirmed sale is cancelled, all `FinancialTransaction` records with `Status == Pending || Overdue` that reference the sale are cancelled via `finTx.Cancel()`. Already-`Paid` transactions are left untouched (they represent money that was received and must be handled as a separate refund workflow).

---

## 6. Atomicity

`ConfirmAsync` and `CancelAsync` are fully atomic: all side-effects (stock, cash, financial) execute within a single `IUnitOfWork` database transaction.

- On any unhandled exception, the transaction is rolled back automatically via `await using var tx`.
- `DbUpdateConcurrencyException` (stock race) is caught inside the `try`, re-thrown as `DomainException`, which causes the outer `catch` to roll back.
- The database-level FK cascade and constraint checking are the final guard against partial writes.

---

## 7. Multi-Tenancy

All entities extend `TenantEntity` and carry a `TenantId`. EF Global Query Filters enforce tenant isolation on all reads. `TenantSaveChangesInterceptor` stamps `TenantId` automatically on every `SaveChanges`.

No repository method accepts a `tenantId` parameter — tenant scoping is transparent at the infrastructure layer.
