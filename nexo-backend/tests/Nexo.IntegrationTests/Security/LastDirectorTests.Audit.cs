using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Audit;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// A refused change rolls back, and the record of the attempt has to outlive it. It is written apart from the request: the answer
/// and the company's lock never depend on it, so these tests wait for the entry instead of expecting it with the response.
/// </summary>
public sealed partial class LastDirectorTests
{
    private const string RefusalAction = "user_last_director_refused";
    private static readonly TimeSpan EntryLimit = TimeSpan.FromSeconds(10);

    // ────────────────────────────────────────────────────────────────────────────
    // THE ENTRY
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("vendedor", "inactive")]
    public async Task A_refusal_through_the_api_survives_the_rollback_and_says_who_what_and_where(string role, string status)
    {
        var director = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var before = await SnapshotOfAsync(director.UserId);

        using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, role, status));

        await ShouldBeRefusedAsync(refused);
        var entry = (await WaitForRefusalsAsync(director.TenantId, 1)).Single();
        entry.Severity.Should().Be(AuditSeverity.Warning);
        entry.EntityType.Should().Be("User");
        entry.EntityId.Should().Be(director.UserId.ToString());
        entry.TenantId.Should().Be(director.TenantId);
        entry.ActorId.Should().Be(director.UserId);
        entry.ActorType.Should().Be("user");
        entry.Description.Should().Contain(role).And.Contain(status)
            .And.Contain(director.UserId.ToString()).And.Contain(director.TenantId.ToString());

        var target = TargetsOf(entry).Single();
        SourceOf(entry).Should().Be("user_update");
        target.GetProperty("userId").GetGuid().Should().Be(director.UserId);
        target.GetProperty("role").GetString().Should().Be(role);
        target.GetProperty("status").GetString().Should().Be(status);
        target.GetProperty("deleted").GetBoolean().Should().BeFalse();

        (await SnapshotOfAsync(director.UserId)).Should().Be(before, "the record of the attempt does not bring the attempt with it");
    }

    /// <summary>
    /// The actor is not the target when the acting token was already out as the company's last director came to be someone else
    /// (the status is cached and the row changed without bumping the stamp, the way a manual fix in the database would).
    /// </summary>
    [Fact]
    public async Task The_entry_tells_the_actor_from_the_target_and_names_no_person()
    {
        var acting = await NewCompanyAsync();
        var target = await AddUserAsync(acting, UserRole.Diretoria);
        var client = await AuthClientFactory.LoginAsync(_factory, acting.Login, acting.Password);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters()
                .Where(u => u.Id == acting.UserId)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, UserStatus.Blocked));
        }
        const string attemptedName = "Nome Tentado Unico";
        var attemptedEmail = $"tentado-{Guid.NewGuid():N}"[..20] + $"@{TestCredentials.TestDomain}";

        using var refused = await client.PutAsJsonAsync(RouteOf(target), new
        {
            fullName = attemptedName, email = attemptedEmail, role = "gerente", phone = "11988887777", notes = "Nota tentada", status = "active",
        });

        await ShouldBeRefusedAsync(refused);
        var entry = (await WaitForRefusalsAsync(acting.TenantId, 1)).Single();
        entry.ActorId.Should().Be(acting.UserId);
        entry.EntityId.Should().Be(target.UserId.ToString());
        entry.ActorName.Should().BeNull("a name is personal data, and the actor is already identified by the id");
        var text = $"{entry.Description} {entry.MetadataJson} {entry.ActorName}";
        foreach (var personal in new[]
                 {
                     acting.FullName, acting.Login, acting.Email, target.Login, target.Email,
                     attemptedName, attemptedEmail, "11988887777", "Nota tentada",
                 })
            text.Should().NotContain(personal);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // THE CONTEXT: A PATH THAT NEVER HEARD OF THE RULE
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Removal.Demotion, "gerente", "active", false)]
    [InlineData(Removal.Block, "diretoria", "blocked", false)]
    [InlineData(Removal.Inactivation, "diretoria", "inactive", false)]
    [InlineData(Removal.Deletion, "diretoria", "active", true)]
    public async Task A_refusal_by_the_context_is_recorded_with_no_request_around(Removal how, string role, string status, bool deleted)
    {
        var director = await NewCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == director.UserId), how);

        var save = () => db.SaveChangesAsync();

        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);
        var entry = (await WaitForRefusalsAsync(director.TenantId, 1)).Single();
        entry.ActorId.Should().BeNull();
        entry.ActorType.Should().Be("system");
        entry.EntityId.Should().Be(director.UserId.ToString());
        SourceOf(entry).Should().Be("data_context");
        var target = TargetsOf(entry).Single();
        target.GetProperty("role").GetString().Should().Be(role);
        target.GetProperty("status").GetString().Should().Be(status);
        target.GetProperty("deleted").GetBoolean().Should().Be(deleted);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    /// <summary>
    /// The refusal does not end a transaction it did not open: the lock stays with the caller until it rolls back. The entry does
    /// not wait for that, on a connection of its own, and it stays after the rollback.
    /// </summary>
    [Fact]
    public async Task A_refusal_inside_a_transaction_the_caller_opened_is_there_before_and_after_its_rollback()
    {
        var director = await NewCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == director.UserId), Removal.Demotion);

        var save = () => db.SaveChangesAsync();
        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);

        (await WaitForRefusalsAsync(director.TenantId, 1)).Should().ContainSingle("the caller's transaction is still open");
        await tx.RollbackAsync();

        (await RefusalsAsync(director.TenantId)).Should().ContainSingle();
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
        (await SnapshotOfAsync(director.UserId)).Role.Should().Be(UserRole.Diretoria);
    }

    [Fact]
    public async Task A_refused_save_of_two_directors_is_one_entry_that_names_both()
    {
        var first = await NewCompanyAsync();
        var second = await AddUserAsync(first, UserRole.Diretoria);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == first.UserId), Removal.Demotion);
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == second.UserId), Removal.Block);

        var save = () => db.SaveChangesAsync();
        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);

        await WaitForTheWriterToCatchUpAsync();
        var entry = (await RefusalsAsync(first.TenantId)).Single();
        TargetsOf(entry).Select(t => t.GetProperty("userId").GetGuid()).Should().BeEquivalentTo([first.UserId, second.UserId]);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CONCURRENCY
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>The race of <see cref="A_request_that_waited_for_the_other_decides_on_what_the_other_committed"/>, deterministically: who waited and lost is on record.</summary>
    [Fact]
    public async Task A_request_refused_after_waiting_for_the_lock_is_recorded_once_and_the_winner_is_not()
    {
        var directors = await NewDirectorsAsync(2);
        var (first, firstClient) = directors[0];
        var (second, secondClient) = directors[1];

        using var hold = _factory.Gate.HoldNext(DbCommandGate.UserRowOf(second.UserId), DbCommandGate.Moment.AfterRunning);
        var firstRequest = firstClient.PutAsJsonAsync(RouteOf(second), Body(second, "gerente", "active"));
        await hold.ReachedAsync();

        var secondRequest = secondClient.PutAsJsonAsync(RouteOf(first), Body(first, "gerente", "active"));
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!secondRequest.IsCompleted && !await SomeoneWaitsForTheLockAsync(first.TenantId))
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("The second request neither finished nor waited for the lock.");
            await Task.Delay(20);
        }

        hold.Release();
        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(firstResponse));
        await ShouldBeRefusedAsync(secondResponse);
        await WaitForTheWriterToCatchUpAsync();
        var entry = (await RefusalsAsync(first.TenantId)).Single();
        entry.ActorId.Should().Be(second.UserId, "who lost the race tried");
        entry.EntityId.Should().Be(first.UserId.ToString());
    }

    [Fact]
    public async Task Every_refusal_of_a_real_race_is_recorded_exactly_once()
    {
        const int directorsInTheCompany = 4;
        const int rounds = 8;
        for (var round = 0; round < rounds; round++)
        {
            var directors = await NewDirectorsAsync(directorsInTheCompany);

            var responses = await RaceAsync(directors
                .Select<(SyntheticAccount Account, HttpClient Client), Func<Task<HttpResponseMessage>>>(d =>
                    () => d.Client.PutAsJsonAsync(RouteOf(d.Account), Body(d.Account, "gerente", "active")))
                .ToArray());

            var loser = directors[Array.FindIndex(responses, r => r.StatusCode != HttpStatusCode.OK)].Account;
            await WaitForTheWriterToCatchUpAsync();
            var entry = (await RefusalsAsync(loser.TenantId)).Should().ContainSingle($"round {round}: {await DescribeAsync(responses)}").Subject;
            entry.ActorId.Should().Be(loser.UserId);
            entry.EntityId.Should().Be(loser.UserId.ToString());
            foreach (var response in responses) response.Dispose();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHEN THE AUDIT FAILS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task When_the_entry_cannot_be_written_the_answer_is_the_same_and_the_lock_is_free()
    {
        var director = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        using (var fault = _factory.Gate.FailWhile(DbCommandGate.AuditInsertOf(RefusalAction)))
        {
            using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active"));
            await ShouldBeRefusedAsync(refused);
            await fault.FailedAtLeastAsync(1);

            await client.PutAsJsonAsync(RouteOf(director), Body(director, "diretoria", "active", fullName: "Nome Depois da Falha"))
                .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
            (await SomeoneWaitsForTheLockAsync(director.TenantId)).Should().BeFalse();
            (await SnapshotOfAsync(director.UserId)).FullName.Should().Be("Nome Depois da Falha");
        }

        (await RefusalsAsync(director.TenantId)).Should().BeEmpty();

        // one failure does not stop the writer: the next refusal is recorded
        using (var again = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")))
            await ShouldBeRefusedAsync(again);
        (await WaitForRefusalsAsync(director.TenantId, 1)).Should().ContainSingle();
    }

    /// <summary>An audit that hangs is worse than one that fails: nothing about the request can be waiting for it.</summary>
    [Fact]
    public async Task While_the_entry_is_stuck_the_answer_and_the_lock_do_not_wait_for_it()
    {
        var director = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        using var hold = _factory.Gate.HoldNext(DbCommandGate.AuditInsertOf(RefusalAction), DbCommandGate.Moment.BeforeRunning);

        using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")).WaitAsync(EntryLimit);
        await ShouldBeRefusedAsync(refused);
        await hold.ReachedAsync();

        // the insert is on hold right now, and the same company still goes through the lock
        await client.PutAsJsonAsync(RouteOf(director), Body(director, "diretoria", "active", fullName: "Nome Com Auditoria Parada")).WaitAsync(EntryLimit)
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await SnapshotOfAsync(director.UserId)).FullName.Should().Be("Nome Com Auditoria Parada");
        (await RefusalsAsync(director.TenantId)).Should().BeEmpty("the entry is the one on hold");

        hold.Release();
        (await WaitForRefusalsAsync(director.TenantId, 1)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_writer_that_throws_does_not_change_the_answer()
    {
        var attempts = new RefusalThrowsAuditWriter.Attempts();
        await using var host = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton(attempts);
            s.AddScoped<AuditWriterService>();
            s.RemoveAll<IAuditWriter>();
            s.AddScoped<IAuditWriter, RefusalThrowsAuditWriter>();
        }));
        var director = await NewCompanyAsync();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var login = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(director.Login, director.Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

        using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active"));

        await ShouldBeRefusedAsync(refused);
        attempts.Count.Should().Be(1, "the writer was asked, and threw");
        (await RefusalsAsync(director.TenantId)).Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHO READS IT
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_entry_reaches_the_platform_log_and_the_audit_screen_of_the_company_only()
    {
        var director = await NewCompanyAsync();
        var stranger = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var strangerClient = await AuthClientFactory.LoginAsync(_factory, stranger.Login, stranger.Password);
        using (var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")))
            await ShouldBeRefusedAsync(refused);
        await WaitForRefusalsAsync(director.TenantId, 1);

        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);
        var log = await platform.GetFromJsonAsync<JsonElement>($"/api/platform/audit?tenantId={director.TenantId}&actionType={RefusalAction}");
        log.GetProperty("total").GetInt32().Should().Be(1);
        var record = log.GetProperty("records").EnumerateArray().Single();
        record.GetProperty("severity").GetString().Should().Be("warning");
        record.GetProperty("actorId").GetGuid().Should().Be(director.UserId);
        record.GetProperty("entityId").GetString().Should().Be(director.UserId.ToString());
        record.GetProperty("tenantId").GetGuid().Should().Be(director.TenantId);
        record.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/audit?actionType={RefusalAction}");
        mine.EnumerateArray().Should().ContainSingle().Which.GetProperty("entityId").GetString().Should().Be(director.UserId.ToString());
        (await strangerClient.GetFromJsonAsync<JsonElement>($"/api/audit?actionType={RefusalAction}")).EnumerateArray().Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    private Task<List<AuditRecord>> RefusalsAsync(Guid tenantId)
        => ReadAsync(db => db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ActionType == RefusalAction).ToListAsync());

    private async Task<List<AuditRecord>> WaitForRefusalsAsync(Guid tenantId, int count)
    {
        var limit = DateTime.UtcNow.Add(EntryLimit);
        while (true)
        {
            var found = await RefusalsAsync(tenantId);
            if (found.Count >= count) return found;
            if (DateTime.UtcNow > limit) found.Count.Should().BeGreaterThanOrEqualTo(count, "the refusal has to reach the audit");
            await Task.Delay(20);
        }
    }

    /// <summary>Entries are written in order, one at a time: once a later one is there, every earlier one was written.</summary>
    private async Task WaitForTheWriterToCatchUpAsync()
    {
        var marker = await NewCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == marker.UserId), Removal.Demotion);
        var save = () => db.SaveChangesAsync();
        await save.Should().ThrowAsync<ConflictException>();
        await WaitForRefusalsAsync(marker.TenantId, 1);
    }

    private static string? SourceOf(AuditRecord entry)
        => JsonDocument.Parse(entry.MetadataJson!).RootElement.GetProperty("source").GetString();

    private static List<JsonElement> TargetsOf(AuditRecord entry)
        => JsonDocument.Parse(entry.MetadataJson!).RootElement.GetProperty("targets").EnumerateArray().Select(t => t.Clone()).ToList();

    /// <summary>The real writer, except that it throws for the entry under test. Everything else the request audits goes through.</summary>
    private sealed class RefusalThrowsAuditWriter : IAuditWriter
    {
        public sealed class Attempts
        {
            public int Count;
        }

        private readonly AuditWriterService _inner;
        private readonly Attempts _attempts;

        public RefusalThrowsAuditWriter(AuditWriterService inner, Attempts attempts)
        {
            _inner = inner;
            _attempts = attempts;
        }

        public void Stage(string actionType, string severity, string entityType, string entityId, string description, Guid? tenantId = null,
            Guid? actorId = null, string? actorName = null, string actorType = "user", object? metadata = null)
        {
            if (actionType == RefusalAction)
            {
                Interlocked.Increment(ref _attempts.Count);
                throw new InvalidOperationException("The test made this audit entry fail.");
            }
            _inner.Stage(actionType, severity, entityType, entityId, description, tenantId, actorId, actorName, actorType, metadata);
        }
    }
}
