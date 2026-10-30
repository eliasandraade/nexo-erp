using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Pure budget arithmetic (unit-tested): totals per kind, margin, per m², and the mapping to the budget DTO
/// shared by the budget services.
/// </summary>
public static class BuildBudgetCalculator
{
    public static BuildBudgetTotalsDto Totals(IEnumerable<(BuildCostKind Kind, decimal TotalCost)> items,
        decimal marginPercent, decimal? areaM2)
    {
        var byKind = items.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => BuildMoney.Round(g.Sum(x => x.TotalCost)));
        decimal Of(BuildCostKind k) => byKind.GetValueOrDefault(k);
        var subtotal = BuildMoney.Round(byKind.Values.Sum());
        var total = BuildMoney.Round(subtotal * (1 + marginPercent / 100m));
        var area = areaM2 is > 0m ? areaM2 : null;
        return new BuildBudgetTotalsDto(
            Of(BuildCostKind.Material), Of(BuildCostKind.Labor), Of(BuildCostKind.Service), Of(BuildCostKind.Equipment),
            Of(BuildCostKind.Other), subtotal, marginPercent, total - subtotal, total, areaM2,
            area is { } a ? BuildMoney.Round(subtotal / a) : null,
            area is { } b ? BuildMoney.Round(total / b) : null);
    }

    public static BuildCostKind ParseKind(string? kind, BuildCostKind fallback = BuildCostKind.Other)
    {
        if (string.IsNullOrWhiteSpace(kind)) return fallback;
        return Enum.TryParse<BuildCostKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k)
            ? k
            : throw new Nexo.Domain.Exceptions.DomainException(
                $"Tipo de item inválido: '{kind}'. Use Material, Labor, Service, Equipment ou Other.", "invalid_kind");
    }

    public static bool ValidKind(string? kind)
        => kind is null || (Enum.TryParse<BuildCostKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k));

    public static BuildBudgetDto ToDto(BuildBudget b, IEnumerable<BuildBudgetItem> items, DateOnly today)
    {
        var list = items.ToList();
        var totals = Totals(list.Select(i => (i.Kind, i.TotalCost)), b.MarginPercent, b.AreaM2);
        var area = b.AreaM2 is > 0m ? b.AreaM2 : null;
        return new BuildBudgetDto(
            Id:               b.Id,
            ProjectId:        b.ProjectId,
            Name:             b.Name,
            Status:           b.Status.ToString(),
            TotalCost:        b.TotalCost,
            MarginPercent:    b.MarginPercent,
            FinalPrice:       b.FinalPrice,
            Items:            list.Select(ToItemDto).ToList(),
            CreatedAt:        b.CreatedAt,
            UpdatedAt:        b.UpdatedAt,
            ValidUntil:       b.ValidUntil,
            IsExpired:        b.IsExpiredOn(today),
            Notes:            b.Notes,
            InternalNotes:    b.InternalNotes,
            AreaM2:           b.AreaM2,
            CostPerM2:        area is { } a ? BuildMoney.Round(b.TotalCost / a) : null,
            PricePerM2:       area is { } p ? BuildMoney.Round(b.FinalPrice / p) : null,
            Version:          b.Version,
            ParentBudgetId:   b.ParentBudgetId,
            RootBudgetId:     b.FamilyId,
            SourceTemplateId: b.SourceTemplateId,
            Totals:           totals,
            ApprovedAt:       Utc(b.ApprovedAt),
            ApprovedByName:   b.ApprovedByName,
            RejectedAt:       Utc(b.RejectedAt),
            RejectedByName:   b.RejectedByName,
            RejectionReason:  b.RejectionReason);
    }

    public static BuildBudgetItemDto ToItemDto(BuildBudgetItem i) => new(
        Id:        i.Id,
        BudgetId:  i.BudgetId,
        StageId:   i.StageId,
        Name:      i.Name,
        Category:  i.Category,
        Quantity:  i.Quantity,
        Unit:      i.Unit,
        UnitCost:  i.UnitCost,
        TotalCost: i.TotalCost,
        CreatedAt: i.CreatedAt,
        UpdatedAt: i.UpdatedAt,
        Kind:      i.Kind.ToString(),
        StageName: i.StageName);

    public static DateTimeOffset? Utc(DateTime? d)
        => d is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc), TimeSpan.Zero) : null;
}
