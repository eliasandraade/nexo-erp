using FluentValidation;
using Nexo.Application.Features.Settings;

namespace Nexo.Application.Validators.Settings;

public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Inventory).NotNull().WithMessage("Informe os alertas de estoque.");
        RuleFor(x => x.Pos).NotNull().WithMessage("Informe as regras do PDV.");

        When(x => x.Pos is not null, () =>
        {
            RuleFor(x => x.Pos.MaxDiscountPercent)
                .InclusiveBetween(0, 100)
                .WithMessage("O desconto máximo vai de 0% a 100%.");
            RuleFor(x => x.Pos)
                .Must(p => p.AllowValueDiscount || p.AllowPercentDiscount)
                .WithMessage("Permita ao menos um tipo de desconto (R$ ou %).");
        });
    }
}
