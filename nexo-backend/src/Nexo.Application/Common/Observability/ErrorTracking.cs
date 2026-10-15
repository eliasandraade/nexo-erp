using System.Security.Cryptography;
using System.Text;

namespace Nexo.Application.Common.Observability;

/// <summary>An unexpected error as the tracker stores it: no body, no headers, no personal data.</summary>
public record ErrorOccurrence(Exception Exception, string Route, string Method, string? RequestId, Guid? TenantId);

/// <summary>Records unexpected errors (HTTP 500) grouped by kind. Never throws.</summary>
public interface IErrorTracker
{
    Task TrackAsync(ErrorOccurrence occurrence, CancellationToken ct = default);
}

/// <summary>Pure helpers: grouping key, masked summary and the top frames of a stack.</summary>
public static class ErrorFingerprint
{
    private const int MaxSummary = 500;
    private const int MaxStack = 2000;
    private const int FramesInKey = 3;

    /// <summary>Same exception type, same route, same top frames of our code → same kind of error.</summary>
    public static string Compute(Exception ex, string route)
    {
        var root = Root(ex);
        var frames = Frames(root).Where(f => f.Contains(" Nexo.", StringComparison.Ordinal)).Take(FramesInKey);
        var key = $"{root.GetType().FullName}|{route}|{string.Join('|', frames.Select(StripLocation))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    /// <summary>
    /// "Type: message" with anything that may be personal masked: words with '@' (e-mails), words
    /// with 6+ digits (formatted documents and phones), runs of 4+ digits and quoted values. Database errors keep only their kind —
    /// their messages quote the row's values.
    /// </summary>
    public static string Summarize(Exception ex)
    {
        var root = Root(ex);
        var type = root.GetType().Name;
        if (IsDatabaseError(root)) return type + ": database error";
        var masked = Mask(root.Message);
        var text = $"{type}: {masked}";
        return text.Length <= MaxSummary ? text : text[..MaxSummary];
    }

    /// <summary>The first frames of the stack (method names and line numbers only — never values).</summary>
    public static string StackTop(Exception ex)
    {
        var text = string.Join('\n', Frames(Root(ex)).Take(12));
        return text.Length <= MaxStack ? text : text[..MaxStack];
    }

    public static string Mask(string message)
    {
        var sb = new StringBuilder(message.Length);
        foreach (var word in message.Split(' '))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(MaskWord(word));
        }
        return sb.ToString();
    }

    private static string MaskWord(string word)
    {
        if (word.Contains('@')) return "***";
        // a formatted document or phone ("529.982.247-25", "(11) 98765-4321") is many short runs
        if (word.Count(char.IsAsciiDigit) >= 6) return "####";
        if (word.Length >= 2 && word[0] is '\'' or '"') return "'***'";
        var sb = new StringBuilder(word.Length);
        var digits = new StringBuilder();
        foreach (var c in word)
        {
            if (char.IsAsciiDigit(c)) { digits.Append(c); continue; }
            Flush(sb, digits);
            sb.Append(c);
        }
        Flush(sb, digits);
        return sb.ToString();

        // short numbers (counts, small ids) stay; 4+ digits may be a document or phone
        static void Flush(StringBuilder sb, StringBuilder digits)
        {
            if (digits.Length == 0) return;
            sb.Append(digits.Length >= 4 ? "####" : digits.ToString());
            digits.Clear();
        }
    }

    private static Exception Root(Exception ex)
    {
        var e = ex;
        while (e is AggregateException { InnerExceptions.Count: 1 } agg) e = agg.InnerExceptions[0];
        return e;
    }

    private static bool IsDatabaseError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var ns = e.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("Npgsql", StringComparison.Ordinal) || ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> Frames(Exception ex)
        => (ex.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string StripLocation(string frame)
    {
        var i = frame.IndexOf(" in ", StringComparison.Ordinal);
        return i < 0 ? frame : frame[..i];
    }
}
