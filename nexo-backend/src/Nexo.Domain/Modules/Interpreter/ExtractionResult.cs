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
    public string LlmRawResponse { get; private set; } = string.Empty;

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
            LlmRawResponse     = output.RawProviderResponse
        };
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
