using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Nexo.Application.Integrations.Pdf;

namespace Nexo.Infrastructure.Integrations.Pdf;

internal sealed class SaleReceiptDocument : IDocument
{
    private readonly SaleReceiptRequest _req;
    private static readonly System.Globalization.CultureInfo Ptbr =
        System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public SaleReceiptDocument(SaleReceiptRequest req) => _req = req;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(_req.TenantName).Bold().FontSize(16);
                col.Item().Text("RECIBO DE VENDA").Bold().FontSize(13);
                col.Item().Text($"Nº {_req.Sale.Number}   {_req.Sale.ConfirmedAt?.ToLocalTime():dd/MM/yyyy HH:mm}");
                if (!string.IsNullOrWhiteSpace(_req.Sale.CustomerName))
                    col.Item().Text($"Cliente: {_req.Sale.CustomerName}");
                col.Item().Text($"Vendedor: {_req.Sale.SoldByName}");
                col.Item().PaddingTop(4).LineHorizontal(1);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                // Items table
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(5); // item name
                        cols.RelativeColumn(2); // qty
                        cols.RelativeColumn(3); // unit price
                        cols.RelativeColumn(3); // total
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Item").Bold();
                        header.Cell().AlignRight().Text("Qtd").Bold();
                        header.Cell().AlignRight().Text("Preço Unit.").Bold();
                        header.Cell().AlignRight().Text("Total").Bold();
                    });

                    foreach (var item in _req.Sale.Items)
                    {
                        table.Cell().Text(item.ProductName);
                        table.Cell().AlignRight().Text(item.Quantity.ToString("N2", Ptbr));
                        table.Cell().AlignRight().Text(item.UnitPrice.ToString("C2", Ptbr));
                        table.Cell().AlignRight().Text(item.Total.ToString("C2", Ptbr));
                    }
                });

                col.Item().PaddingTop(8).LineHorizontal(1);

                // Totals
                col.Item().PaddingTop(4).AlignRight().Column(totals =>
                {
                    totals.Item().Text($"Subtotal: {_req.Sale.Subtotal.ToString("C2", Ptbr)}");
                    if (_req.Sale.DiscountAmount > 0)
                        totals.Item().Text($"Desconto: -{_req.Sale.DiscountAmount.ToString("C2", Ptbr)}");
                    if (_req.Sale.TaxAmount > 0)
                        totals.Item().Text($"Taxas: {_req.Sale.TaxAmount.ToString("C2", Ptbr)}");
                    totals.Item().Text($"TOTAL: {_req.Sale.Total.ToString("C2", Ptbr)}").Bold().FontSize(12);
                });

                // Payments
                if (_req.Sale.Payments.Count > 0)
                {
                    col.Item().PaddingTop(8).Text("Formas de Pagamento:").Bold();
                    foreach (var p in _req.Sale.Payments)
                        col.Item().Text($"  {p.Method}: {p.Amount.ToString("C2", Ptbr)}");
                }

                if (!string.IsNullOrWhiteSpace(_req.Sale.Notes))
                {
                    col.Item().PaddingTop(8).Text("Observações:").Bold();
                    col.Item().Text(_req.Sale.Notes);
                }
            });

            page.Footer().AlignCenter().Text("Gerado pelo Orken").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }
}
