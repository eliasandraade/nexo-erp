using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for SvcPayment — manual payment records against an order or a customer package.
/// Resolves the target (cross-tenant invisible → 404), rejects a cancelled target (422), and
/// rejects an amount that exceeds the remaining balance (422). The CustomerId is taken from the
/// target. This service NEVER mutates the order (total/status) or the package (balance/status) —
/// it only reads them to compute the remaining balance.
///
/// It DOES post to the tenant's financeiro: a confirmed payment becomes a settled Receivable and
/// a void becomes a counter-entry, both via ServiceFinancialPostingService. Without that, Service
/// revenue never reached the financial module and the ERP effectively kept two separate tills.
///
/// A payment that settles an order in full also recognises that order's commissions (regime de
/// caixa) through SvcCommissionService — in the same transaction as the payment and its lançamento.
/// A void that leaves the order no longer fully paid reverses them, in the void's transaction.
/// </summary>
public class SvcPaymentService
{
    private readonly ISvcPaymentRepository         _payments;
    private readonly ISvcOrderRepository           _orders;
    private readonly ISvcCustomerPackageRepository _customerPackages;
    private readonly ICurrentTenant                _currentTenant;
    private readonly ServiceFinancialPostingService _posting;
    private readonly SvcCommissionService          _commissions;
    private readonly IUnitOfWork                   _uow;

    public SvcPaymentService(
        ISvcPaymentRepository payments, ISvcOrderRepository orders,
        ISvcCustomerPackageRepository customerPackages, ICurrentTenant currentTenant,
        ServiceFinancialPostingService posting, SvcCommissionService commissions, IUnitOfWork uow)
    {
        _payments = payments; _orders = orders; _customerPackages = customerPackages;
        _currentTenant = currentTenant; _posting = posting; _commissions = commissions; _uow = uow;
    }

    public async Task<IReadOnlyList<SvcPaymentDto>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default)
        => (await _payments.GetAllAsync(customerId, orderId, customerPackageId, method, status, from, to, ct))
            .Select(MapToDto).ToList();

    public async Task<SvcPaymentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _payments.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPayment", id));

    public async Task<SvcPaymentDto> CreateAsync(CreateSvcPaymentRequest r, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        SvcPayment payment;
        string description;
        if (r.OrderId is { } orderId)
        {
            // Serialises concurrent payments/edits of the same order: the remaining-balance check
            // and the "fully paid -> commission" check below must see each other's payments.
            await _orders.LockAsync(orderId, _currentTenant.Id, ct);
            var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
            if (order.Status == SvcOrderStatus.Cancelled) throw new DomainException("Cannot pay a cancelled order.");
            EnsureWithinRemaining(r.Amount, order.TotalAmount, await PaidTotalForOrderAsync(orderId, ct));
            payment = SvcPayment.CreateForOrder(_currentTenant.Id, order.CustomerId, orderId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
            description = $"Orken Service — pagamento {order.Code}";
        }
        else
        {
            var cpId = r.CustomerPackageId!.Value;
            var cp = await _customerPackages.GetByIdAsync(cpId, ct) ?? throw new NotFoundException("SvcCustomerPackage", cpId);
            if (cp.Status == SvcCustomerPackageStatus.Cancelled) throw new DomainException("Cannot pay a cancelled customer package.");
            EnsureWithinRemaining(r.Amount, cp.PriceSnapshot, await PaidTotalForCustomerPackageAsync(cpId, ct));
            payment = SvcPayment.CreateForCustomerPackage(_currentTenant.Id, cp.CustomerId, cpId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
            description = "Orken Service — pagamento de pacote";
        }

        await _payments.AddAsync(payment, ct);
        // Same scoped DbContext behind both repositories, so the payment and its financial
        // transaction commit together — a payment can never be recorded without its lançamento.
        await _posting.PostPaymentAsync(payment, description, ct);
        await _payments.SaveChangesAsync(ct);

        // Saved first so the settled check below already counts this payment. Recognised at the
        // server clock (when the money was registered), not the client-supplied PaidAt, so a
        // backdated payment cannot drop entries into a period that was already closed.
        if (payment.OrderId is { } paidOrderId)
            await _commissions.RecognizeOrderIfSettledAsync(paidOrderId, DateTime.UtcNow, ct);

        await tx.CommitAsync(ct);
        return MapToDto(payment);
    }

    public async Task<SvcPaymentDto> VoidAsync(Guid id, VoidSvcPaymentRequest r, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        // Lock first, read after: a concurrent void then sees this one committed (already Voided → 422).
        await _payments.LockAsync(id, _currentTenant.Id, ct);
        var payment = await _payments.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPayment", id);
        if (payment.OrderId is { } orderId) await _orders.LockAsync(orderId, _currentTenant.Id, ct);
        payment.Void(r.Reason);
        _payments.Update(payment);
        // Counter-entry, not a delete: the original receivable stays auditable.
        await _posting.ReversePaymentAsync(payment, "Orken Service — estorno de pagamento", ct);
        await _payments.SaveChangesAsync(ct);
        if (payment.OrderId is { } voidedOrderId)
            await _commissions.ReverseOrderIfUnsettledAsync(voidedOrderId, DateTime.UtcNow, ct);
        await tx.CommitAsync(ct);
        return MapToDto(payment);
    }

    public async Task<SvcPaymentSummaryDto> GetOrderSummaryAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        return Summarize(orderId, "Order", order.TotalAmount, await _payments.GetByOrderAsync(orderId, ct));
    }

    public async Task<SvcPaymentSummaryDto> GetCustomerPackageSummaryAsync(Guid customerPackageId, CancellationToken ct = default)
    {
        var cp = await _customerPackages.GetByIdAsync(customerPackageId, ct)
            ?? throw new NotFoundException("SvcCustomerPackage", customerPackageId);
        return Summarize(customerPackageId, "CustomerPackage", cp.PriceSnapshot,
            await _payments.GetByCustomerPackageAsync(customerPackageId, ct));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<decimal> PaidTotalForOrderAsync(Guid orderId, CancellationToken ct)
        => (await _payments.GetByOrderAsync(orderId, ct)).Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);

    private async Task<decimal> PaidTotalForCustomerPackageAsync(Guid cpId, CancellationToken ct)
        => (await _payments.GetByCustomerPackageAsync(cpId, ct)).Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);

    private static void EnsureWithinRemaining(decimal amount, decimal total, decimal paid)
    {
        if (amount > total - paid) throw new DomainException("Amount exceeds the remaining balance.");
    }

    private static SvcPaymentSummaryDto Summarize(Guid targetId, string targetType, decimal total, IEnumerable<SvcPayment> payments)
    {
        var paid   = payments.Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);
        var voided = payments.Where(p => p.Status == SvcPaymentStatus.Voided).Sum(p => p.Amount);
        var remaining = total - paid;
        return new(targetId, targetType, total, paid, voided, remaining, remaining <= 0m);
    }

    private static SvcPaymentDto MapToDto(SvcPayment p) => new(
        Id: p.Id, StoreId: p.StoreId, CustomerId: p.CustomerId, OrderId: p.OrderId,
        CustomerPackageId: p.CustomerPackageId, Amount: p.Amount, Method: p.Method, Status: p.Status,
        PaidAt: p.PaidAt, ExternalReference: p.ExternalReference, Notes: p.Notes, VoidReason: p.VoidReason,
        VoidedAt: p.VoidedAt, CreatedAt: p.CreatedAt, UpdatedAt: p.UpdatedAt);
}
