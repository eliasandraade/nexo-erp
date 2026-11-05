using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>The facts of an operation, whatever module produced them.</summary>
public sealed record TaxOperationFacts(
    TaxTransactionSource Source, Guid? SourceId, TaxOperationRequest Operation);

/// <summary>
/// Turns a confirmed sale into tax facts. Sales does not know about taxes: this reader only looks at
/// the sale (read-only) and states its assumptions in <see cref="Assumptions"/>.
/// </summary>
public sealed class SaleTaxFactsReader
{
    /// <summary>What the mapping assumes — shown with every calculation made from a sale.</summary>
    public static readonly IReadOnlyList<string> Assumptions =
    [
        "Descontos dados no PDV foram tratados como incondicionais: para isso precisam constar do documento fiscal (LC 214/2025, art. 12, § 3º).",
        "O desconto e os acréscimos da venda foram rateados entre os itens pelo valor líquido de cada um.",
        "A classificação de cada item (tipo, NCM/NBS e cClassTrib) vem da classificação tributária do produto definida pela empresa; sem ela a V1 não calcula a venda (classificação insuficiente), em vez de presumir o regime geral.",
        "Venda no PDV é um fornecimento único e imediato: data da venda = data do fornecimento = data do documento.",
        "ICMS, ISS, PIS e Cofins embutidos no preço não são conhecidos na venda — a base pode estar maior do que a real.",
    ];

    private readonly NexoDbContext _db;
    private readonly TaxClassificationService _classifications;

    public SaleTaxFactsReader(NexoDbContext db, TaxClassificationService classifications)
    {
        _db = db;
        _classifications = classifications;
    }

    public async Task<TaxOperationFacts> ReadAsync(Guid saleId, CancellationToken ct)
    {
        var sale = await _db.Sales.AsNoTracking()
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new NotFoundException("Venda", saleId);
        if (sale.ConfirmedAt is null || sale.Status is SaleStatus.Draft or SaleStatus.Cancelled)
            throw new DomainException("Só uma venda confirmada tem fatos fiscais para calcular.", "tax_sale_not_confirmed");

        var items = sale.Items.OrderBy(i => i.Id).ToList();
        var gross = items.Select(i => Cents(i.Quantity * i.UnitPrice)).ToList();
        var net = items.Select((i, k) => gross[k] - i.DiscountAmount).ToList();
        var saleDiscount = Allocate(sale.DiscountAmount, net);
        var surcharges = Allocate(sale.SurchargesAmount, net);

        var classified = await _classifications.ForProductsAsync(items.Select(i => i.ProductId), ct);
        var lines = items.Select((i, k) =>
        {
            // The company's classification of the product, when set; otherwise only the product's NCM and no cClassTrib (refused).
            var c = classified.GetValueOrDefault(i.ProductId);
            return new TaxLineRequest(
                c?.ItemKind ?? TaxItemKind.Good, i.Product?.Name ?? "Item", c?.Ncm ?? i.Product?.Ncm, i.Quantity, gross[k],
                UnconditionalDiscount: i.DiscountAmount + saleDiscount[k], ConditionalDiscount: 0m, Surcharges: surcharges[k],
                ExcludedTaxes: null, SpecialTreatment: null, Nbs: c?.Nbs, CClassTrib: c?.CClassTrib);
        }).ToList();

        var date = DateOnly.FromDateTime(BusinessClock.ToBrazil(sale.ConfirmedAt.Value));
        return new TaxOperationFacts(TaxTransactionSource.Sale, sale.Id,
            new TaxOperationRequest(date, TaxDirection.Outbound, null, null, $"Venda #{sale.Number}", lines,
                new TaxOperationTiming(TaxSupplyPattern.SingleSupply, DocumentDate: date, SupplyDate: date)));
    }

    /// <summary>Splits an amount over weights in cents, the remainder going to the largest fractions (sums exactly).</summary>
    public static IReadOnlyList<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var total = weights.Sum();
        if (amount == 0 || total <= 0) return result;

        var cents = (long)decimal.Round(amount * 100m);
        var raw = weights.Select(w => cents * w / total).ToList();
        var floors = raw.Select(r => (long)decimal.Floor(r)).ToList();
        var left = cents - floors.Sum();
        foreach (var k in raw.Select((r, k) => (Fraction: r - decimal.Floor(r), k))
                     .OrderByDescending(x => x.Fraction).ThenBy(x => x.k).Take((int)left).Select(x => x.k))
            floors[k]++;
        for (var k = 0; k < result.Length; k++) result[k] = floors[k] / 100m;
        return result;
    }

    private static decimal Cents(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
