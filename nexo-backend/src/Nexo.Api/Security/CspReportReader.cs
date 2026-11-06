using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Nexo.Api.Security;

public enum CspReportFormat
{
    /// <summary><c>report-uri</c>: one object, <c>{ "csp-report": { ... } }</c>.</summary>
    ReportUri,
    /// <summary>Reporting API (<c>report-to</c>): an array of reports of any type.</summary>
    ReportingApi,
}

/// <summary>What is written to the log about one violation. Nothing else from a report is kept.</summary>
public sealed record CspViolationLogEntry(string EffectiveDirective, string BlockedOrigin, string Document, string SourceFile, int? Line, string Disposition);

public sealed record CspReportSummary(int Total, IReadOnlyList<CspViolationLogEntry> Violations);

/// <summary>
/// Turns a violation report into what may be logged. Anyone can post anything here, and a real report carries signed URLs,
/// tokens (<c>/rastrear/{token}</c>, <c>/agendar/{slug}/reserva/{token}</c>, <c>?token=</c>) and a sample of the page's content.
/// So the rule is to keep little rather than to remove what looks sensitive: of a blocked address, its origin; of the page, its
/// origin and first path segment; never the sample, the referrer, the policy or the user agent.
/// </summary>
public static class CspReportReader
{
    /// <summary>Largest body read. A report is a few KB, even with long URLs.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>A batch logs this many distinct violations at most, so one log line stays one log line.</summary>
    public const int MaxViolationsLogged = 20;

    private const int MaxFieldLength = 200;
    private const int MaxTokenLength = 40;
    private const int MaxSegmentLength = 64;
    private const int MaxLine = 10_000_000;
    private const string AssetsPrefix = "/assets/";

    private static readonly JsonDocumentOptions Json = new() { MaxDepth = 8 };

    public static CspReportFormat? FormatOf(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed)) return null;
        if (parsed.MediaType.Equals("application/csp-report", StringComparison.OrdinalIgnoreCase)) return CspReportFormat.ReportUri;
        if (parsed.MediaType.Equals("application/reports+json", StringComparison.OrdinalIgnoreCase)) return CspReportFormat.ReportingApi;
        return null;
    }

    public static CspReportSummary Read(ReadOnlyMemory<byte> body, CspReportFormat format)
    {
        var found = new List<CspViolationLogEntry>();
        try
        {
            using var document = JsonDocument.Parse(body, Json);
            if (format == CspReportFormat.ReportUri) ReadReportUri(document.RootElement, found);
            else ReadReportingApi(document.RootElement, found);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // not a report, or a string that is not text: nothing to log
        }
        return new CspReportSummary(found.Count, found.Distinct().Take(MaxViolationsLogged).ToList());
    }

    private static void ReadReportUri(JsonElement root, List<CspViolationLogEntry> found)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var report) && report.ValueKind == JsonValueKind.Object)
            Add(found, Text(report, "effective-directive") ?? Text(report, "violated-directive"), Text(report, "blocked-uri"),
                Text(report, "document-uri"), Text(report, "source-file"), report, "line-number", Text(report, "disposition"));
    }

    private static void ReadReportingApi(JsonElement root, List<CspViolationLogEntry> found)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "csp-violation") continue;
            if (!item.TryGetProperty("body", out var report) || report.ValueKind != JsonValueKind.Object) continue;
            Add(found, Text(report, "effectiveDirective"), Text(report, "blockedURL"), Text(report, "documentURL"),
                Text(report, "sourceFile"), report, "lineNumber", Text(report, "disposition"));
        }
    }

    private static void Add(List<CspViolationLogEntry> found, string? directive, string? blocked, string? document, string? source,
        JsonElement report, string lineField, string? disposition)
    {
        var effective = Token(directive);
        if (effective.Length == 0) return;   // a report with no directive says nothing about the policy
        found.Add(new CspViolationLogEntry(effective, OriginOf(blocked), DocumentOf(document), SourceFileOf(source), Line(report, lineField), Token(disposition)));
    }

    private static string? Text(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Line(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var line) && line is > 0 and <= MaxLine
            ? line
            : null;

    /// <summary>The leading word of a directive or disposition (an old browser sends "img-src https:"): nothing else survives.</summary>
    private static string Token(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var token = new StringBuilder(MaxTokenLength);
        foreach (var c in value)
        {
            if (token.Length == MaxTokenLength || !(char.IsAsciiLetterOrDigit(c) || c == '-')) break;
            token.Append(char.ToLowerInvariant(c));
        }
        return token.ToString();
    }

    /// <summary>A web address becomes its origin; any other scheme, only the scheme; a browser's keyword (inline, eval) stays.</summary>
    private static string OriginOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return Token(value);
        return IsWeb(uri) ? Origin(uri) : Token(uri.Scheme);
    }

    /// <summary>The page, down to its origin and first segment: what follows may be a token or the name of a client's project.</summary>
    private static string DocumentOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return Token(value);
        if (!IsWeb(uri)) return Token(uri.Scheme);

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return Capped(Origin(uri) + "/");
        return Capped($"{Origin(uri)}/{Segment(segments[0])}{(segments.Length > 1 ? "/*" : string.Empty)}");
    }

    /// <summary>The script that caused it: of the app's build files the name, which says nothing about anyone; of others, the origin.</summary>
    private static string SourceFileOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return Token(value);
        if (!IsWeb(uri)) return Token(uri.Scheme);

        var path = uri.AbsolutePath;
        return path.StartsWith(AssetsPrefix, StringComparison.Ordinal)
            ? Capped($"{Origin(uri)}{AssetsPrefix}{Segment(path[AssetsPrefix.Length..])}")
            : Origin(uri);
    }

    private static bool IsWeb(Uri uri) => uri.Scheme is "http" or "https" or "ws" or "wss";

    private static string Origin(Uri uri)
        => Capped($"{uri.Scheme}://{uri.IdnHost}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}");

    private static string Segment(string value)
    {
        var segment = new StringBuilder(MaxSegmentLength);
        foreach (var c in value)
        {
            if (segment.Length == MaxSegmentLength) break;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') segment.Append(c);
        }
        return segment.ToString();
    }

    private static string Capped(string value) => value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];
}
