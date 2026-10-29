using FluentValidation;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

// Input limits of the administration, policies, files and catalogues. Free text is bounded; amounts are money (decimal, cents,
// positive); dates are plausible; codes keep their format; references and reasons are required where a decision is recorded.

internal static class TaxAdminValidation
{
    public static readonly DateOnly EarliestDate = new(2000, 1, 1);
    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    public static bool Plausible(DateOnly d) => d >= EarliestDate && d <= LatestDate;
    public static bool Plausible(DateOnly? d) => d is null || Plausible(d.Value);
}

public sealed class ActivateTaxModuleRequestValidator : AbstractValidator<ActivateTaxModuleRequest>
{
    public ActivateTaxModuleRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum().NotEqual(TaxApprovalMode.Disabled).WithMessage("Escolha o piloto controlado ou o modo definitivo.");
        RuleFor(x => x.DecisionReference).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Confirmation).NotEmpty().MaximumLength(60);
    }
}

public sealed class SuspendTaxModuleRequestValidator : AbstractValidator<SuspendTaxModuleRequest>
{
    public SuspendTaxModuleRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Confirmation).NotEmpty().MaximumLength(60);
    }
}

public sealed class DeactivateTaxModuleRequestValidator : AbstractValidator<DeactivateTaxModuleRequest>
{
    public DeactivateTaxModuleRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Confirmation).NotEmpty().MaximumLength(60);
    }
}

public sealed class ConfigureMaterialityRequestValidator : AbstractValidator<ConfigureMaterialityRequest>
{
    public ConfigureMaterialityRequestValidator()
    {
        RuleFor(x => x.Threshold).GreaterThan(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents).WithMessage(TaxValidation.CentsMessage);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.EffectiveFrom).Must(TaxAdminValidation.Plausible).WithMessage("Data fora do intervalo aceito.");
        RuleFor(x => x.EffectiveTo).Must(TaxAdminValidation.Plausible).WithMessage("Data fora do intervalo aceito.");
        RuleFor(x => x).Must(x => x.EffectiveTo is null || x.EffectiveTo >= x.EffectiveFrom).WithMessage("O fim da vigência é anterior ao início.");
        RuleFor(x => x.Rationale).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.DecisionReference).NotEmpty().MaximumLength(300);
    }
}

public sealed class ConfigureRetentionPolicyRequestValidator : AbstractValidator<ConfigureRetentionPolicyRequest>
{
    public ConfigureRetentionPolicyRequestValidator()
    {
        RuleFor(x => x.EvidenceType).IsInEnum().When(x => x.EvidenceType is not null);
        RuleFor(x => x.RetentionMonths).InclusiveBetween(1, 1200);
        RuleFor(x => x.StartsAt).IsInEnum();
        RuleFor(x => x.Rationale).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(500);
    }
}

public sealed class ImportClassificationCodesRequestValidator : AbstractValidator<ImportClassificationCodesRequest>
{
    public ImportClassificationCodesRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Source).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SourceVersion).MaximumLength(100);
        RuleFor(x => x.Items).NotEmpty().Must(i => i.Count <= 5000).WithMessage("Importe até 5.000 códigos por vez.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
            i.RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
            i.RuleFor(x => x.Notes).MaximumLength(2000);
            i.RuleFor(x => x.ValidFrom).Must(TaxAdminValidation.Plausible).WithMessage("Data fora do intervalo aceito.");
            i.RuleFor(x => x.ValidTo).Must(TaxAdminValidation.Plausible).WithMessage("Data fora do intervalo aceito.");
            i.RuleFor(x => x).Must(x => x.ValidTo is null || x.ValidTo >= x.ValidFrom).WithMessage("O fim da vigência é anterior ao início.");
        });
    }
}

public sealed class ChangeClassificationStatusRequestValidator : AbstractValidator<ChangeClassificationStatusRequest>
{
    public ChangeClassificationStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RecordClassificationReviewRequestValidator : AbstractValidator<RecordClassificationReviewRequest>
{
    public RecordClassificationReviewRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum().NotEqual(ProfessionalReviewStatus.Pending);
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SetItemClassificationRequestValidator : AbstractValidator<SetItemClassificationRequest>
{
    public SetItemClassificationRequestValidator()
    {
        RuleFor(x => x.ItemKind).IsInEnum();
        RuleFor(x => x.Ncm).Must(n => TaxValidation.Digits(n?.Replace(".", ""), 8)).When(x => !string.IsNullOrEmpty(x.Ncm)).WithMessage("NCM tem 8 dígitos.");
        RuleFor(x => x.Ncm).NotEmpty().When(x => x.ItemKind == TaxItemKind.Good).WithMessage("Mercadoria precisa de NCM.");
        RuleFor(x => x.Nbs).NotEmpty().When(x => x.ItemKind == TaxItemKind.Service).WithMessage("Serviço precisa de NBS.");
        RuleFor(x => x.Nbs).MaximumLength(15);
        RuleFor(x => x.CClassTrib).NotEmpty().MaximumLength(10).Must(c => c.All(char.IsAsciiLetterOrDigit))
            .WithMessage("cClassTrib: até 10 letras ou números.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CreateObligationDefinitionRequestValidator : AbstractValidator<CreateObligationDefinitionRequest>
{
    public CreateObligationDefinitionRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(500);
    }
}

public sealed class AssignOpportunityRequestValidator : AbstractValidator<AssignOpportunityRequest>
{
    public AssignOpportunityRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(200).When(x => x.UserId is not null);
    }
}

public sealed class ReplaceEvidenceRequestValidator : AbstractValidator<ReplaceEvidenceRequest>
{
    public ReplaceEvidenceRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.DocumentReference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class RevokeEvidenceRequestValidator : AbstractValidator<RevokeEvidenceRequest>
{
    public RevokeEvidenceRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RevokeTaxFileRequestValidator : AbstractValidator<RevokeTaxFileRequest>
{
    public RevokeTaxFileRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class SetLegalHoldRequestValidator : AbstractValidator<SetLegalHoldRequest>
{
    public SetLegalHoldRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class SetObligationActiveRequestValidator : AbstractValidator<SetObligationActiveRequest>
{
    public SetObligationActiveRequestValidator() => RuleFor(x => x.Active).NotNull();
}
