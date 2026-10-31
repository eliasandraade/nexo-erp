using FluentValidation;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// Request shape checks of Wave 2 (400). Business rules (status, expiry, deadlines…) stay in the domain (422/409).

internal static class BuildEnumRules
{
    public static bool Valid<T>(string? value) where T : struct, Enum
        => value is null || (Enum.TryParse<T>(value, ignoreCase: true, out var v) && Enum.IsDefined(v));
}

public class SetBuildBudgetTermsRequestValidator : AbstractValidator<SetBuildBudgetTermsRequest>
{
    public SetBuildBudgetTermsRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(4000).WithMessage("As observações podem ter até 4000 caracteres.");
        RuleFor(x => x.InternalNotes).MaximumLength(4000).WithMessage("As observações internas podem ter até 4000 caracteres.");
        RuleFor(x => x.AreaM2).GreaterThanOrEqualTo(0m).When(x => x.AreaM2.HasValue).WithMessage("A área não pode ser negativa.");
    }
}

public class SaveBuildBudgetTemplateRequestValidator : AbstractValidator<SaveBuildBudgetTemplateRequest>
{
    public SaveBuildBudgetTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Informe o nome do modelo (até 200 caracteres).");
        RuleFor(x => x.DefaultMarginPercent).InclusiveBetween(0m, 1000m).WithMessage("A margem precisa ficar entre 0 e 1000%.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= 500).WithMessage("Um modelo pode ter até 500 itens.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Name).NotEmpty().MaximumLength(300).WithMessage("Informe o nome do item.");
            i.RuleFor(x => x.Unit).NotEmpty().MaximumLength(20).WithMessage("Informe a unidade do item.");
            i.RuleFor(x => x.Kind).Must(BuildEnumRules.Valid<BuildCostKind>).WithMessage("Tipo de item inválido.");
            i.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0m).WithMessage("O custo unitário não pode ser negativo.");
            i.RuleFor(x => x).Must(x => x.QuantityPerM2.HasValue != x.FixedQuantity.HasValue)
                .WithMessage("Cada item precisa de quantidade por m² ou quantidade fixa (uma das duas).");
        });
    }
}

public class CreateBudgetFromTemplateRequestValidator : AbstractValidator<CreateBudgetFromTemplateRequest>
{
    public CreateBudgetFromTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Informe o nome do orçamento.");
        RuleFor(x => x.AreaM2).GreaterThan(0m).When(x => x.AreaM2.HasValue).WithMessage("A área precisa ser maior que zero.");
        RuleFor(x => x.MarginPercent).GreaterThanOrEqualTo(0m).When(x => x.MarginPercent.HasValue)
            .WithMessage("A margem não pode ser negativa.");
    }
}

public class SaveBuildChangeOrderRequestValidator : AbstractValidator<SaveBuildChangeOrderRequest>
{
    public SaveBuildChangeOrderRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título do aditivo (até 200 caracteres).");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("A descrição pode ter até 4000 caracteres.");
        RuleFor(x => x.Reason).Must(BuildEnumRules.Valid<BuildChangeOrderReason>)
            .WithMessage("Motivo inválido: use ClientRequest, DesignChange, SiteCondition, Error ou Other.");
        RuleFor(x => x.ScheduleImpactDays).InclusiveBetween(-3650, 3650).WithMessage("Impacto no prazo fora do limite.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= 200).WithMessage("Um aditivo pode ter até 200 itens.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Name).NotEmpty().MaximumLength(300).WithMessage("Informe o nome do item.");
            i.RuleFor(x => x.Unit).NotEmpty().MaximumLength(20).WithMessage("Informe a unidade do item.");
            i.RuleFor(x => x.Kind).Must(BuildEnumRules.Valid<BuildCostKind>).WithMessage("Tipo de item inválido.");
            i.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("A quantidade precisa ser maior que zero.");
        });
    }
}

public class DecideBuildChangeOrderRequestValidator : AbstractValidator<DecideBuildChangeOrderRequest>
{
    public DecideBuildChangeOrderRequestValidator()
        => RuleFor(x => x.Comment).MaximumLength(2000).WithMessage("O comentário pode ter até 2000 caracteres.");
}

public class SaveBuildSelectionRequestValidator : AbstractValidator<SaveBuildSelectionRequest>
{
    public SaveBuildSelectionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título da escolha (até 200 caracteres).");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("A descrição pode ter até 4000 caracteres.");
    }
}

public class SaveBuildSelectionOptionRequestValidator : AbstractValidator<SaveBuildSelectionOptionRequest>
{
    public SaveBuildSelectionOptionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Informe o nome da opção (até 200 caracteres).");
        RuleFor(x => x.Description).MaximumLength(2000).WithMessage("A descrição pode ter até 2000 caracteres.");
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 3650).When(x => x.LeadTimeDays.HasValue)
            .WithMessage("O prazo de entrega precisa ficar entre 0 e 3650 dias.");
        RuleFor(x => x.ImageKey).MaximumLength(500);
    }
}

public class SaveBuildMaterialRequestValidator : AbstractValidator<SaveBuildMaterialRequest>
{
    public SaveBuildMaterialRequestValidator()
    {
        RuleFor(x => x.MaterialName).NotEmpty().MaximumLength(300).WithMessage("Informe o material (até 300 caracteres).");
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20).WithMessage("Informe a unidade.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("A quantidade precisa ser maior que zero.");
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("As observações podem ter até 2000 caracteres.");
    }
}

public class DeliverBuildMaterialRequestValidator : AbstractValidator<DeliverBuildMaterialRequest>
{
    public DeliverBuildMaterialRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("A quantidade entregue precisa ser maior que zero.");
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.ReceivedBy).MaximumLength(200);
    }
}

public class SaveBuildMeasurementRequestValidator : AbstractValidator<SaveBuildMeasurementRequest>
{
    public SaveBuildMeasurementRequestValidator()
    {
        RuleFor(x => x).Must(x => x.PeriodEnd >= x.PeriodStart).WithMessage("O fim do período não pode ser antes do início.");
        RuleFor(x => x.Notes).MaximumLength(4000).WithMessage("As observações podem ter até 4000 caracteres.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= 300).WithMessage("Uma medição pode ter até 300 etapas.");
        RuleFor(x => x.Items).Must(i => i is null || i.Select(x => x.StageId).Distinct().Count() == i.Count)
            .WithMessage("Cada etapa aparece uma vez na medição.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.MeasuredPercent).InclusiveBetween(0, 100).WithMessage("O percentual medido precisa ficar entre 0 e 100.");
            i.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0m).When(x => x.Amount.HasValue).WithMessage("O valor medido não pode ser negativo.");
        });
    }
}
