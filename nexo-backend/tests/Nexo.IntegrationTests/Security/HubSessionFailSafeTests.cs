using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Hubs;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Serilog.Core;
using Serilog.Events;
using static Nexo.IntegrationTests.Security.RedisSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// What keeps the guarantee when the immediate signal does not arrive: the periodic re-check, the check on each hub call, more
/// than one instance of the API and the failures of Redis and of the database. A second host in the same process is a second
/// instance: same database and same Redis, its own connections and its own multiplexer.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class HubSessionFailSafeTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _sessions;
    private readonly HubKit _hub;

    public HubSessionFailSafeTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _sessions = new RedisSessionKit(factory);
        _hub = new HubKit(factory);
    }

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HubSessionGuard Guard => _factory.Services.GetRequiredService<HubSessionGuard>();

    private Task<int> SweepAsync(WebApplicationFactory<Program>? host = null)
    {
        var guard = (host?.Services ?? _factory.Services).GetRequiredService<HubSessionGuard>();
        return HubSessionSweepJob.RunOnceAsync(guard, NullLogger.Instance, CancellationToken.None);
    }

    private async Task WaitUntilNoConnectionIsLeftAsync(HubSessionGuard guard)
    {
        using var limit = new CancellationTokenSource(HubProbe.Limit);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (guard.ConnectionCount != 0) await timer.WaitForNextTickAsync(limit.Token);
    }

    private static bool IsTheSweepRead(System.Data.Common.DbCommand command)
        => command.CommandText.Contains(HubSessionGuard.ReadTag, StringComparison.Ordinal);

    // ────────────────────────────────────────────────────────────────────────────
    // THE PERIODIC RE-CHECK
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HubTransport.WebSockets)]
    [InlineData(HubTransport.LongPolling)]
    public async Task The_sweep_closes_a_stale_connection_when_no_signal_was_sent_and_leaves_the_others(HubTransport transport)
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var probe = await _hub.ListenAsAsync(company, target, transport);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.RotateStampSilentlyAsync(target.UserId);

        // no signal went out, so this is the window the sweep exists to bound
        await _hub.BothReceiveAsync(company, probe, witness);

        (await SweepAsync()).Should().Be(1);

        await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        witness.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task The_sweep_makes_one_read_for_any_number_of_connections()
    {
        var company = await _hub.NewCompanyAsync();
        var people = new List<SyntheticAccount>();
        for (var i = 0; i < 3; i++) people.Add(await _hub.AddPersonAsync(company, UserRole.Vendedor));
        var probes = new List<HubProbe>();
        foreach (var person in people)
        {
            var token = (await _hub.SignInAsync(person)).AccessToken;
            probes.Add(await _hub.ListenAsync(company, token));
            probes.Add(await _hub.ListenAsync(company, token));
        }

        try
        {
            var reads = _factory.Gate.Count(IsTheSweepRead);

            (await SweepAsync()).Should().Be(0, "nobody changed");

            reads().Should().Be(1, "the stamps of every connection come in one batch, not one read per connection");
            await _hub.BothReceiveAsync(company, probes.ToArray());
        }
        finally
        {
            foreach (var probe in probes) await probe.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_sweep_with_no_connection_does_not_touch_the_database()
    {
        await WaitUntilNoConnectionIsLeftAsync(Guard);
        var reads = _factory.Gate.Count(IsTheSweepRead);

        (await SweepAsync()).Should().Be(0);

        reads().Should().Be(0);
    }

    [Fact]
    public async Task A_database_failure_in_the_sweep_closes_nothing_and_the_next_pass_works()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var probe = await _hub.ListenAsAsync(company, target);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.RotateStampSilentlyAsync(target.UserId);

        using (_factory.Gate.FailWhile(IsTheSweepRead))
        {
            var failing = () => SweepAsync();
            await failing.Should().ThrowAsync<Exception>();
        }

        witness.IsClosed.Should().BeFalse("a database that did not answer says nothing about anyone's session");
        await _hub.BothReceiveAsync(company, probe, witness);

        (await SweepAsync()).Should().Be(1);
        await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
    }

    [Fact]
    public async Task The_job_goes_on_after_a_database_failure_and_closes_the_stale_connection_by_itself()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var probe = await _hub.ListenAsAsync(company, target);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.RotateStampSilentlyAsync(target.UserId);

        var fault = _factory.Gate.FailWhile(IsTheSweepRead);
        using var job = new HubSessionSweepJob(Guard, _factory.Services.GetRequiredService<HubSessionSignals>(),
            NullLogger<HubSessionSweepJob>.Instance, TimeSpan.FromMilliseconds(100));
        await job.StartAsync(CancellationToken.None);
        try
        {
            await fault.FailedAtLeastAsync(2);
            job.ExecuteTask!.IsCompleted.Should().BeFalse("a failed pass does not end the job");
            witness.IsClosed.Should().BeFalse();
            probe.IsClosed.Should().BeFalse();

            fault.Dispose();

            await probe.ClosedAsync();
            witness.IsClosed.Should().BeFalse();
        }
        finally
        {
            fault.Dispose();
            await job.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void The_stated_bound_holds_the_interval_plus_the_limit_of_one_pass_stay_under_a_minute()
    {
        (HubSessionSweepJob.Interval + HubSessionSweepJob.PassLimit).Should().BeLessThan(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task The_sweep_closes_the_connections_of_a_company_suspended_without_a_signal()
    {
        var company = await _hub.NewCompanyAsync();
        var other = await _hub.NewCompanyAsync();
        await using var probe = await _hub.ListenAsync(company, company.DirectorToken);
        await using var otherProbe = await _hub.ListenAsync(other, other.DirectorToken);
        await _hub.SetCompanyStatusSilentlyAsync(company.TenantId, TenantStatus.Suspended);

        (await SweepAsync()).Should().Be(1);

        await probe.ClosedAsync();
        var otherEvent = await _hub.EmitEventAsync(other);
        await otherProbe.SawAsync(otherEvent);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // THE CHECK ON EACH CALL
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>A connection in no group whose opening check is over: a first call answers only after it. What comes next is the next read.</summary>
    private async Task<HubProbe> ConnectedAndCheckedAsync(RealtimeCompany company, SyntheticAccount person)
    {
        var probe = await HubProbe.ConnectAsync(_hub.Server, (await _hub.SignInAsync(person)).AccessToken);
        await probe.LeaveAsync(company.StoreId);
        return probe;
    }

    [Fact]
    public async Task JoinStore_after_the_change_is_refused_and_the_connection_is_aborted()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await using var probe = await ConnectedAndCheckedAsync(company, target);
        await _hub.RotateStampSilentlyAsync(target.UserId);

        var joining = () => probe.JoinAsync(company.StoreId);

        await joining.Should().ThrowAsync<Exception>("the claims of that token are no longer the truth");
        await probe.ClosedAsync();
        var after = await _hub.EmitEventAsync(company);
        await witness.SawAsync(after);
        probe.Saw(after).Should().BeFalse();
    }

    [Fact]
    public async Task LeaveStore_after_the_change_is_refused_and_the_connection_is_aborted()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var probe = await _hub.ListenAsAsync(company, target);
        await _hub.RotateStampSilentlyAsync(target.UserId);

        var leaving = () => probe.LeaveAsync(company.StoreId);

        await leaving.Should().ThrowAsync<Exception>();
        await probe.ClosedAsync();
    }

    [Fact]
    public async Task A_join_that_read_the_old_stamp_just_before_the_change_does_not_stay_in_the_group()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await using var probe = await ConnectedAndCheckedAsync(company, target);

        // the first read of the call is held with the old stamp already in hand, and only then the stamp changes
        using var hold = _factory.Gate.HoldNext(IsTheSweepRead, DbCommandGate.Moment.AfterRunning);
        var joining = probe.JoinAsync(company.StoreId);
        await hold.ReachedAsync();
        await _hub.RotateStampSilentlyAsync(target.UserId);
        hold.Release();

        var finishing = () => joining;
        await finishing.Should().ThrowAsync<Exception>();
        await probe.ClosedAsync();
        var after = await _hub.EmitEventAsync(company);
        await witness.SawAsync(after);
        probe.Saw(after).Should().BeFalse();
    }

    [Fact]
    public async Task A_join_during_a_database_failure_is_refused_without_closing_the_connection()
    {
        var company = await _hub.NewCompanyAsync();
        await using var probe = await HubProbe.ConnectAsync(_hub.Server, company.DirectorToken);

        using (_factory.Gate.FailWhile(IsTheSweepRead))
        {
            var joining = () => probe.JoinAsync(company.StoreId);
            await joining.Should().ThrowAsync<HubException>("nothing joins a group unchecked");
        }

        probe.IsClosed.Should().BeFalse("a healthy session stays connected");
        await probe.JoinAsync(company.StoreId);
        await _hub.BothReceiveAsync(company, probe);
    }

    [Fact]
    public async Task A_connection_that_opens_while_the_stamp_changes_is_checked_after_it_registers()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;

        // the HTTP checks of the socket request already passed; the change lands before the hub registers the connection's check
        using var hold = _factory.Gate.HoldNext(IsTheSweepRead, DbCommandGate.Moment.BeforeRunning);
        var connecting = HubProbe.ConnectAsync(_hub.Server, token);
        await hold.ReachedAsync();
        await _hub.RotateStampSilentlyAsync(target.UserId);
        hold.Release();

        await using var probe = await connecting;
        await probe.ClosedAsync();
    }

    [Fact]
    public async Task A_change_during_the_opening_check_still_closes_the_connection()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;

        // the check of the connection already read the old stamp when the change commits: the signal has to find this connection
        using var hold = _factory.Gate.HoldNext(IsTheSweepRead, DbCommandGate.Moment.AfterRunning);
        var connecting = HubProbe.ConnectAsync(_hub.Server, token);
        await hold.ReachedAsync();
        await company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "gerente"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        hold.Release();

        await using var probe = await connecting;
        await probe.ClosedAsync();
    }

    [Fact]
    public async Task A_connection_that_drops_during_its_opening_check_leaves_nothing_behind()
    {
        var company = await _hub.NewCompanyAsync();
        await WaitUntilNoConnectionIsLeftAsync(Guard);

        using var hold = _factory.Gate.HoldNext(IsTheSweepRead, DbCommandGate.Moment.BeforeRunning);
        var probe = await HubProbe.ConnectAsync(_hub.Server, company.DirectorToken);
        await hold.ReachedAsync();
        await probe.DisposeAsync();
        hold.Release();

        await WaitUntilNoConnectionIsLeftAsync(Guard);
    }

    [Fact]
    public async Task A_sweep_holding_an_old_read_does_not_close_a_connection_opened_after_it()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var old = await _hub.ListenAsAsync(company, target);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);

        // the pass read the old stamp and stopped there; the person changes role and comes back with a new token
        using var hold = _factory.Gate.HoldNext(IsTheSweepRead, DbCommandGate.Moment.AfterRunning);
        var pass = SweepAsync();
        await hold.ReachedAsync();
        await company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "gerente"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await old.ClosedAsync();
        await using var renewed = await _hub.ListenAsAsync(company, target);
        hold.Release();
        await pass;

        renewed.IsClosed.Should().BeFalse("the row in that pass is older than this token");
        (await SweepAsync()).Should().Be(0);
        await _hub.BothReceiveAsync(company, renewed, witness);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // MORE THAN ONE INSTANCE
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>A second instance of the API. Its job's subscription is opened by hand: hosted services do not run in Testing.</summary>
    private async Task<WebApplicationFactory<Program>> SecondInstanceAsync()
    {
        var instance = _factory.WithWebHostBuilder(_ => { });
        await instance.Services.GetRequiredService<HubSessionSignals>().StartListeningAsync(CancellationToken.None);
        return instance;
    }

    [Fact]
    public async Task A_change_on_one_instance_closes_the_connection_on_the_other_through_redis()
    {
        var instance = await SecondInstanceAsync();
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var bystander = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var remote = await _hub.ListenAsAsync(company, target, server: instance.Server);
        await using var remoteBystander = await _hub.ListenAsAsync(company, bystander, server: instance.Server);

        // the change goes through this instance; the connections are only on the other one
        await company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "gerente"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await remote.ClosedAsync();
        remoteBystander.IsClosed.Should().BeFalse();
        instance.Services.GetRequiredService<HubSessionGuard>().ConnectionCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_suspension_on_one_instance_closes_the_connections_on_the_other_through_redis()
    {
        var instance = await SecondInstanceAsync();
        var company = await _hub.NewCompanyAsync();
        await using var remote = await _hub.ListenAsync(company, company.DirectorToken, server: instance.Server);
        var platform = await _sessions.PlatformClientAsync();

        await platform.PutAsJsonAsync($"/api/platform/tenants/{company.TenantId}/status", new { status = "Suspended" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        await remote.ClosedAsync();
    }

    [Fact]
    public async Task A_signal_that_never_reaches_the_other_instance_is_covered_by_its_sweep()
    {
        var instance = _factory.WithWebHostBuilder(_ => { });   // not subscribed: every message is lost for it
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var remote = await _hub.ListenAsAsync(company, target, server: instance.Server);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, witness);

        await company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "gerente"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        remote.IsClosed.Should().BeFalse();

        (await SweepAsync(instance)).Should().Be(1);

        await remote.ClosedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_redis_down_the_change_goes_through_this_instance_closes_at_once_and_the_other_by_its_sweep(bool frozen)
    {
        var instance = await SecondInstanceAsync();
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;
        await using var local = await _hub.ListenAsync(company, token);
        await using var remote = await _hub.ListenAsync(company, token, server: instance.Server);

        if (frozen) await _factory.FreezeRedisAsync(); else await _factory.StopRedisAsync();
        try
        {
            await company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "gerente"))
                .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory, "the change does not depend on the signal");

            await local.ClosedAsync();
            remote.IsClosed.Should().BeFalse("the message never left");

            (await SweepAsync(instance)).Should().Be(1);
            await remote.ClosedAsync();
        }
        finally
        {
            if (frozen) await _factory.UnfreezeRedisAsync(); else await _factory.StartRedisAsync();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // THE TOKEN IN THE LOGS
    // ────────────────────────────────────────────────────────────────────────────

    private sealed class EveryLogLine : ILogEventSink
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public void Emit(LogEvent logEvent)
            => Lines.Enqueue($"{logEvent.RenderMessage()} {string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}"))} {logEvent.Exception}");
    }

    [Theory]
    [InlineData("Warning")]
    [InlineData("Verbose")]
    public async Task No_log_line_carries_the_token_of_the_query_string_at_any_level(string frameworkLevel)
    {
        var lines = new EveryLogLine();
        var instance = _factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("Serilog:MinimumLevel:Default", "Verbose");
            host.UseSetting("Serilog:MinimumLevel:Override:Microsoft", frameworkLevel);
            host.UseSetting("Serilog:MinimumLevel:Override:System", frameworkLevel);
            host.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(lines));
        });
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;

        await using (await _hub.ListenAsync(company, token, server: instance.Server))
        {
            // connected through the query string and joined to a store: the requests that could carry the token were made
        }
        var refused = () => HubProbe.ConnectAsync(instance.Server, token + "invalid", skipNegotiation: true);
        await refused.Should().ThrowAsync<Exception>();

        var signature = token.Split('.')[2];
        lines.Lines.Should().NotBeEmpty();
        lines.Lines.Should().NotContain(line => line.Contains(token, StringComparison.Ordinal), "the token never goes to a log");
        lines.Lines.Should().NotContain(line => line.Contains(signature, StringComparison.Ordinal), "nor its signature");
        lines.Lines.Should().NotContain(line => line.Contains("access_token=ey", StringComparison.Ordinal), "nor any query string with a token");
        lines.Lines.Should().NotContain(line => line.Contains("Authorization", StringComparison.OrdinalIgnoreCase) && line.Contains("Bearer ey", StringComparison.Ordinal));
    }
}
