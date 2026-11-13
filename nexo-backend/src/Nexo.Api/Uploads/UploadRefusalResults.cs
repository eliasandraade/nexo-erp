using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Middleware;

namespace Nexo.Api.Uploads;

public static class UploadRefusalResults
{
    /// <summary>The API's error shape: { error, details, requestId, code }, and Retry-After with the 429.</summary>
    public static ObjectResult ToResult(this UploadRefusal refusal, HttpContext http)
        => refusal.RetryAfterSeconds is { } seconds
            ? RateLimitRejection.Refuse(http, refusal.Message, refusal.Code, seconds)
            : new ObjectResult(new { error = refusal.Message, details = Array.Empty<string>(), requestId = http.TraceIdentifier, code = refusal.Code })
            {
                StatusCode = refusal.Status,
            };
}
