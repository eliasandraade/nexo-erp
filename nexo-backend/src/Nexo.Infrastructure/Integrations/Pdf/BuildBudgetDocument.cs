using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Nexo.Application.Modules.Build;

namespace Nexo.Infrastructure.Integrations.Pdf;

/// <summary>
/// The budget the client receives (Orken Build). Everything printed comes from <see cref="BuildBudgetPdfModel"/>,
/// which has no costs, margin or internal notes — prices already include the margin.
/// </summary>
internal sealed class BuildBudgetDocument : IDocument
{
    private static readonly System.Globalization.CultureInfo Ptbr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    private readonly BuildBudgetPdfModel _m;

    public BuildBudgetDocument(BuildBudgetPdfModel model) => _m = model;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Orçamento — {_m.Title}",
        Author = _m.CompanyName,
        Creator = "Orken Build",
    };

    private static string Money(decimal v) => v.ToString("C2", Ptbr);
    private static string Qty(decimal v) => v.ToString("0.###", Ptbr);

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.8f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9.5f));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    if (_m.CompanyLogo is { } logo)
                        row.ConstantItem(90).Height(50).Image(logo).FitArea();
                    row.RelativeItem().PaddingLeft(_m.CompanyLogo is null ? 0 : 10).Column(c =>
                    {
                        c.Item().Text(_m.CompanyName).Bold().FontSize(15);
                        if (!string.IsNullOrWhiteSpace(_m.CompanyContact))
                            c.Item().Text(_m.CompanyContact).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("ORÇAMENTO").Bold().FontSize(13);
                        c.Item().AlignRight().Text($"Versão {_m.Version}");
                        c.Item().AlignRight().Text($"Emitido em {_m.IssueDate:dd/MM/yyyy}");
                        if (_m.ValidUntil is { } v)
                            c.Item().AlignRight().Text($"Válido até {v:dd/MM/yyyy}").Bold();
                    });
                });
                col.Item().PaddingTop(6).LineHorizontal(1);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(6);
                col.Item().Text(_m.Title).Bold().FontSize(12);
                if (_m.ProjectName is not null) col.Item().Text($"Obra: {_m.ProjectName}");
                if (_m.ClientName is not null) col.Item().Text($"Cliente: {_m.ClientName}");
                if (_m.ProjectLocation is not null) col.Item().Text($"Local: {_m.ProjectLocation}");
                if (_m.AreaM2 is { } area) col.Item().Text($"Área: {Qty(area)} m²");

                foreach (var group in _m.Groups)
                {
                    col.Item().PaddingTop(8).Text(group.Label).Bold().FontSize(11);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(5);
                            c.RelativeColumn(2);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        table.Header(h =>
                        {
                            foreach (var title in new[] { "Item", "Etapa", "Qtd.", "Un.", "Preço unit.", "Total" })
                                h.Cell().BorderBottom(1).PaddingVertical(2).Text(title).Bold();
                        });
                        foreach (var l in group.Lines)
                        {
                            table.Cell().PaddingVertical(2).Text(l.Name);
                            table.Cell().PaddingVertical(2).Text(l.Stage ?? "—");
                            table.Cell().PaddingVertical(2).AlignRight().Text(Qty(l.Quantity));
                            table.Cell().PaddingVertical(2).Text(l.Unit);
                            table.Cell().PaddingVertical(2).AlignRight().Text(Money(l.UnitPrice));
                            table.Cell().PaddingVertical(2).AlignRight().Text(Money(l.Total));
                        }
                    });
                    col.Item().AlignRight().Text($"Subtotal {group.Label.ToLowerInvariant()}: {Money(group.Subtotal)}").SemiBold();
                }

                col.Item().PaddingTop(10).LineHorizontal(1);
                col.Item().AlignRight().Text($"TOTAL: {Money(_m.Total)}").Bold().FontSize(13);
                if (_m.TotalPerM2 is { } perM2)
                    col.Item().AlignRight().Text($"Valor por m²: {Money(perM2)}");

                if (!string.IsNullOrWhiteSpace(_m.Notes))
                {
                    col.Item().PaddingTop(10).Text("Observações").Bold();
                    col.Item().Text(_m.Notes);
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        });
    }
}
