using System.Buffers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Filters;
using Nexo.Api.Security;

namespace Nexo.Api.Controllers;

/// <summary>
/// Where browsers send Content-Security-Policy violations (docs/security/csp.md). Anonymous by nature: a browser sends no
/// session with a report and ignores the answer. So the answer is always 204, whatever comes in, with no database and no more
/// than one log line per request. Only what <see cref="CspReportReader"/> reduces reaches the log.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/csp-reports")]
public class CspReportsController : ControllerBase
{
    private readonly ILogger<CspReportsController> _logger;

    public CspReportsController(ILogger<CspReportsController> logger) => _logger = logger;

    [HttpPost]
    [EnableRateLimiting(CspReportRateLimitPolicy.Name)]
    [DisableFormValueModelBinding]   // a form body would otherwise be read, and a broken one refused, before this action runs
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var reason = await ReadAndLogAsync(ct);
        if (reason is not null) _logger.LogInformation("CSP report ignored: {Reason}", reason);
        return NoContent();
    }

    /// <summary>Why nothing was logged as a violation, or null when something was.</summary>
    private async Task<string?> ReadAndLogAsync(CancellationToken ct)
    {
        var format = CspReportReader.FormatOf(Request.ContentType);
        if (format is null) return "media type";
        if (Request.ContentLength > CspReportReader.MaxBodyBytes) return "body too large";

        var buffer = ArrayPool<byte>.Shared.Rent(CspReportReader.MaxBodyBytes + 1);
        try
        {
            var length = await ReadBoundedAsync(buffer, ct);
            if (length > CspReportReader.MaxBodyBytes) return "body too large";

            var report = CspReportReader.Read(buffer.AsMemory(0, length), format.Value);
            if (report.Total == 0) return "no violation in the body";

            _logger.LogWarning("CSP report: {ViolationCount} violation(s) {@Violations}", report.Total, report.Violations);
            return null;
        }
        catch (IOException)
        {
            return "body not received";   // the browser went away: the report is lost, and that is fine
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads up to one byte past the limit and stops there, so a large body costs no more than a small one.</summary>
    private async Task<int> ReadBoundedAsync(byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total <= CspReportReader.MaxBodyBytes)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(total, CspReportReader.MaxBodyBytes + 1 - total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
