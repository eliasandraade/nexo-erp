using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Financial;

/// <summary>
/// Lançamentos recorrentes. Títulos are generated through the end of next month: when a recurrence is
/// created or changed, and before every financial read of the tenant (contas a pagar/receber, livro) —
/// inside the tenant's own request, under a row lock, idempotent by <c>GeneratedThrough</c>. No job
/// writes across tenants.
/// </summary>
public class FinancialRecurrenceService
{
    /// <summary>ReferenceType of the títulos a recurrence generates (ReferenceId = the recurrence).</summary>
    public const string RecurrenceReference = "Recurrence";

    private readonly IFinancialRepository _financial;
    private readonly ISupplierRepository _suppliers;
    private readonly ICustomerRepository _customers;
    private readonly IStoreRepository _stores;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public FinancialRecurrenceService(
        IFinancialRepository financial, ISupplierRepository suppliers, ICustomerRepository customers,
        IStoreRepository stores, IUnitOfWork uow, ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _financial     = financial;
        _suppliers     = suppliers;
        _customers     = customers;
        _stores        = stores;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    public async Task<IReadOnlyList<RecurrenceDto>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        var list = await _financial.GetRecurrencesAsync(includeInactive, ct);
        var names = await _financial.GetSupplierNamesAsync(list.Where(r => r.SupplierId.HasValue).Select(r => r.SupplierId!.Value), ct);
        return list.Select(r => Map(r, names)).ToList();
    }

    public async Task<RecurrenceDto> CreateAsync(CreateRecurrenceRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TransactionType>(request.TransactionType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            throw new DomainException("Tipo de lançamento inválido: use Payable ou Receivable.");
        var category = FinancialCategoryRules.DefaultFor(type);
        if (request.Category is not null
            && (!Enum.TryParse(request.Category, ignoreCase: true, out category) || !Enum.IsDefined(category)))
            throw new DomainException("Categoria inválida.");
        if (!category.IsAllowedForManualEntry(type))
            throw new DomainException("Esta categoria não combina com o tipo do lançamento.");
        // The window keeps one request from creating years of títulos by mistake (or from a missing date).
        if (request.StartMonth is not { } startMonth)
            throw new DomainException("Informe o mês de início da recorrência.");
        var current = CurrentMonth();
        if (startMonth < current.AddMonths(-12) || startMonth > current.AddMonths(24))
            throw new DomainException("O início da recorrência vai de 12 meses atrás a 24 meses à frente.");
        if (type == TransactionType.Payable && request.CustomerId is not null)
            throw new DomainException("Uma conta a pagar recorrente tem fornecedor, não cliente.");
        if (type == TransactionType.Receivable && request.SupplierId is not null)
            throw new DomainException("Um recebimento recorrente tem cliente, não fornecedor.");
        if (request.SupplierId is { } supplierId)
            _ = await _suppliers.GetByIdAsync(supplierId, ct) ?? throw new NotFoundException("Supplier", supplierId);
        if (request.CustomerId is { } customerId)
            _ = await _customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException("Customer", customerId);
        _ = await DefaultAccountIdAsync(type, ct);   // generation needs it: fail now, not on every read
        if (request.StoreId is { } storeId)
            _ = await _stores.GetByIdAsync(storeId, ct) ?? throw new NotFoundException("Store", storeId);

        var recurrence = FinancialRecurrence.Create(
            _currentTenant.Id, type, category, request.Description, request.Amount, request.DayOfMonth,
            startMonth, request.EndMonth, _currentUser.UserId,
            request.SupplierId, request.CustomerId, request.StoreId);

        // One unit: the recurrence and its first títulos are saved together or not at all.
        await using var scope = await _uow.BeginTransactionAsync(ct);
        await _financial.AddRecurrenceAsync(recurrence, ct);
        await _financial.SaveChangesAsync(ct);
        await EnsureGeneratedAsync(ct);
        await scope.CommitAsync(ct);
        return Map(recurrence, await NamesAsync(recurrence, ct));
    }

    /// <summary>Changes apply to títulos not generated yet; generated ones are edited one by one.</summary>
    public async Task<RecurrenceDto> UpdateAsync(Guid id, UpdateRecurrenceRequest request, CancellationToken ct = default)
    {
        var recurrence = await _financial.GetRecurrenceByIdAsync(id, ct) ?? throw new NotFoundException("FinancialRecurrence", id);
        recurrence.Update(request.Description, request.Amount, request.DayOfMonth, request.EndMonth);

        await using var scope = await _uow.BeginTransactionAsync(ct);
        await _financial.SaveChangesAsync(ct);
        await EnsureGeneratedAsync(ct);
        await scope.CommitAsync(ct);
        return Map(recurrence, await NamesAsync(recurrence, ct));
    }

    /// <summary>Stops future títulos; the ones already generated stay (pay or cancel them one by one).</summary>
    public async Task<RecurrenceDto> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var recurrence = await _financial.GetRecurrenceByIdAsync(id, ct) ?? throw new NotFoundException("FinancialRecurrence", id);
        recurrence.Deactivate();
        await _financial.SaveChangesAsync(ct);
        return Map(recurrence, await NamesAsync(recurrence, ct));
    }

    /// <summary>
    /// Generates every pending month (through the end of next month, Brazil calendar) of the tenant's
    /// active recurrences. Cheap when there is nothing to do; safe under concurrent requests.
    /// </summary>
    public async Task EnsureGeneratedAsync(CancellationToken ct = default)
    {
        var horizon = CurrentMonth().AddMonths(1);

        // Fast path: nothing pending → no transaction, no lock (the common case on every read).
        var active = await _financial.GetRecurrencesAsync(includeInactive: false, ct);
        if (!active.Any(r => r.PendingMonths(horizon).Count > 0)) return;

        await using var scope = await _uow.BeginTransactionAsync(ct);
        var due = await _financial.LockRecurrencesDueAsync(horizon, _currentTenant.Id, ct);
        var generated = false;
        foreach (var recurrence in due)
        {
            var months = recurrence.PendingMonths(horizon);
            if (months.Count == 0) continue;
            var accountId = await DefaultAccountIdAsync(recurrence.TransactionType, ct);
            foreach (var month in months)
            {
                var due1 = BusinessClock.NoonUtc(recurrence.DueDayIn(month));
                await _financial.AddTransactionAsync(FinancialTransaction.Create(
                    _currentTenant.Id, accountId, recurrence.TransactionType, recurrence.Category, recurrence.Amount,
                    $"{recurrence.Description} — {month:MM/yyyy}", due1, recurrence.CreatedByUserId,
                    referenceType: RecurrenceReference, referenceId: recurrence.Id,
                    storeId: recurrence.StoreId, competenceDate: due1,
                    customerId: recurrence.CustomerId, supplierId: recurrence.SupplierId), ct);
            }
            recurrence.MarkGenerated(months[^1]);
            generated = true;
        }
        if (generated) await _financial.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
    }

    /// <summary>First day of the current month on the Brazil calendar.</summary>
    private static DateOnly CurrentMonth()
    {
        var today = DateOnly.FromDateTime(BusinessClock.TodayStartUtc().AddHours(12));
        return new DateOnly(today.Year, today.Month, 1);
    }

    private async Task<Guid> DefaultAccountIdAsync(TransactionType type, CancellationToken ct)
        => (await _financial.GetDefaultByTypeAsync(
                type == TransactionType.Receivable ? FinancialAccountType.Receivable : FinancialAccountType.Payable, ct)
            ?? throw new DomainException("Nenhuma conta padrão ativa para a recorrência (Contas a Receber / a Pagar).")).Id;

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(FinancialRecurrence r, CancellationToken ct)
        => r.SupplierId is { } id ? await _financial.GetSupplierNamesAsync([id], ct) : new Dictionary<Guid, string>();

    private static RecurrenceDto Map(FinancialRecurrence r, IReadOnlyDictionary<Guid, string> supplierNames) => new(
        r.Id, r.TransactionType.ToString(), r.Category.ToString(), r.Description, r.Amount, r.DayOfMonth,
        r.StartMonth, r.EndMonth, r.GeneratedThrough, r.IsActive,
        r.SupplierId, r.SupplierId is { } s && supplierNames.TryGetValue(s, out var n) ? n : null,
        r.CustomerId, r.StoreId);
}
