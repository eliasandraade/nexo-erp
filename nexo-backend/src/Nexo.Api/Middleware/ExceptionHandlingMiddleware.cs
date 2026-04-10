using System.Text.Json;
using FluentValidation;
using Nexo.Domain.Exceptions;

namespace Nexo.Api.Middleware;

/// <summary>
/// Global exception handler — converts domain and validation exceptions into
/// consistent JSON error responses. Unhandled exceptions return 500.
///
/// Response shape:
/// { "error": "...", "details": ["...", "..."] }
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception.");
            await WriteErrorAsync(ctx, 500, "An unexpected error occurred. Please try again.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext ctx,
        int statusCode,
        string error,
        string[]? details = null)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";

        var body = new
        {
            error,
            details = details ?? Array.Empty<string>(),
        };

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOpts));
    }
}
