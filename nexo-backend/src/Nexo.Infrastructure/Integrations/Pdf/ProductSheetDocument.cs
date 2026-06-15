using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Nexo.Application.Integrations.Pdf;

namespace Nexo.Infrastructure.Integrations.Pdf;

internal sealed class ProductSheetDocument : IDocument
{
    private readonly ProductSheetRequest _req;
    private static readonly System.Globalization.CultureInfo Ptbr =
        System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public ProductSheetDocument(ProductSheetRequest req) => _req = req;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        var p = _req.Product;
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(_req.TenantName).Bold().FontSize(16);
                col.Item().Text("FICHA DE PRODUTO").Bold().FontSize(13);
                col.Item().PaddingTop(4).LineHorizontal(1);
            });

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Text(p.Name).Bold().FontSize(14);
                col.Item().Text($"Código/SKU: {p.Code}");
                if (!string.IsNullOrWhiteSpace(p.Barcode))
                    col.Item().Text($"Código de barras: {p.Barcode}");

                col.Item().PaddingTop(8).LineHorizontal(1);

                col.Item().PaddingTop(8).Column(info =>
                {
                    info.Item().Text($"Preço de custo: {p.CostPrice.ToString("C2", Ptbr)}");
                    info.Item().Text($"Preço de venda: {p.SalePrice.ToString("C2", Ptbr)}");
                    info.Item().Text($"Unidade: {p.Unit}");
                    info.Item().Text($"Status: {(p.IsActive ? "Ativo" : "Inativo")}");
                    if (p.IsIngredient)
                        info.Item().Text("Tipo: Ingrediente");
                });

                if (p.TrackStock)
                {
                    col.Item().PaddingTop(8).LineHorizontal(1);
                    col.Item().PaddingTop(8).Text("Estoque:").Bold();
                    if (p.MinStockQuantity.HasValue)
                        col.Item().Text($"Estoque mínimo: {p.MinStockQuantity.Value.ToString("N2", Ptbr)}");
                    if (p.MaxStockQuantity.HasValue)
                        col.Item().Text($"Estoque máximo: {p.MaxStockQuantity.Value.ToString("N2", Ptbr)}");
                }

                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    col.Item().PaddingTop(8).Text("Descrição:").Bold();
                    col.Item().Text(p.Description);
                }

                col.Item().PaddingTop(16).Text($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });

            page.Footer().AlignCenter().Text("Gerado pelo Orken").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }
}
