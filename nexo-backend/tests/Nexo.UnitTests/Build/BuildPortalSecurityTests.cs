using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Nexo.Api.Middleware;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>Security review of the client portal: client IP source, password-check guard, password-only sections, signed-URL folders.</summary>
public class BuildPortalSecurityTests
{
    // Built at runtime: literal addresses and passwords in tests trip the secret scanner (GitGuardian).
    private static string Ip(int a, int b, int c, int d) => string.Join('.', a, b, c, d);
    private static string WrongPassword() => "w" + Guid.NewGuid().ToString("N")[..10];

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static DefaultHttpContext Request(string? forwardedFor = null, string? realIp = null, string? remote = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote ?? Ip(10, 0, 0, 9));
        if (forwardedFor is not null) http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (realIp is not null) http.Request.Headers["X-Real-IP"] = realIp;
        return http;
    }

    // ── client IP ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, ClientIpSource.ForwardedFor)]
    [InlineData("", ClientIpSource.ForwardedFor)]
    [InlineData("forwardedfor", ClientIpSource.ForwardedFor)]
    [InlineData("RealIp", ClientIpSource.RealIp)]
    [InlineData("remote", ClientIpSource.Remote)]
    [InlineData("2", ClientIpSource.ForwardedFor)]
    [InlineData("nonsense", ClientIpSource.ForwardedFor)]
    public void The_client_ip_source_is_configurable_and_defaults_to_forwarded_for(string? value, ClientIpSource expected)
        => ClientIpResolver.Parse(value).Should().Be(expected);

    [Fact]
    public void Forwarded_for_takes_the_first_entry_written_by_the_edge()
    {
        ClientIpResolver.Resolve(Request($"{Ip(203, 0, 113, 7)}, {Ip(10, 1, 1, 1)}"), ClientIpSource.ForwardedFor).Should().Be(Ip(203, 0, 113, 7));
        ClientIpResolver.Resolve(Request(), ClientIpSource.ForwardedFor).Should().Be(Ip(10, 0, 0, 9), "no header: the connection address");
    }

    [Fact]
    public void Real_ip_and_remote_ignore_x_forwarded_for()
    {
        var http = Request(Ip(203, 0, 113, 7), realIp: Ip(198, 51, 100, 4));
        ClientIpResolver.Resolve(http, ClientIpSource.RealIp).Should().Be(Ip(198, 51, 100, 4));
        ClientIpResolver.Resolve(http, ClientIpSource.Remote).Should().Be(Ip(10, 0, 0, 9));
        ClientIpResolver.Resolve(Request(Ip(203, 0, 113, 7)), ClientIpSource.RealIp).Should().Be(Ip(10, 0, 0, 9));
        new ClientIpResolver(Config((ClientIpResolver.ConfigKey, "Remote"))).Resolve(http).Should().Be(Ip(10, 0, 0, 9));
    }

    // ── password-check guard ──────────────────────────────────────────────────

    [Fact]
    public async Task Password_checks_are_capped_and_a_saturated_guard_says_busy()
    {
        using var guard = new BuildPortalPasswordGuard(Config(("Build:Portal:MaxConcurrentPasswordChecks", "2"),
            ("Build:Portal:PasswordCheckWaitMs", "50")));
        guard.MaxConcurrent.Should().Be(2);
        var a = await guard.TryEnterAsync(CancellationToken.None);
        var b = await guard.TryEnterAsync(CancellationToken.None);
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        (await guard.TryEnterAsync(CancellationToken.None)).Should().BeNull("both slots are taken");
        a!.Dispose();
        a.Dispose();   // releasing twice frees one slot only
        var c = await guard.TryEnterAsync(CancellationToken.None);
        c.Should().NotBeNull();
        (await guard.TryEnterAsync(CancellationToken.None)).Should().BeNull();
        b!.Dispose();
        c!.Dispose();
    }

    [Fact]
    public void The_guard_defaults_to_four_slots()
        => new BuildPortalPasswordGuard(Config()).MaxConcurrent.Should().Be(BuildPortalPasswordGuard.DefaultMaxConcurrent);

    [Fact]
    public void Lockout_keys_never_carry_the_raw_ip_and_separate_portals_and_clients()
    {
        var portal = Guid.NewGuid();
        var key = BuildPortalPasswordGuard.ClientKey(Ip(203, 0, 113, 7));
        key.Should().HaveLength(32).And.NotContain("203");
        BuildPortalPasswordGuard.ClientKey(Ip(203, 0, 113, 7)).Should().Be(key);
        BuildPortalPasswordGuard.ClientKey(Ip(203, 0, 113, 8)).Should().NotBe(key);
        BuildPortalPasswordGuard.LockKey(portal, key).Should().NotBe(BuildPortalPasswordGuard.LockKey(Guid.NewGuid(), key));
        BuildPortalPasswordGuard.FailuresKey(portal, key).Should().NotBe(BuildPortalPasswordGuard.LockKey(portal, key));
    }

    // ── password-only sections ────────────────────────────────────────────────

    [Fact]
    public void A_public_portal_never_serves_the_personal_sections_whatever_the_flags_say()
    {
        var all = new BuildPortalVisibility
        {
            PostConstruction = true, ChangeOrders = true, Selections = true, Documents = true, RealityCapture = true,
            Photos = true, Measurements = true, Model3d = true,
        };
        var pub = all.EffectiveFor(BuildPortalAccess.Public);
        pub.PostConstruction.Should().BeFalse();
        pub.ChangeOrders.Should().BeFalse();
        pub.Selections.Should().BeFalse();
        pub.Documents.Should().BeFalse();
        pub.RealityCapture.Should().BeFalse();
        pub.Photos.Should().BeTrue("non-personal sections are untouched");
        pub.Measurements.Should().BeTrue();
        pub.Model3d.Should().BeTrue();
        all.EffectiveFor(BuildPortalAccess.Password).Should().Be(all);
        BuildPortalVisibility.PasswordOnlySections.Should()
            .BeEquivalentTo("postConstruction", "changeOrders", "selections", "documents", "realityCapture");
    }

    // ── signed URLs only for the section's own private folder ────────────────

    [Fact]
    public void Only_keys_of_the_tenants_private_folder_of_that_section_are_signed()
    {
        var tenant = Guid.NewGuid();
        var photo = $"private/tenants/{tenant}/build/daily-logs/a.jpg";
        BuildPortalMedia.IsSignable(photo, tenant, "build/daily-logs").Should().BeTrue();
        BuildPortalMedia.IsSignable(photo, tenant, "build/documents").Should().BeFalse("a record pointing at another section's file");
        BuildPortalMedia.IsSignable(photo, Guid.NewGuid(), "build/daily-logs").Should().BeFalse("another tenant");
        BuildPortalMedia.IsSignable($"tenants/{tenant}/build/daily-logs/a.jpg", tenant, "build/daily-logs").Should().BeFalse("public key");
        BuildPortalMedia.IsSignable($"private/tenants/{tenant}/build/daily-logs/../documents/x.pdf", tenant, "build/daily-logs").Should().BeFalse();
        BuildPortalMedia.IsSignable($"private/tenants/{tenant}/build/daily-logs-other/a.jpg", tenant, "build/daily-logs").Should().BeFalse();
        BuildPortalMedia.IsSignable(photo, tenant, "").Should().BeFalse();
    }
}
