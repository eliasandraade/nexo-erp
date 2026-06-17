using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Storage;

/// <summary>
/// End-to-end guard for the storage upload path used by the Orken Build daily-log photos.
/// With StorageEnabled=false (the default), upload must return a controlled 404 — never a
/// 500 from the storage provider failing to construct.
/// </summary>
[Collection("Integration")]
public class StorageUploadTests
{
    private readonly HttpClient _client;

    public StorageUploadTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    [Fact]
    public async Task Upload_BuildDailyLog_WhenStorageDisabled_Returns404_NotServerError()
    {
        await AuthenticateAsync();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "obra.jpg");
        content.Add(new StringContent("build-daily-log"), "context");

        var resp = await _client.PostAsync("/api/integrations/storage/upload", content);

        // Must never be a 5xx — the disabled gate returns a controlled 404.
        ((int)resp.StatusCode).Should().BeLessThan(500,
            because: $"upload returned {(int)resp.StatusCode}; body: {await resp.Content.ReadAsStringAsync()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
