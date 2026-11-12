using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Nexo.Api.Middleware;

namespace Nexo.UnitTests.Auth;

/// <summary>The route in a limit's key is the one the request was matched to, in one spelling, whatever the caller wrote.</summary>
public class RateLimitKeysTests
{
    private static HttpContext Request(string writtenPath, string? matchedTemplate)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = writtenPath;
        if (matchedTemplate is not null)
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(matchedTemplate), 0, EndpointMetadataCollection.Empty, matchedTemplate));
        return context;
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/Login")]
    [InlineData("/API/AUTH/LOGIN")]
    [InlineData("/api/auth/login/")]
    public void Every_spelling_of_an_address_gives_the_route_it_was_matched_to(string writtenPath)
        => RateLimitKeys.Route(Request(writtenPath, "api/auth/login")).Should().Be("api/auth/login");

    [Fact]
    public void The_template_is_normalized_too()
        => RateLimitKeys.Route(Request("/x", "/Api/Auth/Login/")).Should().Be("api/auth/login");

    [Fact]
    public void A_route_with_a_parameter_is_one_key_for_every_value()
    {
        var first = RateLimitKeys.Route(Request("/api/public/service/loja-a/appointments", "api/public/service/{slug}/appointments"));
        var second = RateLimitKeys.Route(Request("/api/public/service/loja-b/appointments", "api/public/service/{slug}/appointments"));

        first.Should().Be(second).And.Be("api/public/service/{slug}/appointments");
    }

    [Theory]
    [InlineData("/api/auth/Login/", "api/auth/login")]
    [InlineData("/API/AUTH/LOGIN", "api/auth/login")]
    [InlineData("", "")]
    public void With_no_matched_route_the_written_address_counts_in_one_spelling(string writtenPath, string expected)
        => RateLimitKeys.Route(Request(writtenPath, null)).Should().Be(expected);

    [Fact]
    public void Different_routes_keep_different_keys()
        => RateLimitKeys.Route(Request("/a", "api/auth/login")).Should().NotBe(RateLimitKeys.Route(Request("/a", "api/auth/forgot-password")));
}
