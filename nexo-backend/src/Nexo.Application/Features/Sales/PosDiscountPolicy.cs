using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Settings;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Sales;

/// <summary>A manager's login and password typed at the PDV to let a discount above the limit through.</summary>
public record ManagerApprovalRequest(string Login, string Password);

/// <summary>
/// The PDV discount rule (Configurações → PDV), enforced when a sale is closed at the PDV:
/// the whole discount (items + sale) may not exceed <c>MaxDiscountPercent</c> of the gross value —
/// unless the person closing is a manager, or (when <c>RequireManagerAuth</c> is on) a manager of this
/// company types login and password at the PDV. Overrides are audited with the manager's name.
/// Other flows that confirm sales (restaurant orders) have their own rules and are not affected.
/// </summary>
public class PosDiscountPolicy
{
    private static readonly string[] ManagerRoles = ["Gerente", "Diretoria"];

    private readonly ISaleRepository _sales;
    private readonly SettingsService _settings;
    private readonly ICurrentUser _user;
    private readonly AuthService _auth;
    private readonly IAuditWriter _audit;
    private readonly SaleService _saleService;

    public PosDiscountPolicy(ISaleRepository sales, SettingsService settings, ICurrentUser user, AuthService auth,
        IAuditWriter audit, SaleService saleService)
    {
        _sales       = sales;
        _settings    = settings;
        _user        = user;
        _auth        = auth;
        _audit       = audit;
        _saleService = saleService;
    }

    /// <summary>Checks the discount rule, then confirms the sale as usual.</summary>
    public async Task<SaleDto> ConfirmAtPosAsync(Guid saleId, ConfirmSaleRequest request, CancellationToken ct = default)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId, ct) ?? throw new NotFoundException("Sale", saleId);
        if (sale.IsInDraft)
        {
            var gross = sale.Items.Sum(i => i.Quantity * i.UnitPrice);
            var discount = sale.Items.Sum(i => i.DiscountAmount) + request.DiscountAmount;
            var rules = await _settings.GetPosAsync(ct);
            await EnforceAsync(sale.Number, gross, discount, rules, request.ManagerApproval, ct);
        }
        return await _saleService.ConfirmAsync(saleId, request, ct);
    }

    private async Task EnforceAsync(int saleNumber, decimal gross, decimal discount, PosSettingsDto rules,
        ManagerApprovalRequest? approval, CancellationToken ct)
    {
        if (gross <= 0 || discount <= 0) return;
        var percent = Math.Round(discount / gross * 100m, 2);
        if (percent <= rules.MaxDiscountPercent) return;
        if (ManagerRoles.Contains(_user.Role, StringComparer.OrdinalIgnoreCase)) return;

        if (!rules.RequireManagerAuth)
            throw new DomainException($"Desconto de {percent:0.##}% acima do limite de {rules.MaxDiscountPercent:0.##}%. Só um gerente pode dar esse desconto.");

        if (approval is null || string.IsNullOrWhiteSpace(approval.Login) || string.IsNullOrWhiteSpace(approval.Password))
            throw new DomainException($"Desconto de {percent:0.##}% acima do limite de {rules.MaxDiscountPercent:0.##}%. Peça a autorização de um gerente.");

        var manager = await _auth.VerifyManagerAsync(new VerifyManagerRequest(approval.Login, approval.Password), ct);
        if (!manager.Authorized)
            throw new DomainException("Autorização de gerente recusada. Confira login e senha.");

        _audit.Stage(
            actionType:  "pos_discount_override",
            severity:    AuditSeverity.Warning,
            entityType:  "Sale",
            entityId:    saleNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            description: $"Desconto de {percent:0.##}% (limite {rules.MaxDiscountPercent:0.##}%) autorizado por {manager.ManagerName}.",
            tenantId:    _user.TenantId,
            actorId:     _user.UserId,
            actorName:   _user.Name);
    }
}
