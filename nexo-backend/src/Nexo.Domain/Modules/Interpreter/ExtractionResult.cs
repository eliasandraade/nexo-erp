using System.Text.Json;
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

public class ExtractionResult : TenantEntity
{
    private ExtractionResult() { }
    private ExtractionResult(Guid tenantId) : base(tenantId) { }

    public Guid            MovementId          { get; private set; }
    public InputSourceType InputSource         { get; private set; }
    public string?         RawUserText         { get; private set; }

    // Per-field extraction with individual confidence + status.
    // Stored as flat columns for queryability; value objects used in application layer.
    public decimal? DetectedAmount    { get; private set; }
    public float    AmountConfidence  { get; private set; }
    public FieldStatus AmountStatus   { get; private set; }

    public DateOnly? DetectedDate     { get; private set; }
    public float     DateConfidence   { get; private set; }
    public FieldStatus DateStatus     { get; private set; }

    public string?   DetectedPayee    { get; private set; }
    public float     PayeeConfidence  { get; private set; }
    public FieldStatus PayeeStatus    { get; private set; }

    public string?   DetectedAccount  { get; private set; }
    public float     AccountConfidence { get; private set; }
    public FieldStatus AccountStatus  { get; private set; }

    public AnalyzerProvider AnalyzerProvider  { get; private set; }

    // PromptType stays string for now — will become enum when types stabilize.
    public string PromptType    { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string PromptHash    { get; private set; } = string.Empty;

    // Raw provider payload — observable artifact only, never a business contract.
    // Stored in a jsonb column, so it must always be valid JSON ("{}" when there is no
    // provider payload, e.g. the rule-based analyzer). An empty string is NOT valid JSON
    // and makes Postgres reject the insert (22P02).
    public string LlmRawResponse { get; private set; } = "{}";

    public static ExtractionResult Create(
        Guid            tenantId,
        Guid            movementId,
        InputSourceType inputSource,
        string?         rawUserText,
        AnalysisOutput  output)
    {
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");

        return new ExtractionResult(tenantId)
        {
            MovementId         = movementId,
            InputSource        = inputSource,
            RawUserText        = rawUserText,

            DetectedAmount     = output.Amount.Value,
            AmountConfidence   = output.Amount.Confidence,
            AmountStatus       = output.Amount.Status,

            DetectedDate       = output.Date.Value,
            DateConfidence     = output.Date.Confidence,
            DateStatus         = output.Date.Status,

            DetectedPayee      = output.Payee.Value,
            PayeeConfidence    = output.Payee.Confidence,
            PayeeStatus        = output.Payee.Status,

            DetectedAccount    = output.Account.Value,
            AccountConfidence  = output.Account.Confidence,
            AccountStatus      = output.Account.Status,

            AnalyzerProvider   = output.Prompt.PromptType == string.Empty
                                     ? AnalyzerProvider.RuleBased
                                     : AnalyzerProvider.Claude,
            PromptType         = output.Prompt.PromptType,
            PromptVersion      = output.Prompt.PromptVersion,
            PromptHash         = output.Prompt.PromptHash,
            // jsonb column — must always be valid JSON. The rule-based analyzer puts the
            // raw user text here (not JSON), so sanitize regardless of provider.
            LlmRawResponse     = ToStorableJson(output.RawProviderResponse)
        };
    }

    /// <summary>
    /// Guarantees a value safe for the jsonb column. Valid JSON passes through unchanged
    /// (LLM payloads); anything else (rule-based raw text, plain strings) is JSON-encoded
    /// so it remains observable without breaking the insert. Empty → "{}".
    /// </summary>
    private static string ToStorableJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        try
        {
            using var _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(raw);
        }
    }

    public ExtractedField<decimal?>  AmountField  =>
        ExtractedField<decimal?>.From(DetectedAmount, AmountConfidence, AnalyzerProvider);

    public ExtractedField<DateOnly?> DateField    =>
        ExtractedField<DateOnly?>.From(DetectedDate, DateConfidence, AnalyzerProvider);

    public ExtractedField<string?>  PayeeField   =>
        ExtractedField<string?>.From(DetectedPayee, PayeeConfidence, AnalyzerProvider);

    public ExtractedField<string?>  AccountField =>
        ExtractedField<string?>.From(DetectedAccount, AccountConfidence, AnalyzerProvider);
}
