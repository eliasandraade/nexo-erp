using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// BUDGET PDF — what the client receives. Built from the budget by a pure builder (unit-tested): the
// margin is already inside every price; the margin %, the margin value, costs and internal notes are not
// part of this model at all, so the document cannot print them.
// ═══════════════════════════════════════════════════════════════════════

public sealed record BuildBudgetPdfLine(string Name, string? Stage, decimal Quantity, string Unit, decimal UnitPrice, decimal Total);

public sealed record BuildBudgetPdfGroup(string Kind, string Label, IReadOnlyList<BuildBudgetPdfLine> Lines, decimal Subtotal);

public sealed record BuildBudgetPdfModel(
    string                             CompanyName,
    byte[]?                            CompanyLogo,
    string?                            CompanyContact,
    string                             Title,
    int                                Version,
    DateOnly                           IssueDate,
    DateOnly?                          ValidUntil,
    string?                            ProjectName,
    string?                            ClientName,
    string?                            ProjectLocation,
    IReadOnlyList<BuildBudgetPdfGroup> Groups,
    decimal                            Total,
    decimal?                           AreaM2,
    decimal?                           TotalPerM2,
    string?                            Notes)
{
    public string FileName => $"orcamento-v{Version}-{Slug(Title)}.pdf";

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "obra" : slug.Length > 40 ? slug[..40] : slug;
    }
}

public static class BuildBudgetPdfModelBuilder
{
    private static readonly BuildCostKind[] Order =
        [BuildCostKind.Material, BuildCostKind.Labor, BuildCostKind.Service, BuildCostKind.Equipment, BuildCostKind.Other];

    public static string Label(BuildCostKind kind) => kind switch
    {
        BuildCostKind.Material  => "Materiais",
        BuildCostKind.Labor     => "Mão de obra",
        BuildCostKind.Service   => "Serviços",
        BuildCostKind.Equipment => "Equipamentos",
        _                       => "Outros",
    };

    /// <summary>
    /// Client prices: each line's cost × (1 + margin), rounded; the rounding residue goes to the largest line so
    /// the lines add up exactly to the budget's final price.
    /// </summary>
    public static BuildBudgetPdfModel Build(BuildBudget budget, IReadOnlyList<BuildBudgetItem> items, string companyName,
        byte[]? companyLogo, string? companyContact, string? projectName, string? clientName, string? projectLocation,
        IReadOnlyDictionary<Guid, string>? stageNames, DateOnly issueDate)
    {
        var factor = 1 + budget.MarginPercent / 100m;
        var total = BuildMoney.Round(items.Sum(i => i.TotalCost) * factor);
        var priced = items
            .Select(i => new
            {
                Item = i,
                UnitPrice = BuildMoney.Round(i.UnitCost * factor),
                Total = BuildMoney.Round(i.TotalCost * factor),
            })
            .ToList();
        var residue = total - priced.Sum(p => p.Total);
        if (residue != 0m && priced.Count > 0)
        {
            var largest = priced.MaxBy(p => p.Total)!;
            priced[priced.IndexOf(largest)] = largest with { Total = largest.Total + residue };
        }

        string? StageOf(BuildBudgetItem i)
            => i.StageId is { } sid && stageNames is not null && stageNames.TryGetValue(sid, out var n) ? n : i.StageName;

        var groups = Order
            .Select(kind =>
            {
                var lines = priced.Where(p => p.Item.Kind == kind)
                    .Select(p => new BuildBudgetPdfLine(p.Item.Name, StageOf(p.Item), p.Item.Quantity, p.Item.Unit, p.UnitPrice, p.Total))
                    .ToList();
                return new BuildBudgetPdfGroup(kind.ToString(), Label(kind), lines, lines.Sum(l => l.Total));
            })
            .Where(g => g.Lines.Count > 0)
            .ToList();

        var area = budget.AreaM2 is > 0m ? budget.AreaM2 : null;
        return new BuildBudgetPdfModel(
            CompanyName:     string.IsNullOrWhiteSpace(companyName) ? "Orçamento" : companyName.Trim(),
            CompanyLogo:     companyLogo,
            CompanyContact:  companyContact,
            Title:           budget.Name,
            Version:         budget.Version,
            IssueDate:       issueDate,
            ValidUntil:      budget.ValidUntil,
            ProjectName:     projectName,
            ClientName:      clientName,
            ProjectLocation: projectLocation,
            Groups:          groups,
            Total:           total,
            AreaM2:          area,
            TotalPerM2:      area is { } a ? BuildMoney.Round(total / a) : null,
            Notes:           budget.Notes);
    }
}
