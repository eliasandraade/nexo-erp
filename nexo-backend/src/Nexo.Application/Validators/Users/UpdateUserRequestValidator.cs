using FluentValidation;
using Nexo.Application.Features.Users;

namespace Nexo.Application.Validators.Users;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    private static readonly string[] ValidRoles =
        ["diretoria", "gerente", "vendedor", "estoquista"];

    private static readonly string[] ValidStatuses =
        ["active", "inactive", "blocked"];

    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(150);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Must be a valid email address.");

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(r => ValidRoles.Contains(r?.ToLowerInvariant()))
            .WithMessage($"Role must be one of: {string.Join(", ", ValidRoles)}.");

        When(x => x.Status is not null, () =>
        {
            RuleFor(x => x.Status!)
                .Must(s => ValidStatuses.Contains(s.ToLowerInvariant()))
                .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}.");
        });
    }
}
