namespace Nexo.Application.Integrations.Pdf;

public interface IPdfRenderer
{
    PdfRenderResult RenderSaleReceipt(SaleReceiptRequest request);
    PdfRenderResult RenderCashCloseReport(CashCloseReportRequest request);
    PdfRenderResult RenderProductSheet(ProductSheetRequest request);
}
