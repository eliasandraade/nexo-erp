using FluentValidation;
using Nexo.Application.Features.Users;

namespace Nexo.Application.Validators.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private static readonly string[] ValidRoles =
        ["diretoria", "gerente", "vendedor", "estoquista", "cozinha"];

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

        // an invited user chooses the password through the e-mailed link
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Informe a senha ou envie um convite por e-mail.")
            .MinimumLength(6).WithMessage("A senha deve ter no mínimo 6 caracteres.")
            .When(x => !x.SendInvite);

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r?.ToLowerInvariant()))
            .WithMessage($"Role must be one of: {string.Join(", ", ValidRoles)}.");

    }
}
