using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for SvcPayment — manual payment records against an order or a customer package.
/// Resolves the target (cross-tenant invisible → 404), rejects a cancelled target (422), and
/// rejects an amount that exceeds the remaining balance (422). The CustomerId is taken from the
/// target. This service NEVER mutates the order (total/status) or the package (balance/status),
/// and never creates any global financial/cash entity — it only reads to compute remaining.
/// </summary>
public class SvcPaymentService
{
    private readonly ISvcPaymentRepository         _payments;
    private readonly ISvcOrderRepository           _orders;
    private readonly ISvcCustomerPackageRepository _customerPackages;
    private readonly ICurrentTenant                _currentTenant;

    public SvcPaymentService(
        ISvcPaymentRepository payments, ISvcOrderRepository orders,
        ISvcCustomerPackageRepository customerPackages, ICurrentTenant currentTenant)
    {
        _payments = payments; _orders = orders; _customerPackages = customerPackages; _currentTenant = currentTenant;
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
        SvcPayment payment;
        if (r.OrderId is { } orderId)
        {
            var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
            if (order.Status == SvcOrderStatus.Cancelled) throw new DomainException("Cannot pay a cancelled order.");
            EnsureWithinRemaining(r.Amount, order.TotalAmount, await PaidTotalForOrderAsync(orderId, ct));
            payment = SvcPayment.CreateForOrder(_currentTenant.Id, order.CustomerId, orderId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
        }
        else
        {
            var cpId = r.CustomerPackageId!.Value;
            var cp = await _customerPackages.GetByIdAsync(cpId, ct) ?? throw new NotFoundException("SvcCustomerPackage", cpId);
            if (cp.Status == SvcCustomerPackageStatus.Cancelled) throw new DomainException("Cannot pay a cancelled customer package.");
            EnsureWithinRemaining(r.Amount, cp.PriceSnapshot, await PaidTotalForCustomerPackageAsync(cpId, ct));
            payment = SvcPayment.CreateForCustomerPackage(_currentTenant.Id, cp.CustomerId, cpId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
        }

        await _payments.AddAsync(payment, ct);
        await _payments.SaveChangesAsync(ct);
        return MapToDto(payment);
    }

    public async Task<SvcPaymentDto> VoidAsync(Guid id, VoidSvcPaymentRequest r, CancellationToken ct = default)
    {
        var payment = await _payments.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPayment", id);
        payment.Void(r.Reason);
        _payments.Update(payment);
        await _payments.SaveChangesAsync(ct);
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
