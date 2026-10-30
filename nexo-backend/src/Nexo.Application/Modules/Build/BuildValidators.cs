using FluentValidation;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ── Projects ──────────────────────────────────────────────────────────────────

public class CreateBuildProjectRequestValidator : AbstractValidator<CreateBuildProjectRequest>
{
    public CreateBuildProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome da obra.")
            .MaximumLength(200).WithMessage("O nome da obra pode ter até 200 caracteres.");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("Informe o cliente.")
            .MaximumLength(200).WithMessage("O nome do cliente pode ter até 200 caracteres.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Informe o tipo da obra.")
            .Must(t => Enum.TryParse<BuildProjectType>(t, ignoreCase: true, out _))
            .WithMessage("Tipo de obra inválido.");

        RuleFor(x => x.Location)
            .MaximumLength(300).WithMessage("O endereço pode ter até 300 caracteres.")
            .When(x => x.Location is not null);

        RuleFor(x => x.BudgetEstimated)
            .GreaterThanOrEqualTo(0m).WithMessage("O orçamento estimado não pode ser negativo.")
            .When(x => x.BudgetEstimated.HasValue);

        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.ExpectedEndDate is null || x.StartDate <= x.ExpectedEndDate)
            .WithMessage("A previsão de término precisa ser depois do início.");

        BuildSiteRules.Apply(this, x => x.TrackingMode, x => x.Latitude, x => x.Longitude, x => x.AreaM2);
    }
}

public class UpdateBuildProjectRequestValidator : AbstractValidator<UpdateBuildProjectRequest>
{
    public UpdateBuildProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome da obra.")
            .MaximumLength(200).WithMessage("O nome da obra pode ter até 200 caracteres.");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("Informe o cliente.")
            .MaximumLength(200).WithMessage("O nome do cliente pode ter até 200 caracteres.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Informe o tipo da obra.")
            .Must(t => Enum.TryParse<BuildProjectType>(t, ignoreCase: true, out _))
            .WithMessage("Tipo de obra inválido.");

        RuleFor(x => x.Location)
            .MaximumLength(300).WithMessage("O endereço pode ter até 300 caracteres.")
            .When(x => x.Location is not null);

        RuleFor(x => x.BudgetEstimated)
            .GreaterThanOrEqualTo(0m).WithMessage("O orçamento estimado não pode ser negativo.")
            .When(x => x.BudgetEstimated.HasValue);

        RuleFor(x => x.BudgetApproved)
            .GreaterThanOrEqualTo(0m).WithMessage("O orçamento aprovado não pode ser negativo.")
            .When(x => x.BudgetApproved.HasValue);

        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.ExpectedEndDate is null || x.StartDate <= x.ExpectedEndDate)
            .WithMessage("A previsão de término precisa ser depois do início.");

        BuildSiteRules.Apply(this, x => x.TrackingMode, x => x.Latitude, x => x.Longitude, x => x.AreaM2);
    }
}

// ── Stages ────────────────────────────────────────────────────────────────────

public class CreateBuildStageRequestValidator : AbstractValidator<CreateBuildStageRequest>
{
    public CreateBuildStageRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome da etapa.")
            .MaximumLength(200).WithMessage("O nome da etapa pode ter até 200 caracteres.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("A descrição pode ter até 1000 caracteres.")
            .When(x => x.Description is not null);

        RuleFor(x => x)
            .Must(x => x.PlannedStart is null || x.PlannedEnd is null || x.PlannedStart <= x.PlannedEnd)
            .WithMessage("O fim previsto precisa ser depois do início previsto.");

        RuleFor(x => x.Weight).GreaterThan(0m).When(x => x.Weight.HasValue)
            .WithMessage("O peso da etapa precisa ser maior que zero.");
    }
}

// ── Budgets ───────────────────────────────────────────────────────────────────

public class CreateBuildBudgetRequestValidator : AbstractValidator<CreateBuildBudgetRequest>
{
    public CreateBuildBudgetRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome do orçamento.")
            .MaximumLength(200).WithMessage("O nome do orçamento pode ter até 200 caracteres.");

        RuleFor(x => x.MarginPercent)
            .GreaterThanOrEqualTo(0m).WithMessage("A margem não pode ser negativa.");
    }
}

public class AddBuildBudgetItemRequestValidator : AbstractValidator<AddBuildBudgetItemRequest>
{
    public AddBuildBudgetItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome do item.")
            .MaximumLength(300).WithMessage("O nome do item pode ter até 300 caracteres.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Informe a categoria.")
            .MaximumLength(100).WithMessage("A categoria pode ter até 100 caracteres.");

        RuleFor(x => x.Unit)
            .NotEmpty().WithMessage("Informe a unidade.")
            .MaximumLength(20).WithMessage("A unidade pode ter até 20 caracteres.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m).WithMessage("A quantidade precisa ser maior que zero.");

        RuleFor(x => x.UnitCost)
            .GreaterThanOrEqualTo(0m).WithMessage("O custo unitário não pode ser negativo.");
    }
}

// ── Daily Logs ────────────────────────────────────────────────────────────────

public class CreateDailyLogRequestValidator : AbstractValidator<CreateDailyLogRequest>
{
    public CreateDailyLogRequestValidator()
    {
        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage("Escreva o que aconteceu no dia.")
            .MaximumLength(5000).WithMessage("O registro pode ter até 5000 caracteres.");

        RuleFor(x => x.WeatherSummary)
            .MaximumLength(200).WithMessage("O clima pode ter até 200 caracteres.")
            .When(x => x.WeatherSummary is not null);

        RuleFor(x => x.Date)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("O diário não pode ser de mais de um dia no futuro.");
    }
}

public class UpdateBuildStageRequestValidator : AbstractValidator<UpdateBuildStageRequest>
{
    public UpdateBuildStageRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome da etapa.")
            .MaximumLength(200).WithMessage("O nome da etapa pode ter até 200 caracteres.");
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("A descrição pode ter até 1000 caracteres.")
            .When(x => x.Description is not null);
        RuleFor(x => x.Order).GreaterThanOrEqualTo(0).WithMessage("Ordem inválida.");
        RuleFor(x => x.Weight).GreaterThan(0m).When(x => x.Weight.HasValue)
            .WithMessage("O peso da etapa precisa ser maior que zero.");
        RuleFor(x => x)
            .Must(x => x.PlannedStart is null || x.PlannedEnd is null || x.PlannedStart <= x.PlannedEnd)
            .WithMessage("O fim previsto precisa ser depois do início previsto.");
    }
}

public class ReorderBuildStagesRequestValidator : AbstractValidator<ReorderBuildStagesRequest>
{
    public ReorderBuildStagesRequestValidator()
    {
        RuleFor(x => x.Entries).NotNull().WithMessage("Informe a nova ordem das etapas.");
    }
}

public class UpdateBuildBudgetItemRequestValidator : AbstractValidator<UpdateBuildBudgetItemRequest>
{
    public UpdateBuildBudgetItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do item.")
            .MaximumLength(300).WithMessage("O nome do item pode ter até 300 caracteres.");
        RuleFor(x => x.Category).NotEmpty().WithMessage("Informe a categoria.")
            .MaximumLength(100).WithMessage("A categoria pode ter até 100 caracteres.");
        RuleFor(x => x.Unit).NotEmpty().WithMessage("Informe a unidade.")
            .MaximumLength(20).WithMessage("A unidade pode ter até 20 caracteres.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("A quantidade precisa ser maior que zero.");
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0m).WithMessage("O custo unitário não pode ser negativo.");
    }
}

public class SetBudgetMarginRequestValidator : AbstractValidator<SetBudgetMarginRequest>
{
    public SetBudgetMarginRequestValidator()
    {
        RuleFor(x => x.MarginPercent).GreaterThanOrEqualTo(0m).WithMessage("A margem não pode ser negativa.");
    }
}

public class UpdateDailyLogRequestValidator : AbstractValidator<UpdateDailyLogRequest>
{
    public UpdateDailyLogRequestValidator()
    {
        RuleFor(x => x.Notes).NotEmpty().WithMessage("Escreva o que aconteceu no dia.")
            .MaximumLength(5000).WithMessage("O registro pode ter até 5000 caracteres.");
        RuleFor(x => x.WeatherSummary).MaximumLength(200).WithMessage("O clima pode ter até 200 caracteres.")
            .When(x => x.WeatherSummary is not null);
    }
}
