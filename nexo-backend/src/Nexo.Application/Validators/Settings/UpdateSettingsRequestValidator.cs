using FluentValidation;
using Nexo.Application.Features.Settings;

namespace Nexo.Application.Validators.Settings;

public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    private static readonly string[] ValidMinStockBehaviors = ["alert", "block", "ignore"];

    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Company).NotNull().WithMessage("Company settings are required.");
        RuleFor(x => x.Operation).NotNull().WithMessage("Operation settings are required.");
        RuleFor(x => x.Inventory).NotNull().WithMessage("Inventory settings are required.");
        RuleFor(x => x.Commission).NotNull().WithMessage("Commission settings are required.");
        RuleFor(x => x.Pos).NotNull().WithMessage("POS settings are required.");
        RuleFor(x => x.System).NotNull().WithMessage("System settings are required.");

        // Company.Name validation is intentionally omitted here:
        // updateSettings always sends a full merged payload (all sections), so any
        // section save would fail if Company.Name happens to be empty in the database.
        // Name formatting is enforced in the frontend settings form instead.

        When(x => x.Inventory is not null, () =>
        {
            RuleFor(x => x.Inventory.NoMovementAlertDays)
                .GreaterThan(0).WithMessage("No-movement alert days must be greater than 0.");

            RuleFor(x => x.Inventory.MinStockBehavior)
                .Must(b => ValidMinStockBehaviors.Contains(b?.ToLowerInvariant()))
                .WithMessage($"MinStockBehavior must be one of: {string.Join(", ", ValidMinStockBehaviors)}.");
        });

        When(x => x.Commission is not null, () =>
        {
            RuleFor(x => x.Commission.DefaultCommissionRate)
                .InclusiveBetween(0, 100)
                .WithMessage("Commission rate must be between 0 and 100.");
        });

        When(x => x.Pos is not null, () =>
        {
            RuleFor(x => x.Pos.MaxDiscountPercent)
                .InclusiveBetween(0, 100)
                .WithMessage("Max discount percent must be between 0 and 100.");
        });
    }
}
