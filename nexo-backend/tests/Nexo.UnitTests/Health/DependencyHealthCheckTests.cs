using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Health;
using NSubstitute;
using Xunit;

namespace Nexo.UnitTests.Health;

public class DependencyHealthCheckTests
{
    private sealed class FlakyStorage : IStorageProvider
    {
        public int Pings;
        public bool Fail;
        public Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task PingAsync(CancellationToken ct = default)
        {
            Pings++;
            return Fail ? Task.FromException(new HttpRequestException("r2 down")) : Task.CompletedTask;
        }
    }

    private static IIntegrationFeatureFlags Flags(bool storage)
    {
        var flags = Substitute.For<IIntegrationFeatureFlags>();
        flags.StorageEnabled.Returns(storage);
        return flags;
    }

    [Fact]
    public async Task Redis_not_configured_is_healthy()
    {
        var check = new RedisHealthCheck(new ServiceCollection().BuildServiceProvider(), NullLogger<RedisHealthCheck>.Instance);
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Storage_outage_degrades_and_is_not_pinged_again_for_a_minute()
    {
        var storage = new FlakyStorage { Fail = true };
        var check = new ObjectStorageHealthCheck(storage, Flags(storage: true), NullLogger<ObjectStorageHealthCheck>.Instance);

        var first = await check.CheckHealthAsync(new HealthCheckContext());
        var second = await check.CheckHealthAsync(new HealthCheckContext());

        first.Status.Should().Be(HealthStatus.Degraded, "uploads fail, the rest of the API works");
        second.Status.Should().Be(HealthStatus.Degraded);
        storage.Pings.Should().Be(1, "each ping is a paid request");
    }

    [Fact]
    public async Task A_probe_that_gives_up_leaves_no_verdict_behind()
    {
        var storage = new FlakyStorage();
        var check = new ObjectStorageHealthCheck(storage, Flags(storage: true), NullLogger<ObjectStorageHealthCheck>.Instance);
        using var gaveUp = new CancellationTokenSource();
        await gaveUp.CancelAsync();

        await FluentActions.Awaiting(() => check.CheckHealthAsync(new HealthCheckContext(), gaveUp.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy, "nothing false was kept");
    }

    [Fact]
    public async Task Disabled_storage_is_not_pinged()
    {
        var storage = new FlakyStorage();
        var result = await new ObjectStorageHealthCheck(storage, Flags(storage: false), NullLogger<ObjectStorageHealthCheck>.Instance).CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
        storage.Pings.Should().Be(0);
    }
}
