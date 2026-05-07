using FluentValidation;
using Nexo.Application.Modules.Interpreter;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Validators.Interpreter;

public class ReprocessMovementRequestValidator : AbstractValidator<ReprocessMovementRequest>
{
    private static readonly string[] ValidReasons   = Enum.GetNames<TriggerReason>();
    private static readonly string[] ValidProviders = Enum.GetNames<AnalyzerProvider>();

    public ReprocessMovementRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Must(v => ValidReasons.Contains(v))
            .WithMessage($"Reason must be one of: {string.Join(", ", ValidReasons)}.");

        When(x => x.ForceAnalyzer is not null, () =>
        {
            RuleFor(x => x.ForceAnalyzer)
                .Must(v => ValidProviders.Contains(v!))
                .WithMessage($"ForceAnalyzer must be one of: {string.Join(", ", ValidProviders)}.");
        });

        When(x => x.Notes is not null, () =>
        {
            RuleFor(x => x.Notes)
                .MaximumLength(500).WithMessage("Notes must not exceed 500 characters.");
        });
    }
}
