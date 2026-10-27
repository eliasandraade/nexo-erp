using FluentValidation;

namespace Nexo.Application.Modules.TaxIntelligence;

// Input limits of the Tax Intelligence API. Amounts are non-negative and in cents; lists are bounded
// so one request cannot ask the engine for unbounded work.

internal static class TaxValidation
{
    public const int MaxLines = 500;
    public const int MaxScenarios = 10;

    public const decimal MaxAmount = 10_000_000_000m;

    public static bool Cents(decimal v) => decimal.Round(v, 2) == v;
    public static bool Digits(string? v, int length) => v is not null && v.Length == length && v.All(char.IsAsciiDigit);
}

public sealed class TaxLineRequestValidator : AbstractValidator<TaxLineRequest>
{
    public TaxLineRequestValidator()
    {
        RuleFor(x => x.ItemKind).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Ncm).Must(n => TaxValidation.Digits(n, 8)).When(x => !string.IsNullOrEmpty(x.Ncm)).WithMessage("NCM tem 8 dígitos.");
        RuleFor(x => x.Quantity).GreaterThan(0).LessThan(1_000_000_000m);
        RuleFor(x => x.GrossAmount).GreaterThanOrEqualTo(0).LessThan(10_000_000_000m).Must(TaxValidation.Cents).WithMessage("Valor em centavos.");
        RuleFor(x => x.UnconditionalDiscount).GreaterThanOrEqualTo(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents).WithMessage("Valor em centavos.");
        RuleFor(x => x.ConditionalDiscount).GreaterThanOrEqualTo(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents).WithMessage("Valor em centavos.");
        RuleFor(x => x.Surcharges).GreaterThanOrEqualTo(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents).WithMessage("Valor em centavos.");
        RuleFor(x => x.ExcludedTaxes!.Value).GreaterThanOrEqualTo(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents).When(x => x.ExcludedTaxes is not null)
            .WithName("ExcludedTaxes");
        RuleFor(x => x).Must(x => x.UnconditionalDiscount + x.ConditionalDiscount <= x.GrossAmount)
            .WithMessage("Os descontos passam do valor da linha.");
        RuleFor(x => x.SpecialTreatment).MaximumLength(60);
    }
}

public sealed class TaxOperationRequestValidator : AbstractValidator<TaxOperationRequest>
{
    public TaxOperationRequestValidator()
    {
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.CounterpartyRegime).IsInEnum().When(x => x.CounterpartyRegime is not null);
        RuleFor(x => x.CounterpartyState).Length(2).When(x => !string.IsNullOrEmpty(x.CounterpartyState));
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.OperationDate).Must(d => d.Year is >= 2000 and <= 2100).WithMessage("Data da operação inválida.");
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= TaxValidation.MaxLines).WithMessage($"No máximo {TaxValidation.MaxLines} linhas.");
        RuleForEach(x => x.Lines).NotNull().SetValidator(new TaxLineRequestValidator());
    }
}

public sealed class CreateTaxProfileVersionRequestValidator : AbstractValidator<CreateTaxProfileVersionRequest>
{
    public CreateTaxProfileVersionRequestValidator()
    {
        RuleFor(x => x.Regime).IsInEnum();
        RuleFor(x => x.State).NotEmpty().Length(2);
        RuleFor(x => x.CityIbgeCode).Must(c => TaxValidation.Digits(c, 7)).WithMessage("Código IBGE do município tem 7 dígitos.");
        RuleFor(x => x.PrimaryCnae).Must(c => TaxValidation.Digits(c, 7)).WithMessage("CNAE tem 7 dígitos.");
        RuleForEach(x => x.SecondaryCnaes).Must(c => TaxValidation.Digits(c, 7)).WithMessage("CNAE tem 7 dígitos.");
        RuleFor(x => x.SecondaryCnaes).Must(l => l is null || l.Count <= 50);
        RuleFor(x => x.SpecialRegimes).Must(l => l is null || (l.Count <= 20 && l.All(s => s is not null && s.Length <= 100)));
        RuleFor(x => x.Benefits).Must(l => l is null || (l.Count <= 20 && l.All(s => s is not null && s.Length <= 300)));
        RuleFor(x => x.SecondaryCnaes).Must(l => l is null || l.All(s => s is not null));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ValidFrom).Must(d => d.Year is >= 2000 and <= 2100).WithMessage("Início de vigência inválido.");
    }
}

public sealed class TaxScenarioRequestValidator : AbstractValidator<TaxScenarioRequest>
{
    public TaxScenarioRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Regime).IsInEnum().When(x => x.Regime is not null);
        RuleFor(x => x.Direction).IsInEnum().When(x => x.Direction is not null);
        RuleFor(x => x.CounterpartyRegime).IsInEnum().When(x => x.CounterpartyRegime is not null);
        RuleFor(x => x.Lines).Must(l => l is null || (l.Count > 0 && l.Count <= TaxValidation.MaxLines));
        RuleForEach(x => x.Lines).NotNull().SetValidator(new TaxLineRequestValidator());
        RuleFor(x => x.RateHypotheses).Must(l => l is null || l.Count <= 3);
        RuleForEach(x => x.RateHypotheses).NotNull().ChildRules(h =>
        {
            h.RuleFor(r => r.Tax).IsInEnum();
            h.RuleFor(r => r.RatePercent).InclusiveBetween(0, 100);
            h.RuleFor(r => r.Label).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class CreateTaxSimulationRequestValidator : AbstractValidator<CreateTaxSimulationRequest>
{
    public CreateTaxSimulationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Baseline).NotNull().SetValidator(new TaxOperationRequestValidator());
        RuleFor(x => x.Scenarios).NotEmpty().Must(s => s.Count <= TaxValidation.MaxScenarios)
            .WithMessage($"No máximo {TaxValidation.MaxScenarios} cenários.");
        RuleForEach(x => x.Scenarios).NotNull().SetValidator(new TaxScenarioRequestValidator());
    }
}

public sealed class CompareSuppliersRequestValidator : AbstractValidator<CompareSuppliersRequest>
{
    public CompareSuppliersRequestValidator()
    {
        RuleFor(x => x.Offers).Must(o => o.Count is >= 2 and <= TaxValidation.MaxScenarios)
            .WithMessage($"Compare de 2 a {TaxValidation.MaxScenarios} fornecedores.");
        RuleFor(x => x.Offers).NotNull();
        RuleForEach(x => x.Offers).NotNull().ChildRules(o =>
        {
            o.RuleFor(r => r.Supplier).NotEmpty().MaximumLength(200);
            o.RuleFor(r => r.SupplierRegime).IsInEnum().When(r => r.SupplierRegime is not null);
            o.RuleFor(r => r.Lines).NotEmpty().Must(l => l.Count <= TaxValidation.MaxLines);
            o.RuleForEach(r => r.Lines).NotNull().SetValidator(new TaxLineRequestValidator());
        });
        RuleFor(x => x.RateHypotheses).Must(l => l is null || l.Count <= 3);
        RuleForEach(x => x.RateHypotheses).NotNull().ChildRules(h =>
        {
            h.RuleFor(r => r.Tax).IsInEnum();
            h.RuleFor(r => r.RatePercent).InclusiveBetween(0, 100);
            h.RuleFor(r => r.Label).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class AnswerRequirementRequestValidator : AbstractValidator<AnswerRequirementRequest>
{
    public AnswerRequirementRequestValidator()
    {
        RuleFor(x => x.Met).NotNull().WithMessage("Diga se o requisito foi atendido.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class CreateSplitExpectationsRequestValidator : AbstractValidator<CreateSplitExpectationsRequest>
{
    public CreateSplitExpectationsRequestValidator() => RuleFor(x => x.Procedure).IsInEnum();
}

public sealed class AddEvidenceRequestValidator : AbstractValidator<AddEvidenceRequest>
{
    public AddEvidenceRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.DocumentReference).MaximumLength(200);
    }
}

public sealed class RejectOpportunityRequestValidator : AbstractValidator<RejectOpportunityRequest>
{
    public RejectOpportunityRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class BlockOpportunityRequestValidator : AbstractValidator<BlockOpportunityRequest>
{
    public BlockOpportunityRequestValidator()
    {
        RuleFor(x => x.Structure).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RegisterTaxCreditRequestValidator : AbstractValidator<RegisterTaxCreditRequest>
{
    public RegisterTaxCreditRequestValidator()
    {
        RuleFor(x => x.Tax).IsInEnum();
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(40);
        RuleFor(x => x.SourceDocumentKey).Must(k => TaxValidation.Digits(k, 44)).When(x => !string.IsNullOrEmpty(x.SourceDocumentKey))
            .WithMessage("A chave de acesso tem 44 dígitos.");
        RuleFor(x => x.SupplierDocument).Must(d => TaxValidation.Digits(d, 11) || TaxValidation.Digits(d, 14))
            .When(x => !string.IsNullOrEmpty(x.SupplierDocument)).WithMessage("CPF ou CNPJ do fornecedor, só dígitos.");
        RuleFor(x => x.OriginalAmount).GreaterThan(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents);
        RuleFor(x => x.EligibleAmount).GreaterThanOrEqualTo(0).Must(TaxValidation.Cents);
        RuleFor(x => x.EligibilityBasis).NotEmpty().MaximumLength(500);
        RuleFor(x => x).Must(x => x.ExpiresOn is null || x.ExpiresOn >= x.AcquiredOn).WithMessage("O vencimento é anterior à aquisição.");
    }
}

public sealed class TaxCreditMovementRequestValidator : AbstractValidator<TaxCreditMovementRequest>
{
    public TaxCreditMovementRequestValidator()
    {
        RuleFor(x => x.Movement).IsInEnum();
        RuleFor(x => x.Amount).NotNull().GreaterThan(0).LessThan(TaxValidation.MaxAmount).When(x => x.Movement != TaxCreditMovement.Expire);
        RuleFor(x => x.Amount!.Value).Must(TaxValidation.Cents).When(x => x.Amount is not null).WithName("Amount");
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Movement is TaxCreditMovement.Block or TaxCreditMovement.Reverse);
        RuleFor(x => x.ObligationTax).NotNull().IsInEnum().When(x => x.Movement == TaxCreditMovement.Use);
        RuleFor(x => x.ObligationReference).NotEmpty().MaximumLength(100).When(x => x.Movement == TaxCreditMovement.Use);
    }
}

public sealed class RecordSplitSettlementRequestValidator : AbstractValidator<RecordSplitSettlementRequest>
{
    public RecordSplitSettlementRequestValidator()
    {
        RuleFor(x => x.ActualAmount).GreaterThanOrEqualTo(0).LessThan(TaxValidation.MaxAmount).Must(TaxValidation.Cents);
        RuleFor(x => x.ExternalReference).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateLegalSourceRequestValidator : AbstractValidator<CreateLegalSourceRequest>
{
    public CreateLegalSourceRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(60).Must(c => c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')).WithMessage("Código: letras, números e hífen.");
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Url).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Excerpt).NotEmpty().MaximumLength(20_000);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CreateTaxRuleRequestValidator : AbstractValidator<CreateTaxRuleRequest>
{
    public CreateTaxRuleRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(80).Must(k => k.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-')).WithMessage("Chave: letras, números, ponto e hífen.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
    }
}

public sealed class CreateTaxRuleVersionRequestValidator : AbstractValidator<CreateTaxRuleVersionRequest>
{
    public CreateTaxRuleVersionRequestValidator()
    {
        RuleFor(x => x).Must(x => x.ValidTo is null || x.ValidTo >= x.ValidFrom).WithMessage("O fim da vigência é anterior ao início.");
        RuleFor(x => x.Priority).InclusiveBetween(0, 10_000);
        RuleFor(x => x.LegalSourceCodes).NotEmpty().WithMessage("Toda regra precisa de ao menos uma fonte jurídica.")
            .Must(l => l.Count <= 20 && l.All(c => !string.IsNullOrWhiteSpace(c)));
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
