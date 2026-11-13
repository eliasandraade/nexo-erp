using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Uploads;

/// <summary>Who an in-flight upload counts against: its company and, inside it, the person (or the portal client).</summary>
public readonly record struct UploadCaller(string Company, string Who)
{
    public static UploadCaller Of(ClaimsPrincipal user)
        => new(user.FindFirstValue("tenantId") ?? "unknown", user.FindFirstValue("userId") ?? "unknown");
}

/// <summary>In-flight uploads a pool admits at once.</summary>
public readonly record struct UploadPoolLimits(int PerUser, int PerCompany, int Global, int RetryAfterSeconds);

/// <summary>Why an upload was not admitted or could not be read. Each route turns it into its own error shape.</summary>
public sealed record UploadRefusal(int Status, string Code, string Message, int? RetryAfterSeconds = null);

/// <summary>The form of an admitted upload, or the reason there is none.</summary>
public readonly record struct UploadRead(IFormCollection? Form, UploadRefusal? Refusal);

/// <summary>
/// The only way an upload body is read. A route first decides everything it can without the body (who, flags, context, target),
/// then calls <see cref="ReadAsync(HttpContext, long, string, CancellationToken)"/>, which refuses a declared size over the route's
/// limit, takes an in-flight slot, cuts a body without a Content-Length at the limit, and answers 413 or 429 without touching the disk
/// (past the limit, what was already written goes away with the response).
/// <para>
/// Temp-disk ceiling: the framework spools each file to one temp file under ASPNETCORE_TEMP (nothing else copies it). A small-pool
/// request never carries more than <see cref="SmallBodyLimitBytes"/>, a large-pool one never more than <see cref="LargestBodyBytes"/>,
/// and each pool admits <see cref="UploadPoolLimits.Global"/> at once, so the disk holds at most <see cref="TempCeilingBytes"/>.
/// Two pools, so a few large models cannot hold back a product image.
/// </para>
/// </summary>
public sealed class UploadAdmission : IDisposable
{
    public const string RateLimitPolicy = "uploads";
    public const string BusyCode = "upload_busy";
    public const string TooLargeCode = "file_too_large";
    public const string InvalidFormCode = "upload_invalid_form";

    private const long MiB = 1024 * 1024;

    /// <summary>Room for the multipart framing and the text fields around the file.</summary>
    public const long MultipartOverheadBytes = 1 * MiB;

    /// <summary>The largest body of the small pool: reality capture's default image limit (25 MB) with its framing.</summary>
    public const long SmallBodyLimitBytes = 26 * MiB;

    private readonly bool _enabled;
    private readonly BuildTwinOptions _build;
    private readonly PartitionedRateLimiter<UploadCaller> _small;
    private readonly PartitionedRateLimiter<UploadCaller> _large;

    public UploadAdmission(IConfiguration config, BuildTwinOptions build)
    {
        _enabled = config.GetValue("Uploads:InFlight:Enabled", true);
        _build = build;
        Small = ReadLimits(config, "Small", new UploadPoolLimits(PerUser: 4, PerCompany: 8, Global: 24, RetryAfterSeconds: 5));
        Large = ReadLimits(config, "Large", new UploadPoolLimits(PerUser: 1, PerCompany: 2, Global: 3, RetryAfterSeconds: 30));
        _small = CreatePool(Small);
        _large = CreatePool(Large);
    }

    public UploadPoolLimits Small { get; }
    public UploadPoolLimits Large { get; }

    /// <summary>The largest body any route accepts, from the configured Build limits.</summary>
    public long LargestBodyBytes => Math.Max(Math.Max(_build.MaxModelBytes, _build.MaxCaptureBytes) + MultipartOverheadBytes, SmallBodyLimitBytes);

    /// <summary>What uploads can hold in the temp folder at once, whatever the callers send.</summary>
    public long TempCeilingBytes => Small.Global * SmallBodyLimitBytes + Large.Global * LargestBodyBytes;

    public void LogCeiling(ILogger logger)
    {
        if (!_enabled)
        {
            logger.LogWarning("[Uploads] In-flight limits are OFF (Uploads:InFlight:Enabled): temp-disk use has no ceiling.");
            return;
        }
        logger.LogInformation(
            "[Uploads] Temp-disk ceiling {CeilingMiB} MiB = {SmallGlobal} x {SmallMiB} MiB + {LargeGlobal} x {LargeMiB} MiB.",
            TempCeilingBytes / MiB, Small.Global, SmallBodyLimitBytes / MiB, Large.Global, LargestBodyBytes / MiB);
    }

    /// <summary>Reads the form of a signed-in caller. See <see cref="ReadAsync(HttpContext, UploadCaller, long, string, CancellationToken)"/>.</summary>
    public Task<UploadRead> ReadAsync(HttpContext http, long maxBodyBytes, string tooLargeMessage, CancellationToken ct)
        => ReadAsync(http, UploadCaller.Of(http.User), maxBodyBytes, tooLargeMessage, ct);

    /// <summary>Refuses or reads the form. Call it after every check that does not need the body: from here on, the body goes to disk.</summary>
    public async Task<UploadRead> ReadAsync(HttpContext http, UploadCaller caller, long maxBodyBytes, string tooLargeMessage, CancellationToken ct)
    {
        var request = http.Request;
        if (!IsMultipartWithBoundary(request.ContentType))
            return Refused(StatusCodes.Status400BadRequest, InvalidFormCode, "Envie o arquivo como multipart/form-data, no campo file.");

        if (request.ContentLength > maxBodyBytes)
            return TooLarge(tooLargeMessage);

        if (!TryEnter(http, caller, maxBodyBytes, out var retryAfter))
            return new UploadRead(null, new UploadRefusal(StatusCodes.Status429TooManyRequests, BusyCode,
                $"Há muitos envios em andamento agora. Aguarde {retryAfter} segundos e tente de novo.", retryAfter));

        // Counted here and not by the server's own limit, with or without a Content-Length: see UploadBodyMiddleware
        request.Body = new LimitedBody(request.Body, maxBodyBytes);

        try
        {
            var form = await request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = maxBodyBytes,
                // text fields stay in memory: far below the framework's defaults, which a large route would otherwise let fill it
                ValueCountLimit = 32,
                ValueLengthLimit = 64 * 1024,
                KeyLengthLimit = 256,
                MultipartHeadersLengthLimit = 16 * 1024,
            }, ct);
            return new UploadRead(form, null);
        }
        catch (InvalidDataException)
        {
            // the whole body, a section or a field beyond the limits above
            return TooLarge(tooLargeMessage);
        }
        catch (IOException) when (!http.RequestAborted.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // a truncated or malformed multipart body; a caller that left is not answered (see ExceptionHandlingMiddleware)
            return Refused(StatusCodes.Status400BadRequest, InvalidFormCode, "O envio chegou incompleto. Tente de novo.");
        }
    }

    public void Dispose()
    {
        _small.Dispose();
        _large.Dispose();
    }

    private bool TryEnter(HttpContext http, UploadCaller caller, long maxBodyBytes, out int retryAfterSeconds)
    {
        var expected = Math.Min(http.Request.ContentLength ?? maxBodyBytes, maxBodyBytes);
        var small = expected <= SmallBodyLimitBytes;
        retryAfterSeconds = small ? Small.RetryAfterSeconds : Large.RetryAfterSeconds;
        if (!_enabled) return true;

        var lease = (small ? _small : _large).AttemptAcquire(caller);
        if (!lease.IsAcquired)
        {
            lease.Dispose();
            return false;
        }
        // Completion callbacks run last-registered first, and the form registers its temp files after this line:
        // the slot comes back only once the files are gone, so a new upload never adds to a file on its way out.
        http.Response.RegisterForDispose(lease);
        return true;
    }

    /// <summary>Gives the reader at most <paramref name="limit"/> bytes, then fails as the form's own limits do.</summary>
    private sealed class LimitedBody(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => Counted(await inner.ReadAsync(buffer, ct));

        private int Counted(int read)
        {
            _read += read;
            if (_read > limit) throw new InvalidDataException($"Request body beyond its limit of {limit} bytes.");
            return read;
        }
    }

    private static bool IsMultipartWithBoundary(string? contentType)
        => MediaTypeHeaderValue.TryParse(contentType, out var parsed)
           && parsed.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
           && HeaderUtilities.RemoveQuotes(parsed.Boundary).Length > 0;

    private static UploadRead TooLarge(string message) => Refused(StatusCodes.Status413PayloadTooLarge, TooLargeCode, message);

    private static UploadRead Refused(int status, string code, string message) => new(null, new UploadRefusal(status, code, message));

    private static UploadPoolLimits ReadLimits(IConfiguration config, string pool, UploadPoolLimits defaults)
    {
        var section = config.GetSection($"Uploads:InFlight:{pool}");
        return new UploadPoolLimits(
            Math.Max(1, section.GetValue("PerUser", defaults.PerUser)),
            Math.Max(1, section.GetValue("PerCompany", defaults.PerCompany)),
            Math.Max(1, section.GetValue("Global", defaults.Global)),
            defaults.RetryAfterSeconds);
    }

    /// <summary>The caller's own bound, then the company's, then the global one; a refusal gives back what it had taken.</summary>
    private static PartitionedRateLimiter<UploadCaller> CreatePool(UploadPoolLimits limits)
        => PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<UploadCaller, string>(c => Slots(c.Company + "|" + c.Who, limits.PerUser)),
            PartitionedRateLimiter.Create<UploadCaller, string>(c => Slots(c.Company, limits.PerCompany)),
            PartitionedRateLimiter.Create<UploadCaller, string>(_ => Slots("global", limits.Global)));

    private static RateLimitPartition<string> Slots(string key, int permits)
        => RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = permits,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,   // a queue would only keep the sockets open: refuse, the client knows when to come back
        });
}

public static class UploadLimitsRegistration
{
    /// <summary>
    /// The in-flight bounds and the "uploads" rate policy: a budget per signed-in user of each company, so several terminals of one
    /// store behind one IP are not throttled together. Overridable via RateLimiting:Uploads:*, Uploads:InFlight:*.
    /// </summary>
    public static IServiceCollection AddUploadLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<UploadAdmission>();
        services.Configure<RateLimiterOptions>(options =>
        {
            var enabled = config.GetValue("RateLimiting:Uploads:Enabled", true);
            var permit = config.GetValue("RateLimiting:Uploads:PermitLimit", 60);
            var window = config.GetValue("RateLimiting:Uploads:WindowSeconds", 60);

            options.AddPolicy(UploadAdmission.RateLimitPolicy, http =>
            {
                var company = http.User.FindFirstValue("tenantId");
                var user = http.User.FindFirstValue("userId");
                // without a session the route answers 401 and reads nothing: no budget to spend, nobody else's to spend
                if (!enabled || company is null || user is null)
                    return RateLimitPartition.GetNoLimiter($"{UploadAdmission.RateLimitPolicy}:none");

                return RateLimitPartition.GetFixedWindowLimiter($"{UploadAdmission.RateLimitPolicy}|{company}|{user}", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit,
                        Window = TimeSpan.FromSeconds(window),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    });
            });
        });
        return services;
    }
}
