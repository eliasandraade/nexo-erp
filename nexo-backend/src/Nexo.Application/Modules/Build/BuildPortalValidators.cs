using FluentValidation;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

// ── Site / tracking (shared rules for create and update) ──────────────────────

internal static class BuildSiteRules
{
    public static bool ValidMode(string? mode)
        => mode is null || (Enum.TryParse<BuildTrackingMode>(mode, ignoreCase: true, out var m) && Enum.IsDefined(m));

    public static void Apply<T>(AbstractValidator<T> v, Func<T, string?> mode, Func<T, decimal?> lat,
        Func<T, decimal?> lon, Func<T, decimal?> area)
    {
        v.RuleFor(x => mode(x)).Must(ValidMode).OverridePropertyName("TrackingMode")
            .WithMessage("Modo de acompanhamento inválido: use Standard ou ThreeD.");
        v.RuleFor(x => lat(x)).InclusiveBetween(-90m, 90m).When(x => lat(x).HasValue).OverridePropertyName("Latitude")
            .WithMessage("Latitude inválida: use um valor entre -90 e 90.");
        v.RuleFor(x => lon(x)).InclusiveBetween(-180m, 180m).When(x => lon(x).HasValue).OverridePropertyName("Longitude")
            .WithMessage("Longitude inválida: use um valor entre -180 e 180.");
        v.RuleFor(x => area(x)).GreaterThanOrEqualTo(0m).When(x => area(x).HasValue).OverridePropertyName("AreaM2")
            .WithMessage("A área não pode ser negativa.");
        v.RuleFor(x => x).Must(x => lat(x).HasValue == lon(x).HasValue)
            .WithMessage("Informe latitude e longitude juntas (ou nenhuma das duas).");
    }
}

public class UpdateBuildStageProgressRequestValidator : AbstractValidator<UpdateBuildStageProgressRequest>
{
    public UpdateBuildStageProgressRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000).WithMessage("A observação pode ter até 1000 caracteres.")
            .When(x => x.Note is not null);
        RuleFor(x => x.EffectiveDate)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("A data do avanço não pode estar no futuro.");
    }
}

public class CreateScheduleBaselineRequestValidator : AbstractValidator<CreateScheduleBaselineRequest>
{
    public CreateScheduleBaselineRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("O motivo pode ter até 1000 caracteres.")
            .When(x => x.Reason is not null);
    }
}

// ── Portal ────────────────────────────────────────────────────────────────────

public class UpdateBuildPortalCompanyRequestValidator : AbstractValidator<UpdateBuildPortalCompanyRequest>
{
    public UpdateBuildPortalCompanyRequestValidator()
    {
        RuleFor(x => x.Slug).Custom((slug, ctx) =>
        {
            var problem = BuildPortalSlug.Validate(BuildPortalSlug.Normalize(slug));
            if (problem is not null) ctx.AddFailure(problem);
        });
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Informe o nome da empresa no portal.")
            .MaximumLength(200).WithMessage("O nome pode ter até 200 caracteres.");
        RuleFor(x => x.ContactEmail).MaximumLength(200).EmailAddress().WithMessage("E-mail de contato inválido.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone).MaximumLength(40).WithMessage("O telefone pode ter até 40 caracteres.");
    }
}

public class UpdateBuildProjectPortalRequestValidator : AbstractValidator<UpdateBuildProjectPortalRequest>
{
    public UpdateBuildProjectPortalRequestValidator()
    {
        RuleFor(x => x.Slug).Custom((slug, ctx) =>
        {
            if (string.IsNullOrWhiteSpace(slug)) return;
            var problem = BuildPortalSlug.Validate(BuildPortalSlug.Normalize(slug));
            if (problem is not null) ctx.AddFailure(problem);
        });
        RuleFor(x => x.Access)
            .Must(a => a is null || (Enum.TryParse<BuildPortalAccess>(a, ignoreCase: true, out var v) && Enum.IsDefined(v)))
            .WithMessage("Acesso inválido: use Public ou Password.");
        RuleFor(x => x.PrimaryColor).Must(c => string.IsNullOrWhiteSpace(c) || BuildPortalText.IsHexColor(c.Trim()))
            .WithMessage("Cor principal inválida: use o formato #RRGGBB.");
        RuleFor(x => x.AccentColor).Must(c => string.IsNullOrWhiteSpace(c) || BuildPortalText.IsHexColor(c.Trim()))
            .WithMessage("Cor de destaque inválida: use o formato #RRGGBB.");
        RuleFor(x => x.Title).MaximumLength(200).WithMessage("O título pode ter até 200 caracteres.");
        RuleFor(x => x.WelcomeMessage).MaximumLength(2000).WithMessage("A mensagem pode ter até 2000 caracteres.");
        RuleFor(x => x.ContactEmail).EmailAddress().WithMessage("E-mail de contato inválido.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

public class SetBuildPortalPasswordRequestValidator : AbstractValidator<SetBuildPortalPasswordRequest>
{
    public SetBuildPortalPasswordRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha do portal.")
            .MinimumLength(8).WithMessage("A senha do portal precisa ter pelo menos 8 caracteres.")
            .MaximumLength(128).WithMessage("A senha do portal pode ter até 128 caracteres.");
    }
}

public class SetDailyLogClientVisibilityRequestValidator : AbstractValidator<SetDailyLogClientVisibilityRequest>
{
    public SetDailyLogClientVisibilityRequestValidator()
    {
        RuleFor(x => x.ClientSummary).MaximumLength(2000).WithMessage("O resumo para o cliente pode ter até 2000 caracteres.");
        RuleFor(x => x.ClientSummary).NotEmpty().When(x => x.ClientVisible)
            .WithMessage("Escreva o resumo para o cliente antes de mostrar este registro no portal.");
    }
}

public class SaveBuildClientUpdateRequestValidator : AbstractValidator<SaveBuildClientUpdateRequest>
{
    public SaveBuildClientUpdateRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Informe o título da atualização.")
            .MaximumLength(200).WithMessage("O título pode ter até 200 caracteres.");
        RuleFor(x => x.Body).NotEmpty().WithMessage("Escreva o texto da atualização.")
            .MaximumLength(10000).WithMessage("O texto pode ter até 10000 caracteres.");
        RuleFor(x => x.Kind)
            .Must(k => k is null || (Enum.TryParse<BuildClientUpdateKind>(k, ignoreCase: true, out var v) && Enum.IsDefined(v)))
            .WithMessage("Tipo inválido: use Update, Milestone ou WeeklyDigest.");
    }
}
