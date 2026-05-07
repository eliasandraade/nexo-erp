using FluentValidation;
using Nexo.Application.Modules.Interpreter;

namespace Nexo.Application.Validators.Interpreter;

public class AnalyzeMovementRequestValidator : AbstractValidator<AnalyzeMovementRequest>
{
    public AnalyzeMovementRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Text is not null || x.AttachmentId is not null)
            .WithMessage("Either Text or AttachmentId must be provided.");

        When(x => x.Text is not null, () =>
        {
            RuleFor(x => x.Text)
                .NotEmpty().WithMessage("Text cannot be empty.")
                .MaximumLength(1000).WithMessage("Text must not exceed 1000 characters.");
        });

        When(x => x.AttachmentId is not null, () =>
        {
            RuleFor(x => x.AttachmentId)
                .NotEqual(Guid.Empty).WithMessage("AttachmentId must be a valid GUID.");
        });

        RuleFor(x => x.InputSource)
            .Must(s => new[] { "Text", "File", "Audio", "Xml", "Email", "Webhook" }.Contains(s))
            .WithMessage("InputSource must be one of: Text, File, Audio, Xml, Email, Webhook.");
    }
}
