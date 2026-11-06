using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
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
        _database.ReturnsForAll(new TaskCompletionSource<RedisResult>().Task);
    }

    private async Task<bool> NextCallReachesRedisAsync()
    {
        _redis.ClearReceivedCalls();
        _database.ReturnsForAll(Task.FromResult(RedisValue.Null));
        await _cache.GetAsync<object>("proxima-chamada");
        return _redis.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IConnectionMultiplexer.GetDatabase));
    }

    private Task CallAsync(string operation, CancellationToken ct) => operation switch
    {
        "GET" => _cache.GetAsync<object>("k", ct),
        "SET" => _cache.SetAsync("k", new object(), TimeSpan.FromMinutes(1), ct),
        "DEL" => _cache.RemoveAsync("k", ct),
        "EXISTS" => _cache.ExistsAsync("k", ct),
        "INCR" => _cache.IncrementAsync("k", TimeSpan.FromMinutes(1), ct),
        "SETFLAG" => _cache.SetFlagAsync("k", TimeSpan.FromMinutes(1), ct),
        "COUNT" => _cache.CountInWindowAsync("k", TimeSpan.FromMinutes(1), ct),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    [Theory]
    [InlineData("GET")]
    [InlineData("SET")]
    [InlineData("DEL")]
    [InlineData("EXISTS")]
    [InlineData("INCR")]
    [InlineData("SETFLAG")]
    [InlineData("COUNT")]
    public async Task Cancelled_call_propagates_and_keeps_the_circuit_closed(string operation)
    {
        RedisNeverAnswers();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await FluentActions.Awaiting(() => CallAsync(operation, caller.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        (await NextCallReachesRedisAsync()).Should().BeTrue("o cliente desistiu; o Redis não falhou");
    }

    [Fact]
    public async Task Caller_cancelling_during_the_call_propagates_and_keeps_the_circuit_closed()
    {
        RedisNeverAnswers();
        using var caller = new CancellationTokenSource();

        var pending = _cache.GetAsync<object>("k", caller.Token);
        await caller.CancelAsync();

        // On a loaded machine the 500 ms limit of the call can fire before the line above runs. The caller has left either way:
        // what this test is about is that the failure does not open the circuit.
        var thrown = await pending.Invoking(t => t).Should().ThrowAsync<Exception>();
        thrown.Which.Should().Match<Exception>(e => e is OperationCanceledException || e is TimeoutException);
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

    /// <summary>
    /// A count sits in front of the lookup of a refresh token. If its timeout opened the circuit, that lookup would answer "no
    /// such token" for the whole cooldown: one slow count would become a false verdict on the sessions of the process.
    /// </summary>
    [Fact]
    public async Task Timeout_of_a_count_returns_nothing_and_keeps_the_circuit_closed()
    {
        RedisNeverAnswers();

        var result = await _cache.CountInWindowAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None);

        result.Should().BeNull();
        _cache.IsUnavailable.Should().BeFalse();
        (await NextCallReachesRedisAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Error_of_a_count_returns_nothing_and_keeps_the_circuit_closed()
    {
        _database.ReturnsForAll(Task.FromException<RedisResult>(new RedisTimeoutException("sem resposta", CommandStatus.Unknown)));

        var result = await _cache.CountInWindowAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None);

        result.Should().BeNull();
        _cache.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task A_count_with_the_circuit_open_does_not_touch_redis()
    {
        _database.ReturnsForAll(Task.FromException<RedisValue>(new RedisTimeoutException("sem resposta", CommandStatus.Unknown)));
        await _cache.GetAsync<object>("k", CancellationToken.None);
        _cache.IsUnavailable.Should().BeTrue();
        _redis.ClearReceivedCalls();

        var result = await _cache.CountInWindowAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None);

        result.Should().BeNull();
        _redis.ReceivedCalls().Should().BeEmpty();
    }
}
