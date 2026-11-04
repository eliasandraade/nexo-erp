using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// PR 64b — two-step verification (TOTP): set up with a first code, sign-in becomes two steps,
/// codes work once, recovery codes work once, disabling needs password + code, a director can turn
/// it off for someone who lost the phone. Each test uses its own user.
/// </summary>
[Collection("Integration")]
public class MfaTests
{
    private readonly TestWebApplicationFactory _factory;
    public MfaTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string Fresh() => "t" + Guid.NewGuid().ToString("N")[..12];

    private static byte[] FromBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }

    private static string CodeAt(byte[] secret, int stepOffset) => Totp.Code(secret, Totp.StepAt(DateTime.UtcNow) + stepOffset);

    private async Task<(Guid Id, string Login, string Password, HttpClient Admin)> NewUserAsync()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var password = Fresh();
        var r = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Pessoa MFA", email = $"mfa{tag}@teste.dev", login = $"mfa{tag}", password, role = "vendedor",
            phone = (string?)null, notes = (string?)null,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return ((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), $"mfa{tag}", password, admin);
    }

    private async Task<(byte[] Secret, string[] RecoveryCodes)> EnableAsync(HttpClient client, string password)
    {
        (await client.PostAsJsonAsync("/api/auth/mfa/setup", new { password = Fresh() })).IsSuccessStatusCode
            .Should().BeFalse("setting it up asks for the password, not just a session");
        var setup = await (await client.PostAsJsonAsync("/api/auth/mfa/setup", new { password })).Content.ReadFromJsonAsync<JsonElement>();
        setup.GetProperty("otpAuthUri").GetString().Should().StartWith("otpauth://totp/Orken:");
        var secret = FromBase32(setup.GetProperty("secret").GetString()!);
        var enabled = await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = CodeAt(secret, 0) });
        enabled.StatusCode.Should().Be(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray()
            .Select(x => x.GetString()!).ToArray();
        return (secret, codes);
    }

    private async Task<string> FirstStepAsync(string login, string password)
    {
        var r = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(login, password));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
        body.TryGetProperty("accessToken", out _).Should().BeFalse("the password alone opens nothing");
        return body.GetProperty("mfaChallenge").GetString()!;
    }

    private Task<HttpResponseMessage> SecondStepAsync(string challenge, string? code, string? recoveryCode = null)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/mfa/verify", new { challenge, code, recoveryCode });

    [Fact]
    public async Task Sign_in_takes_a_code_once_the_second_step_is_on()
    {
        var user = await NewUserAsync();
        var (client, _) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        var (secret, codes) = await EnableAsync(client, user.Password);
        codes.Should().HaveCount(8).And.OnlyHaveUniqueItems();
        (await client.GetAsync("/api/auth/mfa")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "turning it on ends every session");

        var challenge = await FirstStepAsync(user.Login, user.Password);
        (await SecondStepAsync(challenge, "000000")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // one code for the whole test: recomputing it could cross a 30 s step and test a different, fresh code
        var code = CodeAt(secret, 1);
        var ok = await SecondStepAsync(challenge, code);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();

        (await SecondStepAsync(challenge, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a challenge works once");
        var again = await FirstStepAsync(user.Login, user.Password);
        (await SecondStepAsync(again, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a code works once");
    }

    [Fact]
    public async Task Recovery_codes_sign_in_once_and_disabling_needs_password_and_code()
    {
        var user = await NewUserAsync();
        var (client, _) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        var (_, codes) = await EnableAsync(client, user.Password);

        var challenge = await FirstStepAsync(user.Login, user.Password);
        var signedIn = await SecondStepAsync(challenge, null, codes[0].ToUpperInvariant());
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await FirstStepAsync(user.Login, user.Password);
        (await SecondStepAsync(again, null, codes[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a recovery code works once");

        var fresh = _factory.CreateApiClient();
        fresh.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        (await fresh.PostAsJsonAsync("/api/auth/mfa/disable", new { password = Fresh(), code = codes[1] }))
            .IsSuccessStatusCode.Should().BeFalse("wrong password");
        (await fresh.PostAsJsonAsync("/api/auth/mfa/disable", new { password = user.Password, code = codes[2] }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "a recovery code typed in the code field works too");
        var plain = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(user.Login, user.Password));
        (await plain.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("accessToken", out _).Should().BeTrue("one step again");
    }

    [Fact]
    public async Task A_director_can_turn_it_off_for_someone_who_lost_the_phone()
    {
        var user = await NewUserAsync();
        var (client, _) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        await EnableAsync(client, user.Password);
        (await user.Admin.GetFromJsonAsync<JsonElement>($"/api/users/{user.Id}")).GetProperty("mfaEnabled").GetBoolean().Should().BeTrue();

        (await user.Admin.PostAsync($"/api/users/{user.Id}/mfa/disable", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await user.Admin.GetFromJsonAsync<JsonElement>($"/api/users/{user.Id}")).GetProperty("mfaEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Wrong_codes_count_toward_the_lockout_and_an_unknown_challenge_is_refused()
    {
        var user = await NewUserAsync();
        var (client, _) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        var (secret, _) = await EnableAsync(client, user.Password);
        var challenge = await FirstStepAsync(user.Login, user.Password);

        (await SecondStepAsync(Guid.NewGuid().ToString("N"), CodeAt(secret, 1))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        for (var i = 0; i < 9; i++) await SecondStepAsync(challenge, "111111");
        // re-entering the (known) password must not reset the count — else the code could be guessed forever
        var renewed = await FirstStepAsync(user.Login, user.Password);
        (await SecondStepAsync(renewed, "111111")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
