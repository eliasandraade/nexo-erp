using System.Globalization;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Settings;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Microsoft.Extensions.Logging;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Sales;

/// <summary>A manager's login and password typed at the PDV to let a discount above the limit through.</summary>
public record ManagerApprovalRequest(string Login, string Password);

/// <summary>
/// The PDV discount rule (Configurações → PDV), enforced when a sale is closed at the PDV:
/// the whole discount (items + sale) may not exceed <c>MaxDiscountPercent</c> of the gross value —
/// unless the person closing is a manager, or (when <c>RequireManagerAuth</c> is on) a manager of this
/// company types login and password at the PDV. Checked on the locked sale, inside the confirmation,
/// so an item added at the same moment cannot slip through. Approvals are audited (refusals are logged —
/// the refused confirmation rolls back); wrong passwords count on their own lock (not the manager's
/// login lock): 10 per 15 minutes.
/// Restaurant order closing has its own rules and is not affected.
/// </summary>
public class PosDiscountPolicy
{
    /// <summary>Error code the PDV reads to ask for a manager's authorization.</summary>
    public const string NeedsManagerCode = "discount_requires_manager";

    private static readonly string[] ManagerRoles = ["Gerente", "Diretoria"];

    private readonly SettingsService _settings;
    private readonly ICurrentUser _user;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly LoginAttemptGuard _attempts;
    private readonly IAuditWriter _audit;
    private readonly SaleService _saleService;
    private readonly ILogger<PosDiscountPolicy> _logger;

    public PosDiscountPolicy(SettingsService settings, ICurrentUser user, IUserRepository users, IPasswordHasher hasher,
        LoginAttemptGuard attempts, IAuditWriter audit, SaleService saleService, ILogger<PosDiscountPolicy> logger)
    {
        _logger      = logger;
        _settings    = settings;
        _user        = user;
        _users       = users;
        _hasher      = hasher;
        _attempts    = attempts;
        _audit       = audit;
        _saleService = saleService;
    }

    /// <summary>Confirms the sale; the discount rule runs on the locked sale before anything is written.</summary>
    public async Task<SaleDto> ConfirmAtPosAsync(Guid saleId, ConfirmSaleRequest request, CancellationToken ct = default)
    {
        var rules = await _settings.GetPosAsync(ct);
        return await _saleService.ConfirmAsync(saleId, request, (sale, token) => EnforceAsync(sale, request, rules, token), ct);
    }

    private async Task EnforceAsync(Sale sale, ConfirmSaleRequest request, PosSettingsDto rules, CancellationToken ct)
    {
        var gross = sale.Items.Sum(i => i.Quantity * i.UnitPrice);
        var discount = sale.Items.Sum(i => i.DiscountAmount) + request.DiscountAmount;
        if (gross <= 0 || discount <= 0) return;
        var percent = discount / gross * 100m;
        if (percent <= rules.MaxDiscountPercent) return;
        if (ManagerRoles.Contains(_user.Role, StringComparer.OrdinalIgnoreCase)) return;

        var shown = percent.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
        var limit = rules.MaxDiscountPercent.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
        if (!rules.RequireManagerAuth)
            throw new DomainException($"Desconto de {shown}% acima do limite de {limit}%. Só um gerente pode dar esse desconto.");

        var approval = request.ManagerApproval;
        if (approval is null || string.IsNullOrWhiteSpace(approval.Login) || string.IsNullOrWhiteSpace(approval.Password))
            throw new DomainException($"Desconto de {shown}% acima do limite de {limit}%. Peça a autorização de um gerente.", NeedsManagerCode);

        var manager = await VerifyManagerAsync(approval, sale, shown, ct);
        _audit.Stage(
            actionType:  "pos_discount_override",
            severity:    AuditSeverity.Warning,
            entityType:  "Sale",
            entityId:    sale.Id.ToString(),
            description: $"Venda #{sale.Number}: desconto de {shown}% (limite {limit}%) autorizado por {manager.FullName}.",
            tenantId:    _user.TenantId,
            actorId:     _user.UserId,
            actorName:   _user.Name,
            metadata:    new { saleNumber = sale.Number, managerUserId = manager.Id, percent = Math.Round(percent, 2), limit = rules.MaxDiscountPercent });
    }

    private async Task<User> VerifyManagerAsync(ManagerApprovalRequest approval, Sale sale, string shown, CancellationToken ct)
    {
        var manager = await _users.GetByLoginInTenantAsync(approval.Login.Trim().ToLowerInvariant(), _user.TenantId, ct);
        var lockKey = manager is null ? null : LoginAttemptGuard.ForPosApproval(manager.Id);
        if (lockKey is not null && await _attempts.IsLockedAsync(lockKey, ct))
            throw new DomainException("Muitas tentativas de autorização. Aguarde alguns minutos.", NeedsManagerCode);

        var ok = manager is not null
                 && manager.Status == UserStatus.Active
                 && manager.TenantId == _user.TenantId
                 && _hasher.Verify(approval.Password, manager.PasswordHash)
                 && manager.IsManager();
        if (ok) return manager!;

        if (lockKey is not null) await _attempts.RegisterFailureAsync(lockKey, ct);
        _logger.LogWarning("PDV discount authorization refused: sale {SaleId}, {Percent}% by user {UserId}.", sale.Id, shown, _user.UserId);
        throw new DomainException("Autorização de gerente recusada. Confira login e senha.", NeedsManagerCode);
    }
}
