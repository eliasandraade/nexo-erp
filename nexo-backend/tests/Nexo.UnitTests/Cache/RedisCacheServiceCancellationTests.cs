using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Cache;
using StackExchange.Redis;

namespace Nexo.UnitTests.Cache;

/// <summary>
/// O circuito do Redis vale para o processo inteiro: enquanto está aberto, o cache responde vazio para todos.
/// Um cliente que desiste do pedido não é falha do Redis e não pode abrir o circuito; só falha de verdade abre.
/// </summary>
public class RedisCacheServiceCancellationTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisCacheService _cache;

    public RedisCacheServiceCancellationTests()
    {
        // O estado do circuito é estático: cada teste começa com ele fechado.
        typeof(RedisCacheService)
            .GetField("s_circuitResetAtTicks", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, 0L);

        _redis.IsConnected.Returns(true);
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _cache = new RedisCacheService(_redis, NullLogger<RedisCacheService>.Instance);
    }

    /// <summary>Toda chamada ao Redis fica pendente, como um servidor lento.</summary>
    private void RedisNeverAnswers()
    {
        _database.ReturnsForAll(new TaskCompletionSource<RedisValue>().Task);
        _database.ReturnsForAll(new TaskCompletionSource<bool>().Task);
        _database.ReturnsForAll(new TaskCompletionSource<long>().Task);
    }

    private async Task<bool> NextCallReachesRedisAsync()
    {
        _redis.ClearReceivedCalls();
        _database.ReturnsForAll(Task.FromResult(RedisValue.Null));
        await _cache.GetAsync<object>("proxima-chamada");
        return _redis.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IConnectionMultiplexer.GetDatabase));
    }

    public static TheoryData<string, Func<ICacheService, CancellationToken, Task>> Operations() => new()
    {
        { "GET", (cache, ct) => cache.GetAsync<object>("k", ct) },
        { "SET", (cache, ct) => cache.SetAsync("k", new object(), TimeSpan.FromMinutes(1), ct) },
        { "DEL", (cache, ct) => cache.RemoveAsync("k", ct) },
        { "EXISTS", (cache, ct) => cache.ExistsAsync("k", ct) },
        { "INCR", (cache, ct) => cache.IncrementAsync("k", TimeSpan.FromMinutes(1), ct) },
        { "SETFLAG", (cache, ct) => cache.SetFlagAsync("k", TimeSpan.FromMinutes(1), ct) },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Cancelled_call_propagates_and_keeps_the_circuit_closed(
        string operation, Func<ICacheService, CancellationToken, Task> call)
    {
        RedisNeverAnswers();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await call(_cache, caller.Token).Invoking(t => t)
            .Should().ThrowAsync<OperationCanceledException>(operation);

        (await NextCallReachesRedisAsync()).Should().BeTrue("o cliente desistiu; o Redis não falhou");
    }

    [Fact]
    public async Task Caller_cancelling_during_the_call_propagates_and_keeps_the_circuit_closed()
    {
        RedisNeverAnswers();
        using var caller = new CancellationTokenSource();

        var pending = _cache.GetAsync<object>("k", caller.Token);
        await caller.CancelAsync();

        await pending.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        (await NextCallReachesRedisAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_with_a_live_token_still_opens_the_circuit()
    {
        RedisNeverAnswers();

        var result = await _cache.GetAsync<object>("k", CancellationToken.None);

        result.Should().BeNull("o cache falha aberto");
        (await NextCallReachesRedisAsync()).Should().BeFalse("o circuito abriu");
    }

    [Fact]
    public async Task Redis_error_with_a_live_token_still_opens_the_circuit()
    {
        _database.ReturnsForAll(Task.FromException<RedisValue>(new RedisTimeoutException("sem resposta", CommandStatus.Unknown)));

        var result = await _cache.GetAsync<object>("k", CancellationToken.None);

        result.Should().BeNull("o cache falha aberto");
        (await NextCallReachesRedisAsync()).Should().BeFalse("o circuito abriu");
    }
}
