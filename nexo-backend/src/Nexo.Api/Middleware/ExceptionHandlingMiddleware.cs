using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Nexo.Application.Common.Observability;
using Nexo.Domain.Exceptions;

namespace Nexo.Api.Middleware;

/// <summary>
/// Global exception handler — converts domain and validation exceptions into
/// consistent JSON error responses. Unhandled exceptions return 500.
///
/// Response shape:
/// { "error": "...", "details": ["...", "..."], "requestId": "..." }
/// The request id matches the X-Request-Id header and the RequestId of every log line of the
/// request. Unexpected errors (500) are also recorded by <see cref="IErrorTracker"/>.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IErrorTracker _errors;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IErrorTracker errors)
    {
        _next = next;
        _logger = logger;
        _errors = errors;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ValidationException ex)
        {
            _logger.LogDebug("Validation failure: {Errors}", ex.Message);
            await WriteErrorAsync(ctx, 400, "Validation failed.",
                ex.Errors.Select(e => e.ErrorMessage).ToArray());
        }
        catch (NotFoundException ex)
        {
            _logger.LogDebug("Not found: {Message}", ex.Message);
            await WriteErrorAsync(ctx, 404, ex.Message);
        }
        catch (ConflictException ex)
        {
            _logger.LogDebug("Conflict: {Message}", ex.Message);
            await WriteErrorAsync(ctx, 409, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Forbidden: {Message}", ex.Message);
            await WriteErrorAsync(ctx, 403, ex.Message);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Domain rule violation: {Message}", ex.Message);
            await WriteErrorAsync(ctx, 422, ex.Message);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // the caller went away (closed tab, timeout): nothing failed, nobody to answer
            _logger.LogInformation("Request aborted by the client.");
        }
        catch (Exception ex) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // same, surfacing as a driver error (e.g. Npgsql cancelling a command)
            _logger.LogInformation(ex, "Request aborted by the client.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception.");
            await _errors.TrackAsync(new ErrorOccurrence(ex, RouteOf(ctx), ctx.Request.Method, ctx.TraceIdentifier, TenantOf(ctx)));
            await WriteErrorAsync(ctx, 500, "Erro inesperado. Tente de novo; se continuar, informe o código ao suporte.");
        }
    }

    /// <summary>The route template ("api/sales/{id:guid}"), never the raw path with its ids.</summary>
    private static string RouteOf(HttpContext ctx) => Nexo.Api.Observability.RequestLogging.RouteOf(ctx);

    private static Guid? TenantOf(HttpContext ctx)
        => Guid.TryParse(ctx.User.FindFirst("tenantId")?.Value, out var id) ? id : null;

    private static async Task WriteErrorAsync(
        HttpContext ctx,
        int statusCode,
        string error,
        string[]? details = null)
    {
        // A streamed download (e.g. the sales spreadsheet) failed midway: the status already went
        // out, so cut the connection — the browser reports a failed download, not a broken file.
        if (ctx.Response.HasStarted)
        {
            ctx.Abort();
            return;
        }
        ctx.Response.Headers.Remove("Content-Disposition");
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";

        var body = new
        {
            error,
            details = details ?? Array.Empty<string>(),
            requestId = ctx.TraceIdentifier,
        };

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOpts));
    }
}
