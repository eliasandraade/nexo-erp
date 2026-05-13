using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

/// <summary>
/// App settings repository.
/// Scoped to the current tenant via EF Core Global Query Filters.
/// </summary>
public class AppSettingsRepository : IAppSettingsRepository
{
    private readonly NexoDbContext _context;
    private readonly ICurrentTenant _currentTenant;

    public AppSettingsRepository(NexoDbContext context, ICurrentTenant currentTenant)
    {
        _context = context;
        _currentTenant = currentTenant;
    }

    public async Task<AppSettings> GetOrCreateAsync(CancellationToken ct = default)
    {
        // Global Query Filter already scopes to current tenant
        var settings = await _context.AppSettings.FirstOrDefaultAsync(ct);

        if (settings is not null) return settings;

        // Auto-create defaults on first access for this tenant
        settings = AppSettings.CreateForTenant(
            tenantId:      _currentTenant.Id,
            companyJson:   """{"name":"Minha Empresa","tradeName":"","cnpj":"","email":"","phone":""}""",
            operationJson: """{"defaultStore":"","defaultOperator":""}""",
            inventoryJson: """{"noMovementAlertDays":30,"minStockBehavior":"alert","enableLowStockAlerts":true,"enableZeroStockAlerts":true,"enableHighRotationAlerts":false}""",
            commissionJson:"""{"defaultCommissionRate":3,"enableProductCommission":false,"policyNotes":""}""",
            posJson:       """{"allowValueDiscount":true,"allowPercentDiscount":true,"requireManagerAuth":true,"maxDiscountPercent":20}""",
            systemJson:    """{"language":"pt-BR","dateFormat":"dd/MM/yyyy","currencySymbol":"R$"}""");

        _context.AppSettings.Add(settings);
        await _context.SaveChangesAsync(ct);

        return settings;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
