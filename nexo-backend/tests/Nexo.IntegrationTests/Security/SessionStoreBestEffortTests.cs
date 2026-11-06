using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// The list of sessions is a record, not the session itself: a failure writing to it must not fail a sign-in, a renewal or a
/// sign-out. Real PostgreSQL; the write fails through an interceptor, the way a database that went away fails it.
/// </summary>
[Collection("Integration")]
public class SessionStoreBestEffortTests
{
    private readonly TestWebApplicationFactory _factory;
    public SessionStoreBestEffortTests(TestWebApplicationFactory factory) => _factory = factory;

    private sealed class FailingSaves : SaveChangesInterceptor
    {
        public int Failed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Failed++;
            throw new DbUpdateException("the database went away");
        }
    }

    private (SessionStoreService Store, NexoDbContext Db, FailingSaves Saves) StoreOverABrokenDatabase(IServiceScope scope)
    {
        var saves = new FailingSaves();
        var options = new DbContextOptionsBuilder<NexoDbContext>(scope.ServiceProvider.GetRequiredService<DbContextOptions<NexoDbContext>>())
            .AddInterceptors(saves)
            .Options;
        var db = new NexoDbContext(options,
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>(),
            scope.ServiceProvider.GetRequiredService<ICurrentStore>());
        return (new SessionStoreService(db), db, saves);
    }

    private async Task<string> ExistingSessionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync();
        var jti = Guid.NewGuid().ToString("N");
        await new SessionStoreService(db).CreateAsync(user.Id, user.TenantId, jti, "10.0.0.1", "test", DateTime.UtcNow.AddDays(7));
        return jti;
    }

    private async Task<(bool Revoked, string Jti)?> RowAsync(string jti)
    {
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().UserSessions.IgnoreQueryFilters()
            .AsNoTracking().FirstOrDefaultAsync(s => s.RefreshJti == jti);
        return row is null ? null : (row.IsRevoked, row.RefreshJti);
    }

    [Fact]
    public async Task A_sign_in_goes_on_when_its_session_cannot_be_recorded()
    {
        using var scope = _factory.Services.CreateScope();
        var (store, _, saves) = StoreOverABrokenDatabase(scope);
        var jti = Guid.NewGuid().ToString("N");

        var act = () => store.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), jti, "10.0.0.1", "test", DateTime.UtcNow.AddDays(7));

        await act.Should().NotThrowAsync();
        saves.Failed.Should().Be(1, "the write has to have been tried, or this test proves nothing");
        (await RowAsync(jti)).Should().BeNull();
    }

    [Fact]
    public async Task A_sign_out_goes_on_when_its_session_cannot_be_marked()
    {
        var jti = await ExistingSessionAsync();
        using var scope = _factory.Services.CreateScope();
        var (store, _, saves) = StoreOverABrokenDatabase(scope);

        var act = () => store.RevokeAsync(jti);

        await act.Should().NotThrowAsync();
        saves.Failed.Should().Be(1);
        (await RowAsync(jti))!.Value.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task A_renewal_goes_on_when_its_session_cannot_be_updated()
    {
        var jti = await ExistingSessionAsync();
        var next = Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var (store, _, saves) = StoreOverABrokenDatabase(scope);

        var act = () => store.TouchAsync(jti, next);

        await act.Should().NotThrowAsync();
        saves.Failed.Should().Be(1);
        (await RowAsync(jti)).Should().NotBeNull("the row keeps the old token");
        (await RowAsync(next)).Should().BeNull();
    }

    [Fact]
    public async Task Marking_or_renewing_a_session_nobody_recorded_does_nothing()
    {
        using var scope = _factory.Services.CreateScope();
        var (store, _, saves) = StoreOverABrokenDatabase(scope);

        await store.RevokeAsync("never-recorded");
        await store.TouchAsync("never-recorded", "next");

        saves.Failed.Should().Be(0);
    }
}
