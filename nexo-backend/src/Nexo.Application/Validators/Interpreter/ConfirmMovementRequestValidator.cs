using FluentValidation;
using Nexo.Application.Modules.Interpreter;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Validators.Interpreter;

public class ConfirmMovementRequestValidator : AbstractValidator<ConfirmMovementRequest>
{
    private static readonly string[] ValidDirections  = Enum.GetNames<MovementDirection>();
    private static readonly string[] ValidNatures     = Enum.GetNames<MovementNature>();
    private static readonly string[] ValidContextTypes = Enum.GetNames<FinancialContextType>();

    public ConfirmMovementRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Date)
            .NotEmpty().WithMessage("Date is required.")
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date cannot be in the future.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");

        RuleFor(x => x.Direction)
            .Must(v => ValidDirections.Contains(v))
            .WithMessage($"Direction must be one of: {string.Join(", ", ValidDirections)}.");

        RuleFor(x => x.Nature)
            .Must(v => ValidNatures.Contains(v))
            .WithMessage($"Nature must be one of: {string.Join(", ", ValidNatures)}.");

        RuleFor(x => x.ContextType)
            .Must(v => ValidContextTypes.Contains(v))
            .WithMessage($"ContextType must be one of: {string.Join(", ", ValidContextTypes)}.");

        RuleFor(x => x.OriginalSuggestionId)
            .NotEqual(Guid.Empty).WithMessage("OriginalSuggestionId is required.");
    }
}
