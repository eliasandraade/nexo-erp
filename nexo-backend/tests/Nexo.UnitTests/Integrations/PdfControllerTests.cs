using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Integrations.Pdf;
using Nexo.Api.Controllers;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public sealed class PdfControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UserId   = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // ── Fake PDF result ───────────────────────────────────────────────────────

    private static PdfRenderResult FakePdf(string name = "test.pdf") =>
        new(new byte[] { 1, 2, 3 }, name);

    // ── ProductsController helpers ────────────────────────────────────────────

    private record ProductSut(
        ProductsController Controller,
        IProductRepository Products,
        IPdfRenderer Renderer);

    private static ProductSut BuildProductsController()
    {
        var products   = Substitute.For<IProductRepository>();
        var stock      = Substitute.For<IStockRepository>();
        var tenant     = Substitute.For<ICurrentTenant>();
        var renderer   = Substitute.For<IPdfRenderer>();

        tenant.Id.Returns(TenantId);

        var service    = new ProductService(products, stock, tenant);
        var controller = new ProductsController(service, renderer);

        return new ProductSut(controller, products, renderer);
    }

    private static Product FakeProduct() =>
        Product.Create(TenantId, "TEST01", "Test Product", ProductUnit.Un, 10m);

    // ── SalesController helpers ───────────────────────────────────────────────

    private record SaleSut(
        SalesController Controller,
        ISaleRepository Sales,
        IPdfRenderer Renderer);

    private static SaleSut BuildSalesController()
    {
        var sales      = Substitute.For<ISaleRepository>();
        var products   = Substitute.For<IProductRepository>();
        var stock      = Substitute.For<IStockRepository>();
        var cash       = Substitute.For<ICashRepository>();
        var financial  = Substitute.For<IFinancialRepository>();
        var uow        = Substitute.For<IUnitOfWork>();
        var tenant     = Substitute.For<ICurrentTenant>();
        var user       = Substitute.For<ICurrentUser>();
        var renderer   = Substitute.For<IPdfRenderer>();

        tenant.Id.Returns(TenantId);
        user.UserId.Returns(UserId);

        var service    = new SaleService(sales, products, stock, cash, financial, uow, tenant, user);
        var controller = new SalesController(service, renderer);

        return new SaleSut(controller, sales, renderer);
    }

    private static Sale FakeSale() =>
        Sale.Create(TenantId, 1, UserId);

    // ── CashController helpers ────────────────────────────────────────────────

    private record CashSut(
        CashController Controller,
        ICashRepository Cash,
        IPdfRenderer Renderer);

    private static CashSut BuildCashController()
    {
        var cash     = Substitute.For<ICashRepository>();
        var tenant   = Substitute.For<ICurrentTenant>();
        var user     = Substitute.For<ICurrentUser>();
        var renderer = Substitute.For<IPdfRenderer>();

        tenant.Id.Returns(TenantId);
        user.UserId.Returns(UserId);

        var service    = new CashService(cash, tenant, user);
        var controller = new CashController(service, renderer);

        return new CashSut(controller, cash, renderer);
    }

    private static CashSession FakeSession() =>
        CashSession.Open(TenantId, UserId, 100m);

    // ═════════════════════════════════════════════════════════════════════════
    // 1. GetProductSheet — product not found → NotFoundException propagates
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProductSheet_ProductNotFound_Returns404()
    {
        var sut = BuildProductsController();
        sut.Products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.Controller.GetProductSheet(Guid.NewGuid(), default));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. GetProductSheet — found → FileContentResult with application/pdf
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProductSheet_Found_ReturnsPdf()
    {
        var sut = BuildProductsController();
        sut.Products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(FakeProduct());
        sut.Renderer.RenderProductSheet(Arg.Any<ProductSheetRequest>())
            .Returns(FakePdf("product.pdf"));

        var result = await sut.Controller.GetProductSheet(Guid.NewGuid(), default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. GetProductSheet — found → bytes are non-empty
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProductSheet_Found_PdfHasBytes()
    {
        var sut = BuildProductsController();
        sut.Products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(FakeProduct());
        sut.Renderer.RenderProductSheet(Arg.Any<ProductSheetRequest>())
            .Returns(FakePdf("product.pdf"));

        var result = await sut.Controller.GetProductSheet(Guid.NewGuid(), default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.True(file.FileContents.Length > 0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. GetSaleReceipt — sale not found → NotFoundException propagates
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSaleReceipt_SaleNotFound_Returns404()
    {
        var sut = BuildSalesController();
        sut.Sales.GetByIdWithItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.Controller.GetSaleReceipt(Guid.NewGuid(), default));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. GetSaleReceipt — found → FileContentResult with application/pdf
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSaleReceipt_Found_ReturnsPdf()
    {
        var sut = BuildSalesController();
        sut.Sales.GetByIdWithItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(FakeSale());
        sut.Renderer.RenderSaleReceipt(Arg.Any<SaleReceiptRequest>())
            .Returns(FakePdf("sale.pdf"));

        var result = await sut.Controller.GetSaleReceipt(Guid.NewGuid(), default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. GetCashCloseReport — session not found → NotFoundException propagates
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCashCloseReport_SessionNotFound_Returns404()
    {
        var sut = BuildCashController();
        sut.Cash.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((CashSession?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.Controller.GetCashCloseReport(Guid.NewGuid(), default));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 7. GetCashCloseReport — found → FileContentResult with application/pdf
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCashCloseReport_Found_ReturnsPdf()
    {
        var sut = BuildCashController();
        sut.Cash.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(FakeSession());
        sut.Cash.GetMovementsBySessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CashMovement>());
        sut.Renderer.RenderCashCloseReport(Arg.Any<CashCloseReportRequest>())
            .Returns(FakePdf("cash.pdf"));

        var result = await sut.Controller.GetCashCloseReport(Guid.NewGuid(), default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 8. GetProductSheet — renderer is called with the correct product id
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProductSheet_RendererReceivesCorrectProductId()
    {
        var sut       = BuildProductsController();
        var productId = Guid.NewGuid();
        var product   = FakeProduct();

        sut.Products.GetByIdAsync(productId, Arg.Any<CancellationToken>())
            .Returns(product);
        sut.Renderer.RenderProductSheet(Arg.Any<ProductSheetRequest>())
            .Returns(FakePdf("product.pdf"));

        await sut.Controller.GetProductSheet(productId, default);

        sut.Renderer.Received(1).RenderProductSheet(
            Arg.Is<ProductSheetRequest>(r => r.Product.Id == product.Id));
    }
}
