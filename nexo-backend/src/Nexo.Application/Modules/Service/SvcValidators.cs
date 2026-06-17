using FluentValidation;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Shared FluentValidation rule sets for the Service create/update requests. Defining the
/// rules once per entity (keyed off the ISvc*Fields interfaces) keeps the create and update
/// validators identical without copy-paste.
/// </summary>
internal static class SvcValidationRules
{
    public static void ApplyProfessionalRules<T>(AbstractValidator<T> v) where T : ISvcProfessionalFields
    {
        v.RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Professional name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        v.RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
        v.RuleFor(x => x.Specialty).MaximumLength(150).When(x => x.Specialty is not null);
        v.RuleFor(x => x.Color).MaximumLength(20).When(x => x.Color is not null);
        v.RuleFor(x => x.Phone).MaximumLength(30).When(x => x.Phone is not null);
        v.RuleFor(x => x.Email).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.DefaultCommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.DefaultCommissionPercent.HasValue);
    }

    public static void ApplyCatalogRules<T>(AbstractValidator<T> v) where T : ISvcCatalogItemFields
    {
        v.RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Catalog item name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        v.RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Duration must be greater than zero minutes.");
        v.RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
        v.RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        v.RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
        v.RuleFor(x => x.CommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.CommissionPercent.HasValue);
    }
}

public class CreateSvcProfessionalRequestValidator : AbstractValidator<CreateSvcProfessionalRequest>
{
    public CreateSvcProfessionalRequestValidator() => SvcValidationRules.ApplyProfessionalRules(this);
}

public class UpdateSvcProfessionalRequestValidator : AbstractValidator<UpdateSvcProfessionalRequest>
{
    public UpdateSvcProfessionalRequestValidator() => SvcValidationRules.ApplyProfessionalRules(this);
}

public class CreateSvcCatalogItemRequestValidator : AbstractValidator<CreateSvcCatalogItemRequest>
{
    public CreateSvcCatalogItemRequestValidator() => SvcValidationRules.ApplyCatalogRules(this);
}

public class UpdateSvcCatalogItemRequestValidator : AbstractValidator<UpdateSvcCatalogItemRequest>
{
    public UpdateSvcCatalogItemRequestValidator() => SvcValidationRules.ApplyCatalogRules(this);
}
