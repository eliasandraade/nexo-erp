using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Api.Controllers.Integrations;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public sealed class BarcodeControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private record Sut(BarcodeController Controller, IBarcodeProductLookupProvider Provider, IIntegrationFeatureFlags Flags);

    private static Sut Build(bool flagEnabled = true)
    {
        var provider = Substitute.For<IBarcodeProductLookupProvider>();
        var flags    = Substitute.For<IIntegrationFeatureFlags>();
        flags.OpenFoodFactsEnabled.Returns(flagEnabled);
        var logger     = NullLogger<BarcodeController>.Instance;
        var controller = new BarcodeController(provider, flags, logger);
        return new Sut(controller, provider, flags);
    }

    private static ProductLookupResult MakeResult(string barcode = "7891000100103") =>
        new(barcode, "Leite Moça", "Nestlé", "https://img.off.org/leite.jpg",
            "Laticínios", "395", "g", "OpenFoodFacts", 0.75);

    [Fact]
    public async Task LookupBarcode_FlagDisabled_ReturnsNotFound()
    {
        var sut = Build(flagEnabled: false);
        var result = await sut.Controller.LookupBarcode("7891000100103", default);
        Assert.IsType<NotFoundObjectResult>(result);
        await sut.Provider.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupBarcode_TooShort_ReturnsBadRequest()
    {
        var sut = Build();
        var result = await sut.Controller.LookupBarcode("123", default);
        Assert.IsType<BadRequestObjectResult>(result);
        await sut.Provider.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupBarcode_TooLong_ReturnsBadRequest()
    {
        var sut = Build();
        var result = await sut.Controller.LookupBarcode("123456789012345", default); // 15 digits
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task LookupBarcode_ValidBarcode_CallsProvider()
    {
        var sut = Build();
        sut.Provider.LookupAsync("7891000100103", Arg.Any<CancellationToken>()).Returns((ProductLookupResult?)null);
        await sut.Controller.LookupBarcode("7891000100103", default);
        await sut.Provider.Received(1).LookupAsync("7891000100103", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupBarcode_ProductFound_ReturnsFoundTrue()
    {
        var sut    = Build();
        var lookup = MakeResult();
        sut.Provider.LookupAsync("7891000100103", Arg.Any<CancellationToken>()).Returns(lookup);
        var result = await sut.Controller.LookupBarcode("7891000100103", default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var body   = ok.Value!;
        // Check found=true and data is not null via dynamic
        var found  = (bool)body.GetType().GetProperty("found")!.GetValue(body)!;
        Assert.True(found);
    }

    [Fact]
    public async Task LookupBarcode_ProductNotFound_ReturnsFoundFalse()
    {
        var sut = Build();
        sut.Provider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ProductLookupResult?)null);
        var result = await sut.Controller.LookupBarcode("7891000100103", default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var body   = ok.Value!;
        var found  = (bool)body.GetType().GetProperty("found")!.GetValue(body)!;
        Assert.False(found);
    }

    [Fact]
    public async Task LookupBarcode_ProviderThrows_ReturnsUnavailable()
    {
        var sut = Build();
        sut.Provider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ProductLookupResult?>(_ => throw new HttpRequestException("OFF down"));
        var result = await sut.Controller.LookupBarcode("7891000100103", default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var body   = ok.Value!;
        var found  = (bool)body.GetType().GetProperty("found")!.GetValue(body)!;
        Assert.False(found);
        var unavail = (bool?)body.GetType().GetProperty("unavailable")?.GetValue(body);
        Assert.True(unavail);
    }

    [Fact]
    public async Task LookupBarcode_BarcodeWithDashes_NormalizedToDigits()
    {
        // Barcode "789-1000-100103" should be normalized to "7891000100103"
        var sut = Build();
        sut.Provider.LookupAsync("7891000100103", Arg.Any<CancellationToken>()).Returns((ProductLookupResult?)null);
        await sut.Controller.LookupBarcode("789-1000-100103", default);
        await sut.Provider.Received(1).LookupAsync("7891000100103", Arg.Any<CancellationToken>());
    }
}
