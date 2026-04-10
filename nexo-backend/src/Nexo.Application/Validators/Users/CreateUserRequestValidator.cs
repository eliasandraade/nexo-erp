using FluentValidation;
using Nexo.Application.Features.Users;

namespace Nexo.Application.Validators.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private static readonly string[] ValidRoles =
        ["diretoria", "gerente", "vendedor", "estoquista"];

    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(150);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Must be a valid email address.");

        RuleFor(x => x.Login)
            .NotEmpty().WithMessage("Login is required.")
            .MinimumLength(3).WithMessage("Login must be at least 3 characters.")
            .MaximumLength(50)
            .Matches("^[a-zA-Z0-9._-]+$")
            .WithMessage("Login may only contain letters, numbers, dots, underscores, and hyphens.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r?.ToLowerInvariant()))
            .WithMessage($"Role must be one of: {string.Join(", ", ValidRoles)}.");

    }
}
