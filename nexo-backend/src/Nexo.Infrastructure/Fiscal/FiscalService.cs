using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Features.Fiscal;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Fiscal;

/// <summary>
/// Fiscal foundation: who issues (per-store profile), what is sold (product fiscal data), who buys
/// (customer IE) and how far the store is from issuing. No nota is issued and nothing leaves the
/// system — emission goes through <see cref="IFiscalDocumentProvider"/>, which in the 1.0 is none.
/// </summary>
public class FiscalService : Nexo.Application.Integrations.Pdf.IReceiptHeaderProvider
{
    private const int IssueListLimit = 50;

    private readonly NexoDbContext _db;
    private readonly IFiscalDocumentProvider _provider;
    private readonly IIntegrationFeatureFlags _flags;

    public FiscalService(NexoDbContext db, IFiscalDocumentProvider provider, IIntegrationFeatureFlags flags)
    {
        _db       = db;
        _provider = provider;
        _flags    = flags;
    }

    // ── Issuer profile ──────────────────────────────────────────────────────────

    public async Task<FiscalProfileDto?> GetProfileAsync(CancellationToken ct)
    {
        var p = await _db.FiscalProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        return p is null ? null : Map(p);
    }

    public async Task<FiscalProfileDto> SaveProfileAsync(Guid tenantId, SaveFiscalProfileRequest r, CancellationToken ct)
    {
        try
        {
            return await UpsertProfileAsync(tenantId, r, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first saves at once: the other one created the row — apply this one on top of it.
            _db.ChangeTracker.Clear();
            return await UpsertProfileAsync(tenantId, r, ct);
        }
    }

    private async Task<FiscalProfileDto> UpsertProfileAsync(Guid tenantId, SaveFiscalProfileRequest r, CancellationToken ct)
    {
        var profile = await _db.FiscalProfiles.FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            profile = FiscalProfile.Create(tenantId);
            _db.FiscalProfiles.Add(profile);
        }
        profile.Update(
            r.LegalName, r.TradeName, FiscalCodes.Document(r.Cnpj),
            FiscalRules.NormalizeStateRegistration(r.StateRegistration)!,
            r.MunicipalRegistration, Enum.Parse<TaxRegime>(r.TaxRegime.Trim(), true),
            r.Street, r.Number, r.Complement, r.District, r.CityName, r.CityIbgeCode.Trim(),
            r.State.Trim().ToUpperInvariant(), FiscalCodes.Digits(r.ZipCode));
        profile.UpdateNumbering(r.OperationNature, r.NfceSeries, r.NfceNextNumber, r.NfeSeries, r.NfeNextNumber);
        await _db.SaveChangesAsync(ct);
        return Map(profile);
    }

    /// <summary>
    /// Who appears at the top of receipts: the store's issuer (trade name, CNPJ, address) when the
    /// fiscal profile is filled in, the company name otherwise — never a placeholder.
    /// </summary>
    public async Task<Nexo.Application.Integrations.Pdf.ReceiptHeader> GetAsync(CancellationToken ct)
    {
        var p = await _db.FiscalProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (p is not null)
            return new(p.TradeName ?? p.LegalName, p.Cnpj,
                $"{p.Street}, {p.Number}{(p.Complement is null ? "" : " " + p.Complement)} — {p.District}, {p.CityName}/{p.State}");
        var name = await _db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.Id == _db.CurrentTenantIdForFilter).Select(t => t.TradeName ?? t.CompanyName).FirstOrDefaultAsync(ct);
        return new(string.IsNullOrWhiteSpace(name) ? "Recibo" : name, null, null);
    }

    // ── Product ────────────────────────────────────────────────────────────────

    public async Task<ProductFiscalDto> GetProductAsync(Guid productId, CancellationToken ct)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId, ct)
                ?? throw new NotFoundException("Produto não encontrado.");
        return Map(p);
    }

    public async Task<ProductFiscalDto> UpdateProductAsync(Guid productId, UpdateProductFiscalRequest r, CancellationToken ct)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId, ct)
                ?? throw new NotFoundException("Produto não encontrado.");
        p.UpdateFiscal(Digits(r.Ncm), Digits(r.Cest), r.Origin, Digits(r.Cfop), Digits(r.TaxSituation), r.TaxUnit);
        await _db.SaveChangesAsync(ct);
        return Map(p);
    }

    // ── Customer ───────────────────────────────────────────────────────────────

    public async Task<CustomerFiscalDto> GetCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, ct)
                ?? throw new NotFoundException("Cliente não encontrado.");
        return Map(c);
    }

    public async Task<CustomerFiscalDto> UpdateCustomerAsync(Guid customerId, UpdateCustomerFiscalRequest r, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct)
                ?? throw new NotFoundException("Cliente não encontrado.");
        TaxpayerIndicator? indicator = string.IsNullOrWhiteSpace(r.TaxpayerIndicator)
            ? null
            : Enum.Parse<TaxpayerIndicator>(r.TaxpayerIndicator.Trim(), true);
        c.UpdateFiscal(FiscalRules.NormalizeStateRegistration(r.StateRegistration), indicator, r.CityIbgeCode);
        await _db.SaveChangesAsync(ct);
        return Map(c);
    }

    // ── Readiness ──────────────────────────────────────────────────────────────

    public async Task<FiscalReadinessDto> GetReadinessAsync(CancellationToken ct)
    {
        var hasProfile = await _db.FiscalProfiles.AnyAsync(ct);
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && !p.IsIngredient)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Code, p.Name, p.Barcode, p.Ncm })
            .ToListAsync(ct);

        var issues = products
            .Select(p => new FiscalProductIssueDto(p.Id, p.Code, p.Name, p.Barcode, p.Ncm, Problems(p.Ncm, p.Barcode)))
            .Where(i => i.Problems.Count > 0)
            .ToList();

        return new FiscalReadinessDto(
            ProfileComplete: hasProfile,
            ProfileMissing: hasProfile ? [] : ["Preencha os dados do emissor: razão social, CNPJ, inscrição estadual, regime e endereço."],
            ActiveProducts: products.Count,
            ProductsWithoutNcm: products.Count(p => string.IsNullOrEmpty(p.Ncm)),
            ProductsWithInvalidBarcode: products.Count(p => !string.IsNullOrWhiteSpace(p.Barcode) && !FiscalCodes.IsGtin(p.Barcode)),
            ProductsToFix: issues.Take(IssueListLimit).ToList(),
            EmissionAvailable: _flags.FiscalEnabled && _provider.IsConfigured,
            Provider: _provider.Name);
    }

    private static List<string> Problems(string? ncm, string? barcode)
    {
        var problems = new List<string>();
        if (string.IsNullOrEmpty(ncm)) problems.Add("Sem NCM");
        if (!string.IsNullOrWhiteSpace(barcode) && !FiscalCodes.IsGtin(barcode))
            problems.Add("Código de barras não é um GTIN válido (irá como SEM GTIN)");
        return problems;
    }

    private static string? Digits(string? v) => string.IsNullOrWhiteSpace(v) ? null : FiscalCodes.Digits(v);

    private static FiscalProfileDto Map(FiscalProfile p) => new(
        p.LegalName, p.TradeName, p.Cnpj, p.StateRegistration, p.MunicipalRegistration, p.TaxRegime.ToString(), p.Crt,
        p.Street, p.Number, p.Complement, p.District, p.CityName, p.CityIbgeCode, p.State, p.ZipCode, p.UpdatedAt,
        p.OperationNature, p.NfceSeries, p.NfceNextNumber, p.NfeSeries, p.NfeNextNumber);

    private static ProductFiscalDto Map(Product p) =>
        new(p.Id, p.Code, p.Name, p.Barcode, p.Ncm, p.Cest, p.FiscalOrigin, p.Cfop, p.TaxSituation, p.TaxUnit, p.Unit.ToString());

    private static CustomerFiscalDto Map(Customer c) => new(
        c.Id, c.Name, c.DocumentType.ToString(), c.DocumentNumber,
        c.DocumentType switch
        {
            DocumentType.Cpf => FiscalCodes.IsCpf(c.DocumentNumber),
            DocumentType.Cnpj => FiscalCodes.IsCnpj(c.DocumentNumber),
            _ => false,
        },
        c.StateRegistration, c.TaxpayerIndicator?.ToString(), c.CityIbgeCode);
}
