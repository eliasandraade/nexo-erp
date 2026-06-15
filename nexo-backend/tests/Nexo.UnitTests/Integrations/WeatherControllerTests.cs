using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Integrations.Weather;
using Nexo.Api.Controllers.Integrations;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public sealed class WeatherControllerTests
{
    private record Sut(WeatherController Controller, IWeatherProvider Provider, IIntegrationFeatureFlags Flags);

    private static Sut Build(bool flagEnabled = true)
    {
        var provider = Substitute.For<IWeatherProvider>();
        var flags    = Substitute.For<IIntegrationFeatureFlags>();
        flags.WeatherEnabled.Returns(flagEnabled);
        var logger     = NullLogger<WeatherController>.Instance;
        var controller = new WeatherController(provider, flags, logger);
        return new Sut(controller, provider, flags);
    }

    private static WeatherResult MakeResult(DateOnly? date = null) => new(
        Latitude: -23.55,
        Longitude: -46.63,
        Date: date ?? DateOnly.FromDateTime(DateTime.UtcNow),
        TemperatureMax: 28.5,
        TemperatureMin: 22.1,
        PrecipitationMm: 2.3,
        WeatherCode: 63,
        Description: "Chuva moderada",
        Summary: "28°C / 22°C · Chuva moderada · Chuva: 2.3mm"
    );

    [Fact]
    public async Task GetCurrent_FlagDisabled_ReturnsNotFound()
    {
        var sut = Build(flagEnabled: false);
        var result = await sut.Controller.GetCurrent(-23.55, -46.63, default);
        Assert.IsType<NotFoundObjectResult>(result);
        await sut.Provider.DidNotReceive().GetCurrentAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrent_InvalidCoordinates_ReturnsBadRequest()
    {
        var sut = Build();
        var result = await sut.Controller.GetCurrent(200.0, 0, default); // lat out of range
        Assert.IsType<BadRequestObjectResult>(result);
        await sut.Provider.DidNotReceive().GetCurrentAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrent_WeatherFound_ReturnsFoundTrue()
    {
        var sut = Build();
        sut.Provider.GetCurrentAsync(-23.55, -46.63, Arg.Any<CancellationToken>()).Returns(MakeResult());
        var result = await sut.Controller.GetCurrent(-23.55, -46.63, default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var found  = (bool)ok.Value!.GetType().GetProperty("found")!.GetValue(ok.Value)!;
        Assert.True(found);
    }

    [Fact]
    public async Task GetCurrent_WeatherNotFound_ReturnsFoundFalse()
    {
        var sut = Build();
        sut.Provider.GetCurrentAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns((WeatherResult?)null);
        var result = await sut.Controller.GetCurrent(-23.55, -46.63, default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        var found  = (bool)ok.Value!.GetType().GetProperty("found")!.GetValue(ok.Value)!;
        Assert.False(found);
    }

    [Fact]
    public async Task GetCurrent_ProviderThrows_ReturnsUnavailable()
    {
        var sut = Build();
        sut.Provider.GetCurrentAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
           .Returns<WeatherResult?>(_ => throw new HttpRequestException("Open-Meteo down"));
        var result  = await sut.Controller.GetCurrent(-23.55, -46.63, default);
        var ok      = Assert.IsType<OkObjectResult>(result);
        var unavail = (bool?)ok.Value!.GetType().GetProperty("unavailable")?.GetValue(ok.Value);
        Assert.True(unavail);
    }

    [Fact]
    public async Task GetHistory_FlagDisabled_ReturnsNotFound()
    {
        var sut  = Build(flagEnabled: false);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var result = await sut.Controller.GetHistory(-23.55, -46.63, date, default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetHistory_FutureDate_ReturnsBadRequest()
    {
        var sut  = Build();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var result = await sut.Controller.GetHistory(-23.55, -46.63, date, default);
        Assert.IsType<BadRequestObjectResult>(result);
        await sut.Provider.DidNotReceive().GetHistoryAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHistory_ValidPastDate_CallsProvider()
    {
        var sut  = Build();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        sut.Provider.GetHistoryAsync(-23.55, -46.63, date, Arg.Any<CancellationToken>()).Returns(MakeResult(date));
        var result = await sut.Controller.GetHistory(-23.55, -46.63, date, default);
        var ok     = Assert.IsType<OkObjectResult>(result);
        await sut.Provider.Received(1).GetHistoryAsync(-23.55, -46.63, date, Arg.Any<CancellationToken>());
    }
}
