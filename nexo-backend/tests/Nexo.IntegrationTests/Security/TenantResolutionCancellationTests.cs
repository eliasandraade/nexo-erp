using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Common.Observability;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O cliente desiste do pedido enquanto a resolução do tenant está em andamento. Nada falhou: o pedido termina
/// sem erro 500, sem registro de erro e sem deixar estado errado para o pedido seguinte.
/// </summary>
[Collection("Integration")]
public class TenantResolutionCancellationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly TestWebApplicationFactory _factory;

    public TenantResolutionCancellationTests(TestWebApplicationFactory factory) => _factory = factory;

    /// <summary>Onde o pedido para, e o que aconteceu com ele no servidor.</summary>
    private sealed class Probe
    {
        public volatile bool Armed;
        public required string Operation { get; init; }
        public required string KeyPrefix { get; init; }
        public required bool ContinueAfterCancel { get; init; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TokenCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> FinalStatus { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Cache de verdade, que espera no ponto escolhido até o token do pedido ser cancelado.</summary>
    private sealed class CacheThatWaitsForTheClient(IMemoryCache memory, Probe probe) : ICacheService
    {
        private readonly InMemoryCacheService _inner = new(memory);

        private async Task WaitAtProbeAsync(string operation, string key, CancellationToken ct)
        {
            if (!probe.Armed || operation != probe.Operation || !key.StartsWith(probe.KeyPrefix) || !key.EndsWith(":info"))
                return;

            probe.Reached.TrySetResult();
            try
            {
                // Limitado, para não travar o host se o token do pedido não chegar aqui.
                await Task.Delay(Patience * 2, ct);
            }
            catch (OperationCanceledException)
            {
                probe.TokenCancelled.TrySetResult();
                // Com ContinueAfterCancel, segue com o token já cancelado: o cliente saiu logo antes da próxima etapa.
                if (!probe.ContinueAfterCancel) throw;
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            await WaitAtProbeAsync("GET", key, ct);
            return await _inner.GetAsync<T>(key, ct);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
        {
            await WaitAtProbeAsync("SET", key, ct);
            await _inner.SetAsync(key, value, ttl, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);
        public Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
            => _inner.GetOrSetAsync(key, factory, ttl, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => _inner.ExistsAsync(key, ct);
        public Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default) => _inner.SetFlagAsync(key, ttl, ct);
        public Task<long?> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default) => _inner.IncrementAsync(key, window, ct);
    }

    private sealed class RecordingErrorTracker : IErrorTracker
    {
        public ConcurrentBag<ErrorOccurrence> Tracked { get; } = [];

        public Task TrackAsync(ErrorOccurrence occurrence, CancellationToken ct = default)
        {
            Tracked.Add(occurrence);
            return Task.CompletedTask;
        }
    }

    /// <summary>Por fora de todo o pipeline: registra como o pedido terminou, inclusive exceção que escape.</summary>
    private sealed class FinalStatusFilter(Probe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                var observed = probe.Armed;
                try
                {
                    await inner();
                    if (observed) probe.FinalStatus.TrySetResult(context.Response.StatusCode);
                }
                catch (Exception ex)
                {
                    if (observed) probe.FinalStatus.TrySetException(ex);
                    throw;
                }
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> HostWith(Probe probe, IErrorTracker tracker) => _factory.WithWebHostBuilder(b =>
        b.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddSingleton<IStartupFilter>(new FinalStatusFilter(probe));
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService, CacheThatWaitsForTheClient>();
            services.RemoveAll<IErrorTracker>();
            services.AddSingleton(tracker);
        }));

    private static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    [Theory]
    // O cliente sai enquanto a operação de cache está em andamento.
    [InlineData("GET", "user:", false)]
    [InlineData("SET", "user:", false)]
    [InlineData("GET", "tenant:", false)]
    [InlineData("SET", "tenant:", false)]
    // O cliente sai logo antes da etapa seguinte, que é consulta real ao banco.
    [InlineData("GET", "user:", true)]
    [InlineData("GET", "tenant:", true)]
    public async Task Client_leaving_during_tenant_resolution_is_not_an_error(string operation, string keyPrefix, bool continueAfterCancel)
    {
        var probe = new Probe { Operation = operation, KeyPrefix = keyPrefix, ContinueAfterCancel = continueAfterCancel };
        var tracker = new RecordingErrorTracker();
        await using var host = HostWith(probe, tracker);
        using var client = await LoggedInClientAsync(host);

        var requestId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-Request-Id", requestId);

        probe.Armed = true;
        using var clientGivesUp = new CancellationTokenSource();
        var sending = client.SendAsync(request, clientGivesUp.Token);

        await probe.Reached.Task.WaitAsync(Patience);
        await clientGivesUp.CancelAsync();

        // O cancelamento do cliente chegou à operação em andamento: o token do pedido foi repassado.
        await probe.TokenCancelled.Task.WaitAsync(Patience);
        await sending.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        var status = await probe.FinalStatus.Task.WaitAsync(Patience);
        probe.Armed = false;

        status.Should().Be(200, "ninguém escreveu resposta de erro: o status segue o inicial");
        tracker.Tracked.Should().BeEmpty("cliente que desiste não é erro inesperado");
        _factory.ServerLog.For(requestId).Should().Be("(no warning or error logged for this request)");

        // O pedido interrompido não deixou nada errado: o seguinte passa pela resolução e responde normalmente.
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        tracker.Tracked.Should().BeEmpty();
    }
}
