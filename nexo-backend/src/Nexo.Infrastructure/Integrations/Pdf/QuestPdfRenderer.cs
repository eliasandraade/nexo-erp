using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Nexo.Application.Integrations.Pdf;

namespace Nexo.Infrastructure.Integrations.Pdf;

public sealed class QuestPdfRenderer : IPdfRenderer
{
    static QuestPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfRenderResult RenderSaleReceipt(SaleReceiptRequest request)
    {
        var doc   = new SaleReceiptDocument(request);
        var bytes = doc.GeneratePdf();
        return new PdfRenderResult(bytes, $"recibo-{request.Sale.Number}.pdf");
    }

    public PdfRenderResult RenderCashCloseReport(CashCloseReportRequest request)
    {
        var doc   = new CashCloseReportDocument(request);
        var bytes = doc.GeneratePdf();
        return new PdfRenderResult(bytes, $"fechamento-caixa-{request.Session.Id:N}.pdf");
    }

    public PdfRenderResult RenderProductSheet(ProductSheetRequest request)
    {
        var doc   = new ProductSheetDocument(request);
        var bytes = doc.GeneratePdf();
        var fileCode = request.Product.Code ?? request.Product.Id.ToString("N");
        return new PdfRenderResult(bytes, $"ficha-produto-{fileCode}.pdf");
    }
}
