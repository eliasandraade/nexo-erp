using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Products;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Enums;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Storage;

/// <summary>
/// Every upload route decides who may call it, the flags, the context and the declared size before reading the body. Proven on real
/// Kestrel: the application reads zero bytes of the request, the object store is never called and no temp file appears.
/// </summary>
[Collection(UploadKestrelCollection.Name)]
public sealed class UploadEarlyRefusalTests(UploadKestrelFactory factory)
{
    private const long MiB = 1024 * 1024;
    private const string Upload = "/api/integrations/storage/upload";
    private static readonly Guid Missing = Guid.NewGuid();

    private static string StorageUrl(string context) => $"{Upload}?context={context}";

    /// <summary>Sends a body that stalls after its first bytes: a refusal has to come without waiting for the rest.</summary>
    private static MultipartUpload Stalling(long declared, string? context = null)
        => new(fileBytes: 256 * 1024, declared: declared, stallAfter: 128 * 1024, fields: context is null ? null : [("context", context)]);

    private static async Task<(HttpResponseMessage Response, string Body)> SendAsync(HttpClient client, HttpMethod method, string url, HttpContent content)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = content }, timeout.Token);
        return (response, await response.Content.ReadAsStringAsync(timeout.Token));
    }

    /// <summary>The proof of a refusal before reading: nothing read, nothing stored, nothing on disk then or now.</summary>
    private static async Task AssertNothingHappenedAsync(UploadHost host, TempWatch temp)
    {
        host.Probe.Bytes.Should().Be(0, "the application must not read a single byte of a refused body");
        host.Storage.UploadCalls.Should().Be(0, "a refused upload never reaches the object store");
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
        await temp.DisposeAsync();
        temp.PeakFiles.Should().Be(0, "a refused upload never writes a temp file");
    }

    // ── Who may call ──────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_the_body_is_read()
    {
        await using var host = await factory.StartAsync();
        using var client = host.Client();
        await using var temp = new TempWatch();

        var (response, _) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(2 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingHappenedAsync(host, temp);
    }

    [Fact]
    public async Task A_role_outside_the_route_is_refused_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var seller = await SyntheticTenant.AddUserAsync(factory, company, UserRole.Vendedor);
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(seller);
        await using var temp = new TempWatch();

        var (response, _) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(2 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertNothingHappenedAsync(host, temp);
    }

    [Theory]
    [InlineData("service-record")]
    [InlineData("build-daily-log")]
    [InlineData("restaurant-logo")]
    public async Task A_role_outside_the_context_is_refused_before_the_body_is_read(string context)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var clerk = await SyntheticTenant.AddUserAsync(factory, company, UserRole.Estoquista);
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(clerk);
        await using var temp = new TempWatch();

        var (response, _) = await SendAsync(client, HttpMethod.Post, StorageUrl(context), Stalling(2 * MiB, context));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertNothingHappenedAsync(host, temp);
    }

    // ── Context and flags ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("nao-existe")]
    [InlineData("")]
    public async Task An_unknown_or_missing_context_is_refused_before_the_body_is_read(string context)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var url = context.Length == 0 ? Upload : StorageUrl(context);
        var (response, body) = await SendAsync(client, HttpMethod.Post, url, Stalling(2 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        await AssertNothingHappenedAsync(host, temp);
    }

    [Fact]
    public async Task The_storage_flag_off_is_refused_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: ("Integrations:Features:StorageEnabled", "false"));
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, _) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(2 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertNothingHappenedAsync(host, temp);
    }

    [Fact]
    public async Task A_private_context_without_a_private_bucket_is_refused_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(new RecordingStorage(refusePrivate: true));
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("build-daily-log"), Stalling(2 * MiB, "build-daily-log"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("storage_unavailable");
        await AssertNothingHappenedAsync(host, temp);
    }

    public static TheoryData<string, string> ModuleRoutes() => new()
    {
        { "POST", $"/api/v1/build/projects/{Missing}/models" },
        { "POST", $"/api/v1/build/models/{Missing}/versions" },
        { "PUT",  $"/api/v1/build/models/versions/{Missing}/viewer" },
        { "POST", $"/api/v1/build/projects/{Missing}/reality-captures" },
        { "POST", $"/api/v1/build/projects/{Missing}/documents" },
        { "POST", $"/api/restaurante/recipe-cards/{Missing}/image" },
    };

    [Theory]
    [MemberData(nameof(ModuleRoutes))]
    public async Task A_company_without_the_module_is_refused_before_the_body_is_read(string method, string url)
    {
        // a module the routes above do not need keeps the company writable; the refusal is the route's own module
        var company = await SyntheticTenant.CreateAsync(factory, "varejo");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, new HttpMethod(method), url, Stalling(2 * MiB));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        await AssertNothingHappenedAsync(host, temp);
    }

    [Theory]
    [InlineData("POST", "/api/v1/build/projects/{0}/models")]
    [InlineData("POST", "/api/v1/build/models/{0}/versions")]
    [InlineData("PUT", "/api/v1/build/models/versions/{0}/viewer")]
    public async Task The_build_3d_flag_off_is_refused_before_the_body_is_read(string method, string template)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(factory);
        (await platform.PutAsJsonAsync($"/api/platform/tenants/{company.TenantId}/build-settings", new { build3DEnabled = false }))
            .EnsureSuccessStatusCode();
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, new HttpMethod(method), string.Format(template, Missing), Stalling(2 * MiB));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be("build_3d_disabled");
        await AssertNothingHappenedAsync(host, temp);
    }

    [Theory]
    [InlineData("POST", "/api/v1/build/projects/{0}/models")]
    [InlineData("POST", "/api/v1/build/models/{0}/versions")]
    [InlineData("PUT", "/api/v1/build/models/versions/{0}/viewer")]
    [InlineData("POST", "/api/v1/build/projects/{0}/reality-captures")]
    [InlineData("POST", "/api/v1/build/projects/{0}/documents")]
    [InlineData("POST", "/api/restaurante/recipe-cards/{0}/image")]
    public async Task A_target_that_does_not_exist_is_refused_before_the_body_is_read(string method, string template)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build", "restaurante");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, new HttpMethod(method), string.Format(template, Missing), Stalling(2 * MiB));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        await AssertNothingHappenedAsync(host, temp);
    }

    // ── Declared size ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_declared_size_over_the_storage_limit_is_413_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: ("Integrations:Storage:MaxFileSizeMb", "10"));
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(12 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().Contain("10");
        await AssertNothingHappenedAsync(host, temp);
    }

    [Fact]
    public async Task A_declared_size_over_each_route_limit_is_413_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build", "restaurante");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync(settings:
        [
            ("Build:Models:MaxUploadMb", "3"),
            ("Build:RealityCapture:MaxImageMb", "1"),
            ("Build:RealityCapture:MaxVideoMb", "2"),
            ("Build:RealityCapture:MaxPointCloudMb", "3"),
        ]);
        using var client = await host.SignedInAsync(company);
        var card = await RecipeCardAsync(client);

        var routes = new (HttpMethod Method, string Url, long Declared)[]
        {
            (HttpMethod.Post, $"/api/v1/build/projects/{project}/models", 5 * MiB),
            (HttpMethod.Post, $"/api/v1/build/projects/{project}/reality-captures", 5 * MiB),
            (HttpMethod.Post, $"/api/v1/build/projects/{project}/documents", 22 * MiB),
            (HttpMethod.Post, $"/api/restaurante/recipe-cards/{card}/image", 7 * MiB),
            (HttpMethod.Post, "/api/v1/interpreter/attachments", 11 * MiB),
        };

        foreach (var (method, url, declared) in routes)
        {
            host.Probe.Reset();
            await using var temp = new TempWatch();
            var (response, body) = await SendAsync(client, method, url, Stalling(declared));
            response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, $"{url}: {body}");
            JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
            await AssertNothingHappenedAsync(host, temp);
        }
    }

    [Fact]
    public async Task A_capture_type_with_a_lower_limit_refuses_its_declared_size_before_the_body_is_read()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync(settings:
        [
            ("Build:RealityCapture:MaxImageMb", "2"),
            ("Build:RealityCapture:MaxPointCloudMb", "20"),
        ]);
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var url = $"/api/v1/build/projects/{project}/reality-captures?type=Photo";
        var (response, body) = await SendAsync(client, HttpMethod.Post, url, Stalling(5 * MiB, null));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        await AssertNothingHappenedAsync(host, temp);
    }

    /// <summary>
    /// The body keeps coming at full speed and is over the server's default limit (30 MB): if the connection were cut with data unread,
    /// the reset could destroy the answer before the client reads it, and the person would see a connection failure instead of the reason.
    /// </summary>
    [Fact]
    public async Task A_refusal_of_a_body_over_the_default_server_limit_reaches_the_client()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build", "restaurante");
        await using var host = await factory.StartAsync(settings:
        [
            ("Uploads:InFlight:Enabled", "true"),
            ("Uploads:InFlight:Large:PerUser", "1"),
            ("RateLimiting:Uploads:Enabled", "false"),
        ]);
        using var anonymous = host.Client();
        using var client = await host.SignedInAsync(company);
        var missingProject = $"/api/v1/build/projects/{Missing}/models";

        for (var i = 0; i < 5; i++)
        {
            var (unauthorized, _) = await SendAsync(anonymous, HttpMethod.Post, StorageUrl("product-image"), new MultipartUpload(50 * MiB));
            unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var (notFound, _) = await SendAsync(client, HttpMethod.Post, missingProject, new MultipartUpload(50 * MiB));
            notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var (tooLarge, _) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), new MultipartUpload(50 * MiB));
            tooLarge.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        }
    }

    // ── Bodies without a Content-Length ───────────────────────────────────────

    [Theory]
    [InlineData("/api/integrations/storage/upload?context=product-image", 11)]
    [InlineData("/api/restaurante/recipe-cards/{card}/image", 6)]
    [InlineData("/api/v1/interpreter/attachments", 11)]
    [InlineData("/api/v1/build/projects/{project}/documents", 21)]
    public async Task A_chunked_body_over_the_limit_is_cut_at_the_limit_with_a_clean_413(string template, long limitMiB)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build", "restaurante");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync(settings: ("Integrations:Storage:MaxFileSizeMb", "10"));
        using var client = await host.SignedInAsync(company);
        var card = await RecipeCardAsync(client);
        await using var temp = new TempWatch();

        var url = template.Replace("{card}", card.ToString()).Replace("{project}", project.ToString());
        var (response, body) = await SendAsync(client, HttpMethod.Post, url,
            new MultipartUpload(fileBytes: (limitMiB + 30) * MiB, chunked: true, fields: [("context", "product-image")]));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        host.Probe.Bytes.Should().BeLessThanOrEqualTo((limitMiB + 1) * MiB, "the body is cut at the limit, not read to its end");
        host.Storage.UploadCalls.Should().Be(0);
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0, "the temp file of a refused request must be gone");
    }

    [Fact]
    public async Task A_chunked_body_within_the_limit_is_stored()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: ("Integrations:Storage:MaxFileSizeMb", "10"));
        using var client = await host.SignedInAsync(company);

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"),
            new MultipartUpload(fileBytes: 2 * MiB, chunked: true, fields: [("context", "product-image")]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        host.Storage.Keys.Should().ContainSingle();
    }

    // ── MaxFileSizeMb ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MaxFileSizeMb_is_honoured_at_the_declared_size_and_after_reading()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: ("Integrations:Storage:MaxFileSizeMb", "2"));
        using var client = await host.SignedInAsync(company);

        // over the file limit by a little: fits the body allowance (file + framing), refused once its size is known
        var (over, overBody) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"),
            new MultipartUpload(fileBytes: 2 * MiB + 1, fields: [("context", "product-image")]));
        over.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, overBody);
        JsonDocument.Parse(overBody).RootElement.GetProperty("error").GetString().Should().Contain("2MB");
        host.Storage.UploadCalls.Should().Be(0);

        // exactly at the limit: accepted
        var (exact, exactBody) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"),
            new MultipartUpload(fileBytes: 2 * MiB, fields: [("context", "product-image")]));
        exact.StatusCode.Should().Be(HttpStatusCode.OK, exactBody);

        // far over: refused by what it declares
        host.Probe.Reset();
        var (declared, declaredBody) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(4 * MiB, "product-image"));
        declared.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, declaredBody);
        host.Probe.Bytes.Should().Be(0);
    }

    [Fact]
    public async Task A_higher_MaxFileSizeMb_does_not_raise_the_twenty_MiB_ceiling_of_the_route()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(settings: ("Integrations:Storage:MaxFileSizeMb", "50"));
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"), Stalling(21 * MiB, "product-image"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        await AssertNothingHappenedAsync(host, temp);
    }

    // ── Real file type ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_spoofed_type_is_refused_and_nothing_is_stored()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build", "restaurante");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        var card = await RecipeCardAsync(client);
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0, 0, 0];

        var (storage, storageBody) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"),
            new MultipartUpload(fileBytes: 300 * 1024, head: zip, fields: [("context", "product-image")]));
        storage.StatusCode.Should().Be(HttpStatusCode.BadRequest, storageBody);

        var (recipe, recipeBody) = await SendAsync(client, HttpMethod.Post, $"/api/restaurante/recipe-cards/{card}/image",
            new MultipartUpload(fileBytes: 300 * 1024, head: zip));
        recipe.StatusCode.Should().Be(HttpStatusCode.BadRequest, recipeBody);

        host.Storage.UploadCalls.Should().Be(0);
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
    }

    // ── Valid uploads ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_public_upload_works_end_to_end()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("product-image"),
            new MultipartUpload(fileBytes: 3 * MiB, fields: [("context", "product-image")]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("key").GetString().Should().StartWith($"tenants/{company.TenantId}/products/").And.EndWith(".png");
        json.GetProperty("publicUrl").GetString().Should().Contain(json.GetProperty("key").GetString());
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0, "the temp file is removed when the response completes");
    }

    [Fact]
    public async Task A_valid_private_upload_works_end_to_end()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        var (response, body) = await SendAsync(client, HttpMethod.Post, StorageUrl("build-daily-log"),
            new MultipartUpload(fileBytes: 1 * MiB, fields: [("context", "build-daily-log")]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("key").GetString().Should().StartWith($"private/tenants/{company.TenantId}/build/daily-logs/");
        json.TryGetProperty("publicUrl", out _).Should().BeFalse();
        json.GetProperty("url").GetString().Should().Contain("signature=");
    }

    [Fact]
    public async Task A_model_upload_keeps_a_single_copy_on_disk_and_is_stored_privately()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);
        await using var temp = new TempWatch();

        var ifc = "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n"u8.ToArray();
        var (response, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/build/projects/{project}/models",
            new MultipartUpload(fileBytes: 3 * MiB, contentType: "application/octet-stream", fileName: "casa.ifc", head: ifc,
                fields: [("name", "Arquitetura"), ("discipline", "Arquitetura")]));

        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        host.Storage.Keys.Should().ContainSingle().Which.Should().StartWith($"private/tenants/{company.TenantId}/build/models/");
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
        await temp.DisposeAsync();
        temp.PeakFiles.Should().Be(1, "the upload must not be copied to a second temp file");
    }

    [Fact]
    public async Task A_reality_capture_and_a_document_keep_their_form_fields()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        var (capture, captureBody) = await SendAsync(client, HttpMethod.Post, $"/api/v1/build/projects/{project}/reality-captures?type=Photo",
            new MultipartUpload(fileBytes: 200 * 1024, fileName: "obra.png", fields:
            [
                ("type", "Photo"), ("title", "Fachada"), ("capturedAt", "2026-03-01T10:00:00Z"), ("notes", "Vista frontal"), ("clientVisible", "true"),
            ]));
        capture.StatusCode.Should().Be(HttpStatusCode.Created, captureBody);
        var captureJson = JsonDocument.Parse(captureBody).RootElement;
        captureJson.GetProperty("title").GetString().Should().Be("Fachada");
        captureJson.GetProperty("notes").GetString().Should().Be("Vista frontal");
        captureJson.GetProperty("clientVisible").GetBoolean().Should().BeTrue();

        var (document, documentBody) = await SendAsync(client, HttpMethod.Post, $"/api/v1/build/projects/{project}/documents",
            new MultipartUpload(fileBytes: 200 * 1024, contentType: "application/pdf", fileName: "contrato.pdf", head: "%PDF-1.7\n"u8.ToArray(), fields:
            [
                ("category", "Contrato"), ("title", "Contrato principal"), ("validUntil", "2027-01-31"), ("clientVisible", "true"),
            ]));
        document.StatusCode.Should().Be(HttpStatusCode.Created, documentBody);
        var documentJson = JsonDocument.Parse(documentBody).RootElement;
        documentJson.GetProperty("title").GetString().Should().Be("Contrato principal");
        documentJson.GetProperty("category").GetString().Should().Be("Contrato");
        documentJson.GetProperty("validUntil").GetString().Should().Be("2027-01-31");
        documentJson.GetProperty("clientVisible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_invalid_form_field_is_still_a_400_and_nothing_is_stored()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        var project = await ProjectAsync(company);
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        var (response, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/build/projects/{project}/reality-captures",
            new MultipartUpload(fileBytes: 200 * 1024, fileName: "obra.png", fields: [("type", "Photo"), ("title", "x"), ("capturedAt", "ontem de tarde")]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        host.Storage.UploadCalls.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static async Task<Guid> RecipeCardAsync(HttpClient client)
    {
        var code = "UP" + Guid.NewGuid().ToString("N")[..10];
        var product = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: 10m, CostPrice: 5m, TrackStock: false));
        product.EnsureSuccessStatusCode();
        var productId = (await product.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var card = await client.PostAsJsonAsync("/api/restaurante/recipe-cards", new CreateRecipeCardRequest(productId, 1m, YieldUnit: "Un"));
        card.EnsureSuccessStatusCode();
        return (await card.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
