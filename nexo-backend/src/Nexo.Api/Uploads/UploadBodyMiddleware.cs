using Microsoft.AspNetCore.Http.Features;
using Nexo.Api.Filters;

namespace Nexo.Api.Uploads;

/// <summary>
/// On upload routes the size is enforced by <see cref="UploadAdmission"/>, so the server's own limit steps aside. A refusal leaves the body
/// unread, and the server then discards it for a few seconds so the client can read the answer. But when the declared size is over the
/// server's limit (30 MB by default), it cuts the connection instead, and the reset can destroy the answer before the client reads it: the
/// person would see a connection failure instead of the 401, 403, 413 or 429. Runs before the middleware that can refuse. Nothing is kept
/// of what is discarded, and the server gives up on it after those seconds.
/// </summary>
public sealed class UploadBodyMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<MultipartFileBodyAttribute>() is not null)
        {
            var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = null;
        }
        return next(http);
    }
}
