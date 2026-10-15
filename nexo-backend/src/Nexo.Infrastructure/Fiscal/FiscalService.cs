using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Domain.Exceptions;
using Nexo.Application.Features.Fiscal;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Fiscal;

/// <summary>
/// Fiscal foundation: who issues (per-store profile), what is sold (product fiscal data) and how
/// far the store is from issuing. No nota is issued and nothing leaves the system — emission needs
/// a provider and a certificate, which are a later step.
/// </summary>
public class FiscalService
{
    private const int IssueListLimit = 50;

    private readonly NexoDbContext _db;

    public FiscalService(NexoDbContext db) => _db = db;

    public async Task<FiscalProfileDto?> GetProfileAsync(CancellationToken ct)
    {
        var p = await _db.FiscalProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        return p is null ? null : Map(p);
    }

    public async Task<FiscalProfileDto> SaveProfileAsync(Guid tenantId, SaveFiscalProfileRequest r, CancellationToken ct)
    {
        var profile = await _db.FiscalProfiles.FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            profile = FiscalProfile.Create(tenantId);
            _db.FiscalProfiles.Add(profile);
        }
        var ie = r.StateRegistration.Trim();
        profile.Update(
            r.LegalName, r.TradeName, FiscalCodes.Digits(r.Cnpj),
            ie.Equals("ISENTO", StringComparison.OrdinalIgnoreCase) ? "ISENTO" : FiscalCodes.Digits(ie),
            r.MunicipalRegistration, Enum.Parse<TaxRegime>(r.TaxRegime, true),
            r.Street, r.Number, r.Complement, r.District, r.CityName, r.CityIbgeCode.Trim(),
            r.State.Trim().ToUpperInvariant(), FiscalCodes.Digits(r.ZipCode));
        await _db.SaveChangesAsync(ct);
        return Map(profile);
    }

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
        p.UpdateFiscal(Digits(r.Ncm), Digits(r.Cest), r.Origin, Digits(r.Cfop), Digits(r.TaxSituation));
        await _db.SaveChangesAsync(ct);
        return Map(p);
    }

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
            EmissionAvailable: false);
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
        p.Street, p.Number, p.Complement, p.District, p.CityName, p.CityIbgeCode, p.State, p.ZipCode, p.UpdatedAt);

    private static ProductFiscalDto Map(Product p) =>
        new(p.Id, p.Code, p.Name, p.Barcode, p.Ncm, p.Cest, p.FiscalOrigin, p.Cfop, p.TaxSituation);
}
