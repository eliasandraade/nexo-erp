using System.Net;
using System.Text.Json;
using FluentAssertions;
using Nexo.IntegrationTests.Common;

namespace Nexo.IntegrationTests.Storage;

/// <summary>
/// The context now travels in the query string, so it can be decided before the body. A private context must still never reach the
/// public bucket on this path: not by a different value in the form, not by the letter case, not when the private bucket is missing.
/// </summary>
[Collection(UploadKestrelCollection.Name)]
public sealed class UploadPrivateSeparationTests(UploadKestrelFactory factory)
{
    private const string Upload = "/api/integrations/storage/upload";

    public static TheoryData<string> PrivateContexts() => new()
    {
        "build-daily-log", "build-selection-option", "build-quality", "build-handover", "build-assistance", "build-reality", "service-record",
        "SERVICE-RECORD",
    };

    [Theory]
    [MemberData(nameof(PrivateContexts))]
    public async Task A_private_context_is_stored_under_the_private_prefix_and_never_gets_a_public_url(string context)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        using var response = await client.PostAsync($"{Upload}?context={context}", new MultipartUpload(128 * 1024, fields: [("context", context)]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.TryGetProperty("publicUrl", out _).Should().BeFalse("a private file never gets a public URL");
        host.Storage.Keys.Should().ContainSingle().Which.Should().StartWith($"private/tenants/{company.TenantId}/");
    }

    [Theory]
    [MemberData(nameof(PrivateContexts))]
    public async Task Without_a_private_bucket_a_private_context_stores_nothing(string context)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(new RecordingStorage(refusePrivate: true));
        using var client = await host.SignedInAsync(company);

        using var response = await client.PostAsync($"{Upload}?context={context}", new MultipartUpload(128 * 1024, stallAfter: 64 * 1024, fields: [("context", context)]));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Storage.UploadCalls.Should().Be(0);
        host.Probe.Bytes.Should().Be(0, "no body is read for a file that has nowhere to go");
    }

    [Theory]
    [InlineData("product-image", "service-record")]
    [InlineData("service-record", "product-image")]
    public async Task A_form_field_that_disagrees_with_the_query_is_refused_and_nothing_is_stored(string query, string form)
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync();
        using var client = await host.SignedInAsync(company);

        using var response = await client.PostAsync($"{Upload}?context={query}", new MultipartUpload(128 * 1024, fields: [("context", form)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a body claiming a private context must never land in the public bucket, nor the reverse");
        host.Storage.UploadCalls.Should().Be(0);
        (await TempWatch.FilesLeftAfterAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_public_context_is_public_and_needs_no_private_bucket()
    {
        var company = await SyntheticTenant.CreateAsync(factory, "build");
        await using var host = await factory.StartAsync(new RecordingStorage(refusePrivate: true));
        using var client = await host.SignedInAsync(company);

        using var response = await client.PostAsync($"{Upload}?context=build-portal-logo", new MultipartUpload(128 * 1024, fields: [("context", "build-portal-logo")]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("publicUrl").GetString().Should().NotBeNullOrWhiteSpace();
        host.Storage.Keys.Should().ContainSingle().Which.Should().StartWith($"tenants/{company.TenantId}/build/portal/logo/");
    }
}
