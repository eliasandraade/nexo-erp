using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Cache;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Os testes com Redis de verdade rodam um de cada vez, sozinhos. O disjuntor do <see cref="RedisCacheService"/> é estático
/// (vale para o processo inteiro): uma queda provocada aqui não pode derrubar o cache de outro host, nem a carga dos demais
/// testes pode estourar os 500 ms de uma chamada e abrir o disjuntor aqui.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisRealCollection
{
    public const string Name = "RedisReal";
}

/// <summary>
/// O host de teste com o cache de produção: o <see cref="RedisCacheService"/> e o multiplexador registrados pela própria
/// <c>AddInfrastructure</c> (com as opções de produção), falando com um Redis só desta classe de testes, na mesma versão e
/// com a mesma persistência do serviço em produção (<c>--save 60 1</c>, em <c>.railway/railway.ts</c>). Nada é reaproveitado
/// de produção. A porta é fixa para o contêiner voltar no mesmo endereço depois de parado, como o serviço de verdade.
/// </summary>
public sealed class RedisWebApplicationFactory : TestWebApplicationFactory
{
    private readonly int _port = FreeTcpPort();
    private readonly RedisContainer _redis;

    public RedisWebApplicationFactory()
        => _redis = new RedisBuilder("redis:8.2.10")
            .WithPortBinding(_port, RedisBuilder.RedisPort)
            .WithCommand("--save", "60", "1")
            .Build();

    public override async Task InitializeAsync()
    {
        await _redis.StartAsync();
        await base.InitializeAsync();
        await WaitForCacheAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // A connection string faz a AddInfrastructure registrar o Redis como em produção; a classe base troca o cache pelo
        // de memória logo depois, então o cache real é recolocado por cima.
        builder.UseSetting("ConnectionStrings:Redis", $"127.0.0.1:{_port}");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService, RedisCacheService>();
        });
    }

    /// <summary>O Redis direto, sem o serviço de cache: para conferir o que está gravado de verdade.</summary>
    public IDatabase RedisDatabase => Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    /// <summary>As chaves gravadas que atendem ao padrão.</summary>
    public IReadOnlyList<string> RedisKeys(string pattern)
    {
        var mux = Services.GetRequiredService<IConnectionMultiplexer>();
        return mux.GetServer(mux.GetEndPoints()[0]).Keys(pattern: pattern).Select(k => k.ToString()).ToList();
    }

    /// <summary>Para o contêiner: o Redis grava o snapshot e sai, como num redeploy do serviço.</summary>
    public Task StopRedisAsync() => _redis.StopAsync();

    /// <summary>Sobe de novo, no mesmo endereço, com as chaves do snapshot (que continuam contando o tempo de vida).</summary>
    public async Task StartRedisAsync()
    {
        await _redis.StartAsync();
        await WaitForCacheAsync();
    }

    /// <summary>Congela o processo: a conexão fica aberta e nada responde, que é o caso em que cada chamada estoura o tempo.</summary>
    public Task FreezeRedisAsync() => _redis.PauseAsync();

    public async Task UnfreezeRedisAsync()
    {
        await _redis.UnpauseAsync();
        await WaitForCacheAsync();
    }

    /// <summary>
    /// Espera o cache voltar a funcionar pelo caminho que a aplicação usa: conexão refeita e disjuntor fechado (até 10 s
    /// depois da última falha). Espera por uma condição; o resultado de nenhum teste depende deste tempo.
    /// </summary>
    public Task WaitForCacheAsync() => WaitForCacheAsync(Services);

    /// <summary>The same wait on a host derived from this one: each host has its own connection, which comes back on its own.</summary>
    public async Task WaitForCacheAsync(IServiceProvider services)
    {
        var key = $"teste:sonda:{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var scope = services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        while (true)
        {
            await cache.SetAsync(key, new CacheProbe(key), TimeSpan.FromSeconds(30), CancellationToken.None);
            if (await cache.GetAsync<CacheProbe>(key, CancellationToken.None) is not null) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record CacheProbe(string Value);
}
