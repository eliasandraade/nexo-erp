using FluentValidation;

namespace Nexo.Application.Modules.Service;

// ── Professionals ───────────────────────────────────────────────────────────────

public class CreateSvcProfessionalRequestValidator : AbstractValidator<CreateSvcProfessionalRequest>
{
    public CreateSvcProfessionalRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Professional name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
        RuleFor(x => x.Specialty).MaximumLength(150).When(x => x.Specialty is not null);
        RuleFor(x => x.Color).MaximumLength(20).When(x => x.Color is not null);
        RuleFor(x => x.Phone).MaximumLength(30).When(x => x.Phone is not null);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DefaultCommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.DefaultCommissionPercent.HasValue);
    }
}

public class UpdateSvcProfessionalRequestValidator : AbstractValidator<UpdateSvcProfessionalRequest>
{
    public UpdateSvcProfessionalRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Professional name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
        RuleFor(x => x.Specialty).MaximumLength(150).When(x => x.Specialty is not null);
        RuleFor(x => x.Color).MaximumLength(20).When(x => x.Color is not null);
        RuleFor(x => x.Phone).MaximumLength(30).When(x => x.Phone is not null);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DefaultCommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.DefaultCommissionPercent.HasValue);
    }
}

// ── Catalog ─────────────────────────────────────────────────────────────────────

public class CreateSvcCatalogItemRequestValidator : AbstractValidator<CreateSvcCatalogItemRequest>
{
    public CreateSvcCatalogItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Catalog item name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Duration must be greater than zero minutes.");
        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.CommissionPercent.HasValue);
    }
}

public class UpdateSvcCatalogItemRequestValidator : AbstractValidator<UpdateSvcCatalogItemRequest>
{
    public UpdateSvcCatalogItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Catalog item name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Duration must be greater than zero minutes.");
        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.CommissionPercent.HasValue);
    }
}
