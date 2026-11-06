using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Infrastructure.Hubs;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Nexo.UnitTests.Auth;

/// <summary>
/// The signal about a session change comes after the commit: whatever happens with Redis, it must not fail the person's request,
/// and subscribing has to be tried again. The sweep of each instance covers a message that was lost.
/// </summary>
public class HubSessionSignalsTests
{
    private sealed class RecordingLogger : ILogger<HubSessionSignals>
    {
        private readonly TaskCompletionSource<string> _warned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Warned => _warned.Task;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) _warned.TrySetResult(formatter(state, exception));
        }
    }

    private static readonly HubSessionChange AnyChange = new([Guid.NewGuid()], []);

    private static (HubSessionSignals Signals, IConnectionMultiplexer Redis, RecordingLogger Log) WithRedis()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var services = new ServiceCollection().AddSingleton(redis).BuildServiceProvider();
        var guard = new HubSessionGuard(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<HubSessionGuard>.Instance);
        var log = new RecordingLogger();
        return (new HubSessionSignals(guard, services, log), redis, log);
    }

    [Fact]
    public async Task A_publish_that_fails_does_not_fail_the_change_that_was_already_committed()
    {
        var (signals, redis, log) = WithRedis();
        redis.GetSubscriber().Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis is out"));

        var act = () => signals.PublishAsync(AnyChange, CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await log.Warned.WaitAsync(TimeSpan.FromSeconds(5))).Should().Contain("sweep");
    }

    [Fact]
    public async Task A_subscription_that_fails_is_tried_again_on_the_next_pass_and_only_until_it_works()
    {
        var (signals, redis, log) = WithRedis();
        var subscriber = Substitute.For<ISubscriber>();
        redis.GetSubscriber().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis is out"), _ => subscriber);

        await signals.StartListeningAsync(CancellationToken.None);
        (await log.Warned.WaitAsync(TimeSpan.FromSeconds(5))).Should().Contain("trying again");

        await signals.StartListeningAsync(CancellationToken.None);
        await signals.StartListeningAsync(CancellationToken.None);

        redis.Received(2).GetSubscriber();
        await subscriber.Received(1).SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>());
    }

    [Fact]
    public async Task A_message_from_another_instance_that_cannot_be_read_is_only_logged()
    {
        var (signals, redis, log) = WithRedis();
        var subscriber = Substitute.For<ISubscriber>();
        Action<RedisChannel, RedisValue>? handler = null;
        subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(h => handler = h)).Returns(Task.CompletedTask);
        redis.GetSubscriber().Returns(subscriber);
        await signals.StartListeningAsync(CancellationToken.None);

        var act = () => handler!(RedisChannel.Literal("any"), "not json at all");

        act.Should().NotThrow();
        (await log.Warned.WaitAsync(TimeSpan.FromSeconds(5))).Should().Contain("another instance");
    }
}
