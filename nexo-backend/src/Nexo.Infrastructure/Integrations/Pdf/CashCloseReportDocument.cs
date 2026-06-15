using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Nexo.Application.Integrations.Pdf;

namespace Nexo.Infrastructure.Integrations.Pdf;

internal sealed class CashCloseReportDocument : IDocument
{
    private readonly CashCloseReportRequest _req;
    private static readonly System.Globalization.CultureInfo Ptbr =
        System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public CashCloseReportDocument(CashCloseReportRequest req) => _req = req;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        var s = _req.Session;
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(_req.TenantName).Bold().FontSize(16);
                col.Item().Text("RELATÓRIO DE FECHAMENTO DE CAIXA").Bold().FontSize(13);
                col.Item().PaddingTop(4).LineHorizontal(1);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Text($"Operador abertura: {s.OpenedByName}");
                col.Item().Text($"Abertura: {s.OpenedAt.ToLocalTime():dd/MM/yyyy HH:mm}");
                if (!string.IsNullOrWhiteSpace(s.ClosedByName))
                    col.Item().Text($"Operador fechamento: {s.ClosedByName}");
                if (s.ClosedAt.HasValue)
                    col.Item().Text($"Fechamento: {s.ClosedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}");

                col.Item().PaddingTop(8).LineHorizontal(1);
                col.Item().PaddingTop(8).Text("Valores:").Bold();
                col.Item().Text($"Saldo inicial: {s.OpeningBalance.ToString("C2", Ptbr)}");
                if (s.ClosingBalance.HasValue)
                    col.Item().Text($"Saldo final: {s.ClosingBalance.Value.ToString("C2", Ptbr)}");

                if (s.Movements is { Count: > 0 })
                {
                    col.Item().PaddingTop(8).Text("Movimentações:").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(4);
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(3);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Descrição").Bold();
                            h.Cell().Text("Tipo").Bold();
                            h.Cell().AlignRight().Text("Valor").Bold();
                        });
                        foreach (var m in s.Movements)
                        {
                            table.Cell().Text(m.Description);
                            table.Cell().Text(m.MovementType);
                            table.Cell().AlignRight().Text(m.Amount.ToString("C2", Ptbr));
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(s.Notes))
                {
                    col.Item().PaddingTop(8).Text("Observações:").Bold();
                    col.Item().Text(s.Notes);
                }
            });

            page.Footer().AlignCenter().Text("Gerado pelo Orken").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }
}
