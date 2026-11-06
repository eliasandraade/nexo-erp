using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Domain.Enums;
using Nexo.IntegrationTests.Common;

namespace Nexo.IntegrationTests.Storage;

/// <summary>
/// In-flight bounds per signed-in user, per company and global, and the rate window per user: simultaneous requests on real Kestrel,
/// held open by stalling their bodies, so what is "in flight" is not a matter of timing.
/// </summary>
[Collection(UploadKestrelCollection.Name)]
public sealed class UploadConcurrencyTests(UploadKestrelFactory factory)
{
    private const long MiB = 1024 * 1024;
    private const string Url = "/api/integrations/storage/upload?context=product-image";

    private static (string, string)[] InFlight(int perUser, int perCompany, int global) =>
    [
        ("Uploads:InFlight:Enabled", "true"),
        ("Uploads:InFlight:Small:PerUser", perUser.ToString()),
        ("Uploads:InFlight:Small:PerCompany", perCompany.ToString()),
        ("Uploads:InFlight:Small:Global", global.ToString()),
    ];

    private static (MultipartUpload Body, Task<HttpResponseMessage> Response) StartStalled(HttpClient client, string url = Url, long fileBytes = 2 * MiB)
    {
        var body = new MultipartUpload(fileBytes, stallAfter: 512 * 1024, fields: [("context", "product-image")]);
        return (body, client.PostAsync(url, body));
    }

    private static MultipartUpload Small() => new(64 * 1024, fields: [("context", "product-image")]);

    private static async Task AssertBusyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, body);
        response.Headers.RetryAfter?.Delta.Should().BeGreaterThan(TimeSpan.Zero, "the client is told when to come back");
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("code").GetString().Should().Be("upload_busy");
        json.GetProperty("error").GetString().Should().Contain("Aguarde", "the message is for a person, in Portuguese");
        json.GetProperty("retryAfterSeconds").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Uploads_over_the_user_bound_get_429_and_leave_nothing_behind_while_those_within_it_succeed()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 2, perCompany: 10, global: 10));
        using var client = await host.SignedInAsync(company);

        var first = StartStalled(client);
        var second = StartStalled(client);
        await TempWatch.WaitForFilesAsync(2);

        // the third is refused while the others hold their slots, and writes nothing
        var probeBefore = host.Probe.Bytes;
        await using (var temp = new TempWatch())
        {
            using var overflow = await client.PostAsync(Url, new MultipartUpload(2 * MiB, stallAfter: 128 * 1024, fields: [("context", "product-image")]));
            await AssertBusyAsync(overflow);
            await temp.DisposeAsync();
            temp.PeakFiles.Should().Be(2, "the refused upload added no temp file");
        }
        (host.Probe.Bytes - probeBefore).Should().BeLessThan(2 * MiB, "only the stalled uploads were being read");

        first.Body.Resume();
        second.Body.Resume();
        (await first.Response).StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Response).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Storage.Keys.Should().HaveCount(2);
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0, "nothing stays on disk");

        // the slots came back
        using var after = await client.PostAsync(Url, Small());
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Many_simultaneous_uploads_within_the_bounds_all_succeed()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 4, perCompany: 4, global: 8));
        using var client = await host.SignedInAsync(company);
        var uploads = Enumerable.Range(0, 4).Select(_ => StartStalled(client)).ToArray();
        await TempWatch.WaitForFilesAsync(4);

        foreach (var upload in uploads) upload.Body.Resume();
        var responses = await Task.WhenAll(uploads.Select(u => u.Response));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK);
        host.Storage.Keys.Should().HaveCount(4);
    }

    [Fact]
    public async Task Two_companies_do_not_block_each_other()
    {
        var busy = await SyntheticTenant.CreateAsync(factory, "build");
        var other = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 2, perCompany: 2, global: 10));
        using var busyClient = await host.SignedInAsync(busy);
        using var otherClient = await host.SignedInAsync(other);

        var uploads = new[] { StartStalled(busyClient), StartStalled(busyClient) };
        await TempWatch.WaitForFilesAsync(2);

        using (var refused = await busyClient.PostAsync(Url, Small())) await AssertBusyAsync(refused);
        using (var served = await otherClient.PostAsync(Url, Small()))
            served.StatusCode.Should().Be(HttpStatusCode.OK, "another company has its own bound");

        foreach (var upload in uploads) upload.Body.Resume();
        await Task.WhenAll(uploads.Select(u => u.Response));
    }

    [Fact]
    public async Task Colleagues_share_the_company_bound_but_not_the_user_bound()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var colleague = await SyntheticTenant.AddUserAsync(factory, company, UserRole.Diretoria);
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 1, perCompany: 2, global: 10));
        using var first = await host.SignedInAsync(company);
        using var second = await host.SignedInAsync(colleague);

        var upload = StartStalled(first);
        await TempWatch.WaitForFilesAsync(1);

        using (var sameUser = await first.PostAsync(Url, Small())) await AssertBusyAsync(sameUser);

        var colleagueUpload = StartStalled(second);
        await TempWatch.WaitForFilesAsync(2);

        // the company's two slots are taken, one by each person
        var third = await SyntheticTenant.AddUserAsync(factory, company, UserRole.Diretoria);
        using var thirdClient = await host.SignedInAsync(third);
        using (var overCompany = await thirdClient.PostAsync(Url, Small())) await AssertBusyAsync(overCompany);

        upload.Body.Resume();
        colleagueUpload.Body.Resume();
        await Task.WhenAll(upload.Response, colleagueUpload.Response);
    }

    [Fact]
    public async Task The_global_bound_holds_across_companies()
    {
        var a = await SyntheticTenant.CreateAsync(factory, "build");
        var b = await SyntheticTenant.CreateAsync(factory, "build");
        var c = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 5, perCompany: 5, global: 2));
        using var aClient = await host.SignedInAsync(a);
        using var bClient = await host.SignedInAsync(b);
        using var cClient = await host.SignedInAsync(c);

        var uploads = new[] { StartStalled(aClient), StartStalled(bClient) };
        await TempWatch.WaitForFilesAsync(2);

        using (var refused = await cClient.PostAsync(Url, Small())) await AssertBusyAsync(refused);

        foreach (var upload in uploads) upload.Body.Resume();
        await Task.WhenAll(uploads.Select(u => u.Response));
        using var served = await cClient.PostAsync(Url, Small());
        served.StatusCode.Should().Be(HttpStatusCode.OK, "the bound is on what is in flight, not on what happened before");
    }

    [Fact]
    public async Task Large_uploads_have_their_own_pool_and_never_block_small_ones()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync(settings:
        [
            ("Uploads:InFlight:Enabled", "true"),
            ("Uploads:InFlight:Large:PerUser", "1"),
            ("Uploads:InFlight:Large:PerCompany", "1"),
            ("Uploads:InFlight:Large:Global", "1"),
        ]);
        using var client = await host.SignedInAsync(company);
        var modelUrl = $"/api/v1/build/projects/{project}/models";
        var ifc = "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n"u8.ToArray();

        var large = new MultipartUpload(60 * MiB, contentType: "application/octet-stream", fileName: "a.ifc", head: ifc, stallAfter: 512 * 1024);
        var largeResponse = client.PostAsync(modelUrl, large);
        await TempWatch.WaitForFilesAsync(1);

        // another large one waits, with what it declares; a small file on the same route goes through the small pool
        using (var secondLarge = await client.PostAsync(modelUrl,
                   new MultipartUpload(60 * MiB, contentType: "application/octet-stream", fileName: "b.ifc", head: ifc, stallAfter: 128 * 1024)))
            await AssertBusyAsync(secondLarge);
        using (var image = await client.PostAsync(Url, Small()))
            image.StatusCode.Should().Be(HttpStatusCode.OK, "a large upload does not hold a product image back");

        large.Resume();
        var finished = await largeResponse;
        finished.StatusCode.Should().Be(HttpStatusCode.Created, await finished.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cancelled_upload_leaves_no_temp_file_and_gives_its_slot_back()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 1, perCompany: 1, global: 1));
        using var client = await host.SignedInAsync(company);

        using var cancel = new CancellationTokenSource();
        var body = new MultipartUpload(4 * MiB, stallAfter: 2 * MiB, fields: [("context", "product-image")]);
        var request = client.PostAsync(Url, body, cancel.Token);
        await TempWatch.WaitForFilesAsync(1);

        using (var refused = await client.PostAsync(Url, Small())) await AssertBusyAsync(refused);

        await cancel.CancelAsync();
        await FluentActions.Awaiting(() => request).Should().ThrowAsync<OperationCanceledException>();

        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0, "the temp file of a cancelled request must be gone");
        host.Storage.UploadCalls.Should().Be(0);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        HttpStatusCode status;
        do
        {
            using var again = await client.PostAsync(Url, Small());
            status = again.StatusCode;
            if (status != HttpStatusCode.OK) await Task.Delay(50);
        } while (status != HttpStatusCode.OK && DateTime.UtcNow < deadline);
        status.Should().Be(HttpStatusCode.OK, "the slot of a cancelled upload comes back");
    }

    [Fact]
    public async Task A_slot_is_released_only_after_the_temp_file_is_gone()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var storage = new RecordingStorage { HoldUploads = new TaskCompletionSource() };
        await using var host = await factory.StartAsync(storage, InFlight(perUser: 1, perCompany: 1, global: 1));
        using var client = await host.SignedInAsync(company);

        // the whole file is on disk and the store is still answering: the slot is held for all of that
        var upload = client.PostAsync(Url, new MultipartUpload(1 * MiB, fields: [("context", "product-image")]));
        await TempWatch.WaitForFilesAsync(1);
        using (var refused = await client.PostAsync(Url, Small())) await AssertBusyAsync(refused);

        storage.HoldUploads!.TrySetResult();
        (await upload).StatusCode.Should().Be(HttpStatusCode.OK);

        // with one slot there is never a second file: whenever an upload is admitted, the previous file has already been removed
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
        await using var temp = new TempWatch();
        for (var i = 0; i < 20; i++)
        {
            using var next = await client.PostAsync(Url, new MultipartUpload(512 * 1024, fields: [("context", "product-image")]));
            if (next.StatusCode == HttpStatusCode.TooManyRequests) await Task.Delay(5);
            else next.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
        await temp.DisposeAsync();
        temp.PeakFiles.Should().BeLessThanOrEqualTo(1);
    }

    // ── Rate window ───────────────────────────────────────────────────────────

    private static (string, string)[] Window(int permits) =>
    [
        ("RateLimiting:Uploads:Enabled", "true"),
        ("RateLimiting:Uploads:PermitLimit", permits.ToString()),
        ("RateLimiting:Uploads:WindowSeconds", "300"),
    ];

    [Fact]
    public async Task The_rate_window_answers_429_with_retry_after_before_reading()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: Window(1));
        using var client = await host.SignedInAsync(company);

        using (var first = await client.PostAsync(Url, Small())) first.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Probe.Reset();

        using var second = await client.PostAsync(Url, new MultipartUpload(2 * MiB, stallAfter: 128 * 1024, fields: [("context", "product-image")]));

        var body = await second.Content.ReadAsStringAsync();
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, body);
        second.Headers.RetryAfter?.Delta.Should().BeGreaterThan(TimeSpan.Zero);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("code").GetString().Should().Be("rate_limited");
        json.GetProperty("error").GetString().Should().Contain("envios").And.Contain("Aguarde");
        host.Probe.Bytes.Should().Be(0);
    }

    [Fact]
    public async Task One_user_spending_the_window_does_not_throttle_a_colleague_or_another_company_behind_the_same_ip()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var colleague = await SyntheticTenant.AddUserAsync(factory, company, UserRole.Diretoria);
        var other = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: Window(2));
        using var spender = await host.SignedInAsync(company);
        using var colleagueClient = await host.SignedInAsync(colleague);
        using var otherClient = await host.SignedInAsync(other);

        for (var i = 0; i < 2; i++)
        {
            using var ok = await spender.PostAsync(Url, Small());
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using (var over = await spender.PostAsync(Url, Small())) over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using (var a = await colleagueClient.PostAsync(Url, Small())) a.StatusCode.Should().Be(HttpStatusCode.OK, "terminals of one store are not one IP");
        using (var b = await otherClient.PostAsync(Url, Small())) b.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_calls_spend_no_window_and_stay_401()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: Window(1));
        using var anonymous = host.Client();
        using var client = await host.SignedInAsync(company);

        for (var i = 0; i < 3; i++)
        {
            using var refused = await anonymous.PostAsync(Url, Small());
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using var served = await client.PostAsync(Url, Small());
        served.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Public portal photo ───────────────────────────────────────────────────

    private async Task<(string Url, string Token)> PortalRequestAsync(SyntheticAccount company)
    {
        var admin = await AuthClientFactory.LoginAsync(factory, company.Login, company.Password);
        var project = await ProjectAsync(company);
        await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{project}/handover", new { scheduledFor = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd") }));
        await Ok(admin.PostAsync($"/api/v1/build/projects/{project}/handover/deliver", null));
        var companySlug = "uploadc-" + Guid.NewGuid().ToString("N")[..8];
        await Ok(admin.PutAsJsonAsync("/api/v1/build/portal/company", new { slug = companySlug, displayName = "Construtora Upload", contactEmail = "contato@" + TestCredentials.TestDomain }));
        var slug = "uploadp-" + Guid.NewGuid().ToString("N")[..8];
        var password = "Qx!" + Guid.NewGuid().ToString("N")[..12];
        await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{project}/portal", new { slug, access = "Public", title = "Obra" }));
        await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{project}/portal/password", new { password }));
        await Ok(admin.PutAsJsonAsync($"/api/v1/build/projects/{project}/portal", new { slug, access = "Password", title = "Obra" }));
        await Ok(admin.PostAsync($"/api/v1/build/projects/{project}/portal/publish", null));
        var portal = $"/api/public/build/{companySlug}/{slug}";
        var session = await factory.CreateApiClient().PostAsJsonAsync($"{portal}/session", new { password });
        session.EnsureSuccessStatusCode();
        var token = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var request = await admin.PostAsJsonAsync($"/api/v1/build/projects/{project}/assistance", new { category = "Estrutura", title = "Trinca", description = "Trinca na viga." });
        request.EnsureSuccessStatusCode();
        var publicId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publicId").GetGuid();
        return ($"{portal}/assistance/{publicId}/photos", token);

        static async Task Ok(Task<HttpResponseMessage> call) => (await call).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_chunked_portal_photo_over_the_limit_is_413_not_a_bad_request()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var (url, token) = await PortalRequestAsync(company);
        await using var host = await factory.StartAsync();
        using var client = host.Client(headers: [new("X-Build-Portal-Session", token)]);

        using var response = await client.PostAsync(url, new MultipartUpload(40 * MiB, chunked: true));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("file_too_large");
        host.Probe.Bytes.Should().BeLessThanOrEqualTo(10 * MiB);
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Portal_photos_count_in_the_bound_of_their_company()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var (url, token) = await PortalRequestAsync(company);
        await using var host = await factory.StartAsync(settings: InFlight(perUser: 5, perCompany: 1, global: 5));
        using var portal = host.Client(headers: [new("X-Build-Portal-Session", token)]);
        using var office = await host.SignedInAsync(company);

        var photo = new MultipartUpload(2 * MiB, stallAfter: 512 * 1024);
        var pending = portal.PostAsync(url, photo);
        await TempWatch.WaitForFilesAsync(1);

        using (var second = await portal.PostAsync(url, new MultipartUpload(64 * 1024)))
        {
            var body = await second.Content.ReadAsStringAsync();
            second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, body);
            second.Headers.RetryAfter?.Delta.Should().BeGreaterThan(TimeSpan.Zero);
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("upload_busy");
        }
        using (var fromTheOffice = await office.PostAsync(Url, Small())) await AssertBusyAsync(fromTheOffice);

        photo.Resume();
        var finished = await pending;
        finished.StatusCode.Should().Be(HttpStatusCode.OK, await finished.Content.ReadAsStringAsync());
    }

    private async Task<Guid> ProjectAsync(SyntheticAccount company)
    {
        var client = await AuthClientFactory.LoginAsync(factory, company.Login, company.Password);
        var response = await client.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra Upload", clientName = "Cliente Upload", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
