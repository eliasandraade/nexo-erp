using System.Net.Http.Headers;

namespace Nexo.IntegrationTests.Common;

/// <summary>
/// Shared HttpClient extension methods for integration tests.
///
/// Centralizes WithBearer() to avoid the SonarLint / CS0101 issue
/// caused by the same extension being defined in multiple test files.
/// </summary>
public static class TestHttpClientExtensions
{
    /// <summary>
    /// Sets the Authorization: Bearer header and returns the same client
    /// to allow fluent chaining.
    /// </summary>
    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
