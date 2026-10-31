using FluentValidation;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// Request shape checks of Wave 3 (400). Business rules (status transitions, outcome, photos, critical NCs) stay in the
// domain (422/409).

public class BuildChecklistItemRequestValidator : AbstractValidator<BuildChecklistItemRequest>
{
    public const int MaxItems = 300;

    public BuildChecklistItemRequestValidator()
        => RuleFor(x => x.Text).NotEmpty().MaximumLength(500).WithMessage("Informe o texto do item (até 500 caracteres).");
}

public class SaveBuildChecklistTemplateRequestValidator : AbstractValidator<SaveBuildChecklistTemplateRequest>
{
    public SaveBuildChecklistTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Informe o nome do checklist (até 200 caracteres).");
        RuleFor(x => x.Category).MaximumLength(100).WithMessage("A disciplina pode ter até 100 caracteres.");
        RuleFor(x => x.StageNameHint).MaximumLength(200).WithMessage("A etapa sugerida pode ter até 200 caracteres.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= BuildChecklistItemRequestValidator.MaxItems)
            .WithMessage("Um checklist pode ter até 300 itens.");
        RuleForEach(x => x.Items).SetValidator(new BuildChecklistItemRequestValidator());
    }
}

public class CreateBuildInspectionRequestValidator : AbstractValidator<CreateBuildInspectionRequest>
{
    public CreateBuildInspectionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título da vistoria (até 200 caracteres).");
        RuleFor(x => x.InspectorName).MaximumLength(200).WithMessage("O nome do vistoriador pode ter até 200 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(4000).WithMessage("As observações podem ter até 4000 caracteres.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= BuildChecklistItemRequestValidator.MaxItems)
            .WithMessage("Um checklist pode ter até 300 itens.");
        RuleForEach(x => x.Items).SetValidator(new BuildChecklistItemRequestValidator());
    }
}

public class UpdateBuildInspectionRequestValidator : AbstractValidator<UpdateBuildInspectionRequest>
{
    public UpdateBuildInspectionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título da vistoria (até 200 caracteres).");
        RuleFor(x => x.InspectorName).MaximumLength(200).WithMessage("O nome do vistoriador pode ter até 200 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(4000).WithMessage("As observações podem ter até 4000 caracteres.");
    }
}

public class RecordBuildInspectionItemRequestValidator : AbstractValidator<RecordBuildInspectionItemRequest>
{
    public RecordBuildInspectionItemRequestValidator()
    {
        RuleFor(x => x.Result).NotEmpty().Must(BuildEnumRules.Valid<BuildInspectionItemResult>)
            .WithMessage("Resultado inválido: use Pending, Ok, NotOk ou NotApplicable.");
        RuleFor(x => x.Severity).Must(BuildEnumRules.Valid<BuildNonConformitySeverity>)
            .WithMessage("Gravidade inválida: use Low, Medium, High ou Critical.");
        RuleFor(x => x.Comment).MaximumLength(2000).WithMessage("O comentário pode ter até 2000 caracteres.");
        RuleFor(x => x.PhotoKeys).Must(p => p is null || p.Count <= BuildInspectionItem.MaxPhotos)
            .WithMessage($"Cada item aceita até {BuildInspectionItem.MaxPhotos} fotos.");
    }
}

public class SaveBuildNonConformityRequestValidator : AbstractValidator<SaveBuildNonConformityRequest>
{
    public SaveBuildNonConformityRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título da não conformidade (até 200 caracteres).");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("A descrição pode ter até 4000 caracteres.");
        RuleFor(x => x.Severity).Must(BuildEnumRules.Valid<BuildNonConformitySeverity>)
            .WithMessage("Gravidade inválida: use Low, Medium, High ou Critical.");
        RuleFor(x => x.ResponsibleName).MaximumLength(200).WithMessage("O responsável pode ter até 200 caracteres.");
        RuleFor(x => x.CostImpact).GreaterThanOrEqualTo(0m).When(x => x.CostImpact.HasValue)
            .WithMessage("O impacto de custo não pode ser negativo.");
        RuleFor(x => x.PhotoKeys).Must(p => p is null || p.Count <= BuildNonConformity.MaxPhotos)
            .WithMessage($"Uma não conformidade aceita até {BuildNonConformity.MaxPhotos} fotos.");
        RuleFor(x => x.RootCause).MaximumLength(2000).WithMessage("A causa raiz pode ter até 2000 caracteres.");
        RuleFor(x => x.CorrectiveAction).MaximumLength(2000).WithMessage("A ação corretiva pode ter até 2000 caracteres.");
    }
}

public class CancelBuildNonConformityRequestValidator : AbstractValidator<CancelBuildNonConformityRequest>
{
    public CancelBuildNonConformityRequestValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000).WithMessage("Informe o motivo do cancelamento.");
}

public class SaveBuildHandoverRequestValidator : AbstractValidator<SaveBuildHandoverRequest>
{
    public SaveBuildHandoverRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(4000).WithMessage("As observações podem ter até 4000 caracteres.");
        RuleFor(x => x.Documents).Must(d => d is null || d.Count <= BuildHandover.MaxDocuments)
            .WithMessage($"A entrega aceita até {BuildHandover.MaxDocuments} documentos.");
        RuleForEach(x => x.Documents).ChildRules(d =>
            d.RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Informe o nome do documento."))
            .When(x => x.Documents is not null);
    }
}

public class CreateBuildWalkthroughRequestValidator : AbstractValidator<CreateBuildWalkthroughRequest>
{
    public CreateBuildWalkthroughRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200).WithMessage("O título pode ter até 200 caracteres.");
        RuleFor(x => x.InspectorName).MaximumLength(200).WithMessage("O nome do vistoriador pode ter até 200 caracteres.");
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= BuildChecklistItemRequestValidator.MaxItems)
            .WithMessage("Um checklist pode ter até 300 itens.");
        RuleForEach(x => x.Items).SetValidator(new BuildChecklistItemRequestValidator());
    }
}

public class AcceptBuildHandoverRequestValidator : AbstractValidator<AcceptBuildHandoverRequest>
{
    public AcceptBuildHandoverRequestValidator()
    {
        RuleFor(x => x.SignerName).NotEmpty().MaximumLength(200).WithMessage("Informe o nome de quem assina o aceite.");
        RuleFor(x => x.Comment).MaximumLength(2000).WithMessage("O comentário pode ter até 2000 caracteres.");
    }
}

public class SaveBuildWarrantyTermRequestValidator : AbstractValidator<SaveBuildWarrantyTermRequest>
{
    public SaveBuildWarrantyTermRequestValidator()
    {
        RuleFor(x => x.System).NotEmpty().MaximumLength(100).WithMessage("Informe o sistema (até 100 caracteres).");
        RuleFor(x => x.Months).InclusiveBetween(1, 600).WithMessage("O prazo da garantia precisa ficar entre 1 e 600 meses.");
        RuleFor(x => x.Description).MaximumLength(2000).WithMessage("A descrição pode ter até 2000 caracteres.");
    }
}

public class OpenBuildAssistanceRequestValidator : AbstractValidator<OpenBuildAssistanceRequest>
{
    public OpenBuildAssistanceRequestValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100).WithMessage("Informe a categoria (até 100 caracteres).");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Informe o título (até 200 caracteres).");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000).WithMessage("Descreva o problema (até 4000 caracteres).");
        RuleFor(x => x.Location).MaximumLength(200).WithMessage("O local pode ter até 200 caracteres.");
        RuleFor(x => x.OpenedByName).MaximumLength(200).WithMessage("O nome pode ter até 200 caracteres.");
        RuleFor(x => x.ContactPhone).MaximumLength(30).WithMessage("O telefone pode ter até 30 caracteres.");
    }
}

public class TriageBuildAssistanceRequestValidator : AbstractValidator<TriageBuildAssistanceRequest>
{
    public TriageBuildAssistanceRequestValidator()
        => RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("As notas da triagem podem ter até 2000 caracteres.");
}

public class ResolveBuildAssistanceRequestValidator : AbstractValidator<ResolveBuildAssistanceRequest>
{
    public ResolveBuildAssistanceRequestValidator()
        => RuleFor(x => x.ResolutionNotes).NotEmpty().MaximumLength(2000).WithMessage("Descreva o que foi feito (até 2000 caracteres).");
}

public class RejectBuildAssistanceRequestValidator : AbstractValidator<RejectBuildAssistanceRequest>
{
    public RejectBuildAssistanceRequestValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000).WithMessage("Informe o motivo da recusa.");
}
