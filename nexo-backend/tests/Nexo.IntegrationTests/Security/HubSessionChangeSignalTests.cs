using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Hubs;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// When a change to a person closes their realtime connection, and when it must not. Every test first makes the connection stale
/// without any signal (the stamp changes behind the API's back), so any signal at all would close it: a connection still open
/// proves that nothing was said, and a closed one proves that something was.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class HubSessionChangeSignalTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _sessions;
    private readonly HubKit _hub;

    public HubSessionChangeSignalTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _sessions = new RedisSessionKit(factory);
        _hub = new HubKit(factory);
    }

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(RealtimeCompany Company, SyntheticAccount Target, HubProbe Probe, HubProbe Witness)> StaleConnectionAsync()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var probe = await _hub.ListenAsAsync(company, target);
        var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.RotateStampSilentlyAsync(target.UserId);
        return (company, target, probe, witness);
    }

    private static async Task<Nexo.Domain.Entities.User> LoadAsync(NexoDbContext db, Guid userId)
        => await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);

    /// <summary>A second, unrelated save on the same context: it is what would flush anything left pending.</summary>
    private static void TouchSomethingElse(Nexo.Domain.Entities.User other, string note)
        => other.UpdateProfile(other.FullName, other.Email, other.Phone, other.Role, note);

    // ── Synchronous saves tell too ────────────────────────────────────────────

    [Fact]
    public async Task A_synchronous_save_closes_the_connection()
    {
        var (_, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await LoadAsync(db, target.UserId)).BumpSecurityStamp();
        db.SaveChanges();

        await probe.ClosedAsync();
        witness.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task A_synchronous_commit_closes_the_connection_and_only_the_commit_does()
    {
        var (company, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        using var tx = db.Database.BeginTransaction();
        (await LoadAsync(db, target.UserId)).BumpSecurityStamp();
        db.SaveChanges();

        await _hub.BothReceiveAsync(company, probe, witness);   // saved, not committed: still open and receiving

        tx.Commit();

        await probe.ClosedAsync();
    }

    [Fact]
    public async Task A_synchronous_rollback_says_nothing_even_to_a_later_save()
    {
        var (company, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await LoadAsync(db, target.UserId);
        using (var tx = db.Database.BeginTransaction())
        {
            user.BumpSecurityStamp();
            db.SaveChanges();
            tx.Rollback();
        }
        db.ChangeTracker.Clear();

        TouchSomethingElse(await LoadAsync(db, company.DirectorAccount.UserId), "after the rollback");
        db.SaveChanges();

        await _hub.BothReceiveAsync(company, probe, witness);
        probe.IsClosed.Should().BeFalse();
    }

    // ── A save that failed changed nothing, so it says nothing ────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_save_that_failed_says_nothing_even_to_a_later_save(bool synchronous)
    {
        var (company, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await LoadAsync(db, target.UserId);
        var taken = (await LoadAsync(db, company.DirectorAccount.UserId)).Login;
        user.BumpSecurityStamp();
        db.Entry(user).Property(u => u.Login).CurrentValue = taken;   // unique in the company: this save fails

        if (synchronous)
        {
            var failed = () => db.SaveChanges();
            failed.Should().Throw<DbUpdateException>();
        }
        else
        {
            var failed = () => db.SaveChangesAsync();
            await failed.Should().ThrowAsync<DbUpdateException>();
        }
        db.ChangeTracker.Clear();

        TouchSomethingElse(await LoadAsync(db, company.DirectorAccount.UserId), "after the failure");
        await db.SaveChangesAsync();

        await _hub.BothReceiveAsync(company, probe, witness);
        probe.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task A_save_canceled_by_the_caller_says_nothing_even_to_a_later_save()
    {
        var (company, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await LoadAsync(db, target.UserId)).BumpSecurityStamp();

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var act = () => db.SaveChangesAsync(canceled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        db.ChangeTracker.Clear();

        TouchSomethingElse(await LoadAsync(db, company.DirectorAccount.UserId), "after the cancellation");
        await db.SaveChangesAsync();

        await _hub.BothReceiveAsync(company, probe, witness);
        probe.IsClosed.Should().BeFalse();
    }

    // ── A signal when the database does not answer ────────────────────────────

    [Fact]
    public async Task A_database_failure_during_a_signal_does_not_fail_the_caller_and_closes_nothing()
    {
        var (company, target, probe, witness) = await StaleConnectionAsync();
        await using var _1 = probe;
        await using var _2 = witness;
        var signals = _factory.Services.GetRequiredService<HubSessionSignals>();

        using (_factory.Gate.FailWhile(command => command.CommandText.Contains(HubSessionGuard.ReadTag, StringComparison.Ordinal)))
        {
            var act = () => signals.PublishAsync(new HubSessionChange([target.UserId], []), CancellationToken.None);
            await act.Should().NotThrowAsync("the change it reports is already committed");
        }

        await _hub.BothReceiveAsync(company, probe, witness);
        probe.IsClosed.Should().BeFalse("a database that did not answer says nothing about anyone's session");
    }

    // ── The sweep and a platform access that ended ────────────────────────────

    [Fact]
    public async Task The_sweep_closes_a_platform_access_that_ended_when_the_signal_was_lost()
    {
        var company = await _hub.NewCompanyAsync();
        await using var person = await _hub.ListenAsync(company, company.DirectorToken);
        var platform = await _sessions.PlatformClientAsync();
        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{company.TenantId}/impersonate", new { reason = "Suporte: conferir a cozinha" });
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var token = (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        await using var access = await _hub.ListenAsync(company, token);
        await _hub.BothReceiveAsync(company, access, person);

        // What the end of the access leaves behind, with no message telling this instance.
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICacheService>()
                .SetFlagAsync(ImpersonationClaims.EndedKey(RedisSessionKit.JtiOf(token)), TimeSpan.FromMinutes(5));

        var closed = await HubSessionSweepJob.RunOnceAsync(_factory.Services.GetRequiredService<HubSessionGuard>(), NullLogger.Instance, CancellationToken.None);

        closed.Should().Be(1, "only the access ended; the person of the company goes on");
        await access.ClosedAsync();
        person.IsClosed.Should().BeFalse();
    }
}
