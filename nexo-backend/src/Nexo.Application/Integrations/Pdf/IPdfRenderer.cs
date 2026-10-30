namespace Nexo.Application.Integrations.Pdf;

public interface IPdfRenderer
{
    PdfRenderResult RenderSaleReceipt(SaleReceiptRequest request);
    PdfRenderResult RenderCashCloseReport(CashCloseReportRequest request);
    PdfRenderResult RenderProductSheet(ProductSheetRequest request);
    /// <summary>Orken Build budget for the client (prices with margin applied; no costs, margin or internal notes).</summary>
    PdfRenderResult RenderBuildBudget(Nexo.Application.Modules.Build.BuildBudgetPdfModel model);
}
