using FluentValidation;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ── Projects ──────────────────────────────────────────────────────────────────

public class CreateBuildProjectRequestValidator : AbstractValidator<CreateBuildProjectRequest>
{
    public CreateBuildProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must not exceed 200 characters.");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("Client name is required.")
            .MaximumLength(200).WithMessage("Client name must not exceed 200 characters.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Project type is required.")
            .Must(t => Enum.TryParse<BuildProjectType>(t, ignoreCase: true, out _))
            .WithMessage($"Invalid project type. Valid values: {string.Join(", ", Enum.GetNames<BuildProjectType>())}.");

        RuleFor(x => x.Location)
            .MaximumLength(300).WithMessage("Location must not exceed 300 characters.")
            .When(x => x.Location is not null);

        RuleFor(x => x.BudgetEstimated)
            .GreaterThanOrEqualTo(0m).WithMessage("Budget estimated cannot be negative.")
            .When(x => x.BudgetEstimated.HasValue);

        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.ExpectedEndDate is null || x.StartDate <= x.ExpectedEndDate)
            .WithMessage("Expected end date must be on or after start date.");
    }
}

public class UpdateBuildProjectRequestValidator : AbstractValidator<UpdateBuildProjectRequest>
{
    public UpdateBuildProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must not exceed 200 characters.");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("Client name is required.")
            .MaximumLength(200).WithMessage("Client name must not exceed 200 characters.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Project type is required.")
            .Must(t => Enum.TryParse<BuildProjectType>(t, ignoreCase: true, out _))
            .WithMessage($"Invalid project type. Valid values: {string.Join(", ", Enum.GetNames<BuildProjectType>())}.");

        RuleFor(x => x.Location)
            .MaximumLength(300).WithMessage("Location must not exceed 300 characters.")
            .When(x => x.Location is not null);

        RuleFor(x => x.BudgetEstimated)
            .GreaterThanOrEqualTo(0m).WithMessage("Budget estimated cannot be negative.")
            .When(x => x.BudgetEstimated.HasValue);

        RuleFor(x => x.BudgetApproved)
            .GreaterThanOrEqualTo(0m).WithMessage("Budget approved cannot be negative.")
            .When(x => x.BudgetApproved.HasValue);

        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.ExpectedEndDate is null || x.StartDate <= x.ExpectedEndDate)
            .WithMessage("Expected end date must be on or after start date.");
    }
}

// ── Stages ────────────────────────────────────────────────────────────────────

public class CreateBuildStageRequestValidator : AbstractValidator<CreateBuildStageRequest>
{
    public CreateBuildStageRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Stage name is required.")
            .MaximumLength(200).WithMessage("Stage name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x)
            .Must(x => x.PlannedStart is null || x.PlannedEnd is null || x.PlannedStart <= x.PlannedEnd)
            .WithMessage("Planned end date must be on or after planned start date.");
    }
}

// ── Budgets ───────────────────────────────────────────────────────────────────

public class CreateBuildBudgetRequestValidator : AbstractValidator<CreateBuildBudgetRequest>
{
    public CreateBuildBudgetRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Budget name is required.")
            .MaximumLength(200).WithMessage("Budget name must not exceed 200 characters.");

        RuleFor(x => x.MarginPercent)
            .GreaterThanOrEqualTo(0m).WithMessage("Margin percent cannot be negative.");
    }
}

public class AddBuildBudgetItemRequestValidator : AbstractValidator<AddBuildBudgetItemRequest>
{
    public AddBuildBudgetItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Item name is required.")
            .MaximumLength(300).WithMessage("Item name must not exceed 300 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .MaximumLength(100).WithMessage("Category must not exceed 100 characters.");

        RuleFor(x => x.Unit)
            .NotEmpty().WithMessage("Unit is required.")
            .MaximumLength(20).WithMessage("Unit must not exceed 20 characters.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m).WithMessage("Quantity must be greater than zero.");

        RuleFor(x => x.UnitCost)
            .GreaterThanOrEqualTo(0m).WithMessage("Unit cost cannot be negative.");
    }
}

// ── Daily Logs ────────────────────────────────────────────────────────────────

public class CreateDailyLogRequestValidator : AbstractValidator<CreateDailyLogRequest>
{
    public CreateDailyLogRequestValidator()
    {
        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("Notes are required.")
            .MaximumLength(5000).WithMessage("Notes must not exceed 5000 characters.");

        RuleFor(x => x.WeatherSummary)
            .MaximumLength(200).WithMessage("Weather summary must not exceed 200 characters.")
            .When(x => x.WeatherSummary is not null);

        RuleFor(x => x.Date)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("Daily log date cannot be more than 1 day in the future.");
    }
}
