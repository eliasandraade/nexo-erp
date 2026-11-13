using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>The whole API on a real Kestrel: the test server does not enforce a body limit, and a body with no declared size is only refused while it is being read.</summary>
public sealed class KestrelWebApplicationFactory : TestWebApplicationFactory
{
    private IHost? _kestrel;

    /// <summary>Where the real server listens.</summary>
    public Uri Address => new(_kestrel!.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

    public IServiceProvider KestrelServices => _kestrel!.Services;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();
        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));
        _kestrel = builder.Build();
        _kestrel.Start();
        testHost.Start();
        return testHost;
    }

    public override async Task DisposeAsync()
    {
        if (_kestrel is not null)
        {
            await _kestrel.StopAsync();
            _kestrel.Dispose();
        }
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RenewalKestrelCollection : ICollectionFixture<KestrelWebApplicationFactory>
{
    public const string Name = "RenewalKestrel";
}

[Collection(RenewalKestrelCollection.Name)]
public sealed class RenewalBodyLimitKestrelTests
{
    private const int LimitBytes = 16 * 1024;

    private readonly KestrelWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RenewalBodyLimitKestrelTests(KestrelWebApplicationFactory factory)
    {
        _factory = factory;
        _ = factory.Services;
        _client = new HttpClient { BaseAddress = factory.Address };
    }

    /// <summary>What a client sends when it does not say the size: only the limit of Kestrel stops it, while it is being read.</summary>
    private sealed class ChunkedContent(string text) : HttpContent
    {
        public ChunkedContent(int size) : this(JsonSerializer.Serialize(new { refreshToken = new string('x', size) }))
        {
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes.AsMemory(0, 1000));
            await stream.FlushAsync();
            await stream.WriteAsync(bytes.AsMemory(1000));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static StringContent Sized(int size) => new(JsonSerializer.Serialize(new { refreshToken = new string('x', size) }), Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> PostChunkedAsync(string route, int size)
    {
        var content = new ChunkedContent(size);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return await _client.PostAsync(route, content);
    }

    private async Task<int> ErrorEventsAsync()
    {
        using var scope = _factory.KestrelServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().ErrorEvents.AsNoTracking().CountAsync();
    }

    [Theory]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_body_past_the_limit_that_does_not_say_its_size_gets_413_and_no_error_event(string route)
    {
        var before = await ErrorEventsAsync();

        var response = await PostChunkedAsync(route, LimitBytes + 4 * 1024);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await response.Content.ReadAsStringAsync());
        (await ErrorEventsAsync()).Should().Be(before, "the client sent too much: nothing failed here");
    }

    [Theory]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_body_past_the_limit_that_says_its_size_gets_413(string route)
    {
        var response = await _client.PostAsync(route, Sized(LimitBytes + 4 * 1024));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_body_of_a_real_size_is_read(string route)
    {
        var response = await PostChunkedAsync(route, 4 * 1024);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "it was read, and the token is no good");
    }

    /// <summary>The proof of the review: 120 of these took the process from 825 MB to 3.7 GB. Kestrel stops reading at the limit, and most of it is never even sent.</summary>
    [Theory]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_body_of_29_MB_is_cut_off_at_the_limit(string route)
    {
        const int total = 29 * 1024 * 1024;
        var content = new PiecewiseContent(total);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        HttpResponseMessage? response = null;
        try { response = await _client.PostAsync(route, content); }
        catch (HttpRequestException) { /* the server closed the connection at the limit: that is the point */ }

        content.Written.Should().BeLessThan(total);
        response?.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    private sealed class PiecewiseContent(int total) : HttpContent
    {
        public int Written { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var piece = new byte[64 * 1024];
            Array.Fill(piece, (byte)'x');
            await stream.WriteAsync("{\"refreshToken\":\""u8.ToArray());
            while (Written < total)
            {
                await stream.WriteAsync(piece);
                Written += piece.Length;
            }
            await stream.WriteAsync("\"}"u8.ToArray());
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
