using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Nexo.Api.Middleware;
using Nexo.Application.Features.Auth;

namespace Nexo.UnitTests.Auth;

/// <summary>
/// What comes out of the resolver partitions the limits and becomes a Redis key, and it comes from a request header. The source stays the same: the edge overwrites the header.
/// What changes is that only an address gets through, in one spelling.
/// </summary>
public class ClientIpValidationTests
{
    private const string Connection = "10.0.0.9";

    private static DefaultHttpContext Request(string? forwardedFor = null, string? realIp = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(Connection);
        if (forwardedFor is not null) http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (realIp is not null) http.Request.Headers["X-Real-IP"] = realIp;
        return http;
    }

    public static TheoryData<string> NoAddresses => new()
    {
        "nem-um-endereco",
        "unknown",
        "::1%aaaaaaaa",
        "fe80::1%eth0",
        "::1%" + new string('a', 16 * 1024),
        new string('a', 16 * 1024),
        "203.0.113.7:8080x",
        "1.2.3.4.5",
        "2001:db8::" + new string('0', 64),
    };

    [Theory]
    [MemberData(nameof(NoAddresses))]
    public void A_forwarded_value_that_is_no_address_falls_back_to_the_connection(string value)
        => ClientIpResolver.Resolve(Request(forwardedFor: value), ClientIpSource.ForwardedFor).Should().Be(Connection);

    [Theory]
    [MemberData(nameof(NoAddresses))]
    public void A_real_ip_value_that_is_no_address_falls_back_to_the_connection(string value)
        => ClientIpResolver.Resolve(Request(realIp: value), ClientIpSource.RealIp).Should().Be(Connection);

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("  203.0.113.7  ", "203.0.113.7")]
    [InlineData("203.0.113.7, 10.1.1.1", "203.0.113.7")]
    [InlineData("2001:db8::7", "2001:db8::7")]
    [InlineData("2001:DB8:0:0:0:0:0:7", "2001:db8::7")]
    [InlineData("2001:0db8::0007", "2001:db8::7")]
    public void An_address_comes_out_in_its_parsed_form(string value, string expected)
        => ClientIpResolver.Resolve(Request(forwardedFor: value), ClientIpSource.ForwardedFor).Should().Be(expected);

    [Fact]
    public void The_first_entry_decides_it_is_not_looked_for_further_down_the_list()
        => ClientIpResolver.Resolve(Request(forwardedFor: "lixo, 203.0.113.7"), ClientIpSource.ForwardedFor).Should().Be(Connection);

    [Fact]
    public void The_default_source_did_not_change()
        => ClientIpResolver.Parse(null).Should().Be(ClientIpSource.ForwardedFor);

    [Fact]
    public void No_key_of_the_failure_budget_grows_with_what_was_sent()
        => RefreshBudget.FailureKey("::1%" + new string('a', 16 * 1024)).Length.Should().BeLessThan(128);
}
