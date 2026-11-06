using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Nexo.Api.Observability;

/// <summary>
/// A browser cannot send headers on a WebSocket, so the access token of the hub travels in the query string of the request. The
/// framework's "Request starting" line has that query string: today its level is filtered in appsettings (Microsoft: Warning), and
/// this keeps the token out of the log when someone raises the level to investigate something.
/// </summary>
public sealed class AccessTokenLogRedactor : ILogEventEnricher
{
    private const string Parameter = "access_token=";

    private static readonly Regex TokenInQuery = new("access_token=[^&\\s\"']*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? redacted = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is ScalarValue { Value: string or Uri } scalar
                && scalar.Value.ToString() is { } text
                && text.Contains(Parameter, StringComparison.OrdinalIgnoreCase))
            {
                (redacted ??= []).Add(new LogEventProperty(name, new ScalarValue(TokenInQuery.Replace(text, Parameter + "[redacted]"))));
            }
        }
        redacted?.ForEach(logEvent.AddOrUpdateProperty);
    }
}
