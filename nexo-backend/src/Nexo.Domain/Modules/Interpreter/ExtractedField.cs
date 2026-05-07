namespace Nexo.Domain.Modules.Interpreter;

// Immutable value object representing one extracted field from a document or text.
// Backend exclusively computes Status from Confidence — frontend only renders.
public sealed record ExtractedField<T>(
    T?               Value,
    float            Confidence,
    FieldStatus      Status,
    AnalyzerProvider Provider)
{
    public static ExtractedField<T> From(T? value, float confidence, AnalyzerProvider provider)
    {
        var status = confidence switch
        {
            >= 0.90f => FieldStatus.AutoFilled,
            >= 0.60f => FieldStatus.NeedsAttention,
            _        => FieldStatus.RequiresInput
        };

        return new ExtractedField<T>(value, confidence, status, provider);
    }

    public static ExtractedField<T> Unknown() =>
        new(default, 0f, FieldStatus.RequiresInput, AnalyzerProvider.RuleBased);
}
