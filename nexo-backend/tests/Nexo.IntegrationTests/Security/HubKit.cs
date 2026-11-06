using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Features.Auth;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

public enum HubTransport
{
    /// <summary>What a browser uses: the token goes in the query string of the upgrade request.</summary>
    WebSockets,
    /// <summary>The fallback the frontend allows: the token goes in the Authorization header of each poll.</summary>
    LongPolling,
}

/// <summary>
/// A real SignalR client connected to the real hub of a test host. Every wait is on a signal, with a limit: no test sleeps.
/// </summary>
internal sealed class HubProbe : IAsyncDisposable
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly HubConnection _connection;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _reconnecting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _events = new();

    private HubProbe(HubConnection connection)
    {
        _connection = connection;
        _connection.Closed += _ =>
        {
            _closed.TrySetResult();
            return Task.CompletedTask;
        };
        _connection.Reconnecting += _ =>
        {
            _reconnecting.TrySetResult();
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            _reconnected.TrySetResult();
            return Task.CompletedTask;
        };
        _connection.On<string, string>("TableStatusChanged", (tableId, _) => Signal(tableId).TrySetResult());
    }

    private TaskCompletionSource Signal(string tableId)
        => _events.GetOrAdd(tableId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public static async Task<HubProbe> ConnectAsync(TestServer server, string token, HubTransport transport = HubTransport.WebSockets,
        Func<Task<string?>>? renewedToken = null, bool skipNegotiation = false)
    {
        var builder = new HubConnectionBuilder().WithUrl(new Uri(server.BaseAddress, "/hubs/restaurant"), options =>
        {
            options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            options.AccessTokenProvider = renewedToken ?? (() => Task.FromResult<string?>(token));
            options.SkipNegotiation = skipNegotiation;
            if (transport == HubTransport.LongPolling)
            {
                options.Transports = HttpTransportType.LongPolling;
                return;
            }
            options.Transports = HttpTransportType.WebSockets;
            options.WebSocketFactory = async (context, ct) =>
            {
                // Same as a browser: a WebSocket request cannot carry headers, so the token goes in the query string.
                var current = renewedToken is null ? token : await renewedToken();
                var query = string.IsNullOrEmpty(context.Uri.Query) ? "?" : context.Uri.Query + "&";
                var uri = new Uri(context.Uri.GetLeftPart(UriPartial.Path) + query + "access_token=" + current);
                return await server.CreateWebSocketClient().ConnectAsync(uri, ct);
            };
        });
        if (renewedToken is not null) builder.WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)]);

        var probe = new HubProbe(builder.Build());
        await probe._connection.StartAsync().WaitAsync(Limit);
        return probe;
    }

    public Task JoinAsync(Guid storeId) => _connection.InvokeAsync("JoinStore", storeId.ToString()).WaitAsync(Limit);

    public Task LeaveAsync(Guid storeId) => _connection.InvokeAsync("LeaveStore", storeId.ToString()).WaitAsync(Limit);

    public bool Saw(Guid tableId) => _events.TryGetValue(tableId.ToString(), out var seen) && seen.Task.IsCompleted;

    public Task SawAsync(Guid tableId) => Signal(tableId.ToString()).Task.WaitAsync(Limit);

    /// <summary>The event arrived or the connection closed, whichever came first.</summary>
    public Task EventOrCloseAsync(Guid tableId) => Task.WhenAny(Signal(tableId.ToString()).Task, _closed.Task).WaitAsync(Limit);

    public bool IsClosed => _closed.Task.IsCompleted;

    public Task ClosedAsync() => _closed.Task.WaitAsync(Limit);

    public bool WentReconnecting => _reconnecting.Task.IsCompleted;

    public Task ReconnectingAsync() => _reconnecting.Task.WaitAsync(Limit);

    public Task ReconnectedAsync() => _reconnected.Task.WaitAsync(Limit);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

/// <summary>
/// A client that speaks the protocol by hand and ignores whatever the server tells it: it never closes on its own, it only keeps
/// reading the socket. This is the modified client, and what it reads is exactly what the server wrote to it.
/// </summary>
internal sealed class RawHubClient : IAsyncDisposable
{
    private const char Separator = '\u001e';

    private readonly WebSocket _socket;
    private readonly ConcurrentQueue<string> _frames = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private int _invocation;

    private RawHubClient(WebSocket socket)
    {
        _socket = socket;
        _reader = ReadUntilTheServerClosesAsync();
    }

    public static async Task<RawHubClient> ConnectAsync(TestServer server, HttpClient http, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/restaurant/negotiate?negotiateVersion=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var negotiated = await http.SendAsync(request);
        negotiated.StatusCode.Should().Be(HttpStatusCode.OK);
        var connectionToken = (await negotiated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("connectionToken").GetString();

        var uri = new Uri(server.BaseAddress, $"/hubs/restaurant?id={connectionToken}&access_token={token}");
        var socket = await server.CreateWebSocketClient().ConnectAsync(new UriBuilder(uri) { Scheme = "ws" }.Uri, CancellationToken.None);
        var client = new RawHubClient(socket);
        await client.SendAsync("""{"protocol":"json","version":1}""");
        return client;
    }

    public Task SendAsync(string message)
        => _socket.SendAsync(Encoding.UTF8.GetBytes(message + Separator), WebSocketMessageType.Text, true, CancellationToken.None);

    public Task InvokeAsync(string target, params string[] arguments)
    {
        var id = Interlocked.Increment(ref _invocation).ToString();
        return SendAsync(JsonSerializer.Serialize(new { type = 1, invocationId = id, target, arguments }));
    }

    /// <summary>Waits for a frame that matches, already read or still to come.</summary>
    public async Task WaitForFrameAsync(Func<string, bool> match)
    {
        using var limit = new CancellationTokenSource(HubProbe.Limit);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (!_frames.Any(match))
        {
            if (_reader.IsCompleted && !_frames.Any(match))
                throw new Xunit.Sdk.XunitException($"The socket ended without that frame. Read: {string.Join(" | ", _frames)}");
            await timer.WaitForNextTickAsync(limit.Token);
        }
    }

    public IReadOnlyList<string> Frames => _frames.ToArray();

    /// <summary>Done when the server ended the socket: nothing can be delivered to this client after it.</summary>
    public Task ServerEndedTheSocketAsync() => _reader.WaitAsync(HubProbe.Limit);

    private async Task ReadUntilTheServerClosesAsync()
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var read = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (read.MessageType == WebSocketMessageType.Close) return;
                text.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
                if (!read.EndOfMessage) continue;
                foreach (var frame in text.ToString().Split(Separator, StringSplitOptions.RemoveEmptyEntries)) _frames.Enqueue(frame);
                text.Clear();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
        {
            // The socket is gone: the reader's job ends here.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _reader;
        _socket.Dispose();
        _stop.Dispose();
    }
}

/// <summary>A company with the restaurant module: its director makes real requests, which emit the real events of the hub.</summary>
internal sealed record RealtimeCompany(HttpClient Director, SyntheticAccount DirectorAccount, string DirectorToken, Guid AreaId)
{
    public Guid TenantId => DirectorAccount.TenantId;
    public Guid StoreId => DirectorAccount.StoreId;
}

internal sealed class HubKit
{
    private static int _tableSeq;

    private readonly TestWebApplicationFactory _factory;

    public HubKit(TestWebApplicationFactory factory) => _factory = factory;

    public TestServer Server => _factory.Server;

    public async Task<RealtimeCompany> NewCompanyAsync()
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "restaurante");
        var (director, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, account.Login, account.Password);
        var created = await director.PostAsJsonAsync("/api/restaurante/areas", new CreateAreaRequest("Salao"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return new RealtimeCompany(director, account, login.AccessToken, (await created.Content.ReadFromJsonAsync<AreaDto>())!.Id);
    }

    public Task<SyntheticAccount> AddPersonAsync(RealtimeCompany company, UserRole role)
        => SyntheticTenant.AddUserAsync(_factory, company.DirectorAccount, role);

    public async Task<LoginResponse> SignInAsync(SyntheticAccount account)
        => (await AuthClientFactory.LoginWithBodyAsync(_factory, account.Login, account.Password)).Body;

    public async Task<HubProbe> ListenAsync(RealtimeCompany company, string token, HubTransport transport = HubTransport.WebSockets, TestServer? server = null)
    {
        var probe = await HubProbe.ConnectAsync(server ?? Server, token, transport);
        await probe.JoinAsync(company.StoreId);
        return probe;
    }

    public async Task<HubProbe> ListenAsAsync(RealtimeCompany company, SyntheticAccount account, HubTransport transport = HubTransport.WebSockets, TestServer? server = null)
        => await ListenAsync(company, (await SignInAsync(account)).AccessToken, transport, server);

    /// <summary>
    /// A real event, from the real endpoint: opening a dine-in order sets the table as occupied and the API tells the store group.
    /// Returns the id of the table, which identifies this event.
    /// </summary>
    public async Task<Guid> EmitEventAsync(RealtimeCompany company)
    {
        var number = $"H{Interlocked.Increment(ref _tableSeq):D4}";
        var table = await company.Director.PostAsJsonAsync("/api/restaurante/tables", new CreateTableRequest(company.AreaId, number, Capacity: 2));
        table.StatusCode.Should().Be(HttpStatusCode.Created, await table.Content.ReadAsStringAsync());
        var tableId = (await table.Content.ReadFromJsonAsync<TableDto>())!.Id;
        var order = await company.Director.PostAsJsonAsync("/api/restaurante/orders", new OpenOrderRequest("DineIn", TableId: tableId));
        order.StatusCode.Should().Be(HttpStatusCode.Created, await order.Content.ReadAsStringAsync());
        return tableId;
    }

    /// <summary>The target is listening before the change: without this, "no event after it" would prove nothing.</summary>
    public async Task BothReceiveAsync(RealtimeCompany company, params HubProbe[] probes)
    {
        var tableId = await EmitEventAsync(company);
        foreach (var probe in probes) await probe.SawAsync(tableId);
    }

    /// <summary>
    /// An event emitted now reaches who stayed connected and never reaches the target. The witness proves the event went out;
    /// the target either gets it (the failure) or is closed. Nothing here waits for time to pass.
    /// </summary>
    public async Task ShouldNoLongerReceiveAsync(RealtimeCompany company, HubProbe target, HubProbe witness)
    {
        var tableId = await EmitEventAsync(company);
        await witness.SawAsync(tableId);
        await target.EventOrCloseAsync(tableId);
        target.Saw(tableId).Should().BeFalse("the connection of whoever lost the session cannot receive one more event");
        await target.ClosedAsync();
        target.Saw(tableId).Should().BeFalse();
    }

    /// <summary>Rotates the stamp in the database with no code of the API involved: no immediate signal goes out.</summary>
    public async Task RotateStampSilentlyAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var stamp = Guid.NewGuid().ToString("N");
        var rotated = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.SecurityStamp, stamp));
        rotated.Should().Be(1);
    }

    /// <summary>Changes the status of the company in the database with no code of the API involved: no immediate signal goes out.</summary>
    public async Task SetCompanyStatusSilentlyAsync(Guid tenantId, TenantStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var changed = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, status));
        changed.Should().Be(1);
    }

    /// <summary>The same token with another lifetime, signed with the key of the test host, to reach an expiry without waiting 15 minutes.</summary>
    public static string WithLifetime(string token, TimeSpan lifetime, Func<Claim, bool>? keep = null)
    {
        var original = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var registered = new[] { "exp", "nbf", "iat", "iss", "aud" };
        var claims = original.Claims.Where(c => !registered.Contains(c.Type) && (keep is null || keep(c)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-minimum-32-characters-long!"));
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: original.Issuer,
            audience: original.Audiences.First(),
            claims: claims,
            notBefore: now.AddMinutes(-5),
            expires: now.Add(lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }
}
