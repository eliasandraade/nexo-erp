using FluentValidation;
using Nexo.Application.Common;
using Nexo.Domain.Enums;

namespace Nexo.Application.Features.Fiscal;

// ── Emitter profile (per store) ─────────────────────────────────────────────────

public record FiscalProfileDto(
    string LegalName,
    string? TradeName,
    string Cnpj,
    string StateRegistration,
    string? MunicipalRegistration,
    string TaxRegime,
    int Crt,
    string Street,
    string Number,
    string? Complement,
    string District,
    string CityName,
    string CityIbgeCode,
    string State,
    string ZipCode,
    DateTime UpdatedAt);

public record SaveFiscalProfileRequest(
    string LegalName,
    string? TradeName,
    string Cnpj,
    string StateRegistration,
    string? MunicipalRegistration,
    string TaxRegime,
    string Street,
    string Number,
    string? Complement,
    string District,
    string CityName,
    string CityIbgeCode,
    string State,
    string ZipCode);

public class SaveFiscalProfileRequestValidator : AbstractValidator<SaveFiscalProfileRequest>
{
    public static readonly string[] States =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    public SaveFiscalProfileRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().WithMessage("Informe a razão social.").MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.Cnpj).Must(FiscalCodes.IsCnpj).WithMessage("CNPJ inválido. Confira os números.");
        RuleFor(x => x.StateRegistration).NotEmpty().WithMessage("Informe a inscrição estadual (ou ISENTO).")
            .Must(BeStateRegistration).WithMessage("Inscrição estadual: só números (até 14) ou ISENTO.");
        RuleFor(x => x.MunicipalRegistration).MaximumLength(20);
        RuleFor(x => x.TaxRegime).Must(r => Enum.TryParse<TaxRegime>(r, true, out var v) && Enum.IsDefined(v))
            .WithMessage("Escolha o regime tributário.");
        RuleFor(x => x.Street).NotEmpty().WithMessage("Informe o logradouro.").MaximumLength(200);
        RuleFor(x => x.Number).NotEmpty().WithMessage("Informe o número (ou S/N).").MaximumLength(20);
        RuleFor(x => x.Complement).MaximumLength(100);
        RuleFor(x => x.District).NotEmpty().WithMessage("Informe o bairro.").MaximumLength(100);
        RuleFor(x => x.CityName).NotEmpty().WithMessage("Informe o município.").MaximumLength(100);
        RuleFor(x => x.CityIbgeCode).Must(FiscalCodes.IsIbgeCity).WithMessage("Código IBGE do município: 7 números.");
        RuleFor(x => x.State).Must(s => States.Contains((s ?? "").Trim().ToUpperInvariant())).WithMessage("UF inválida.");
        RuleFor(x => x.ZipCode).Must(z => FiscalCodes.Digits(z).Length == 8).WithMessage("CEP: 8 números.");
    }

    public static bool BeStateRegistration(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Equals("ISENTO", StringComparison.OrdinalIgnoreCase)) return true;
        var digits = FiscalCodes.Digits(v);
        return digits.Length is >= 2 and <= 14;
    }
}

// ── Product fiscal data ─────────────────────────────────────────────────────────

public record ProductFiscalDto(Guid ProductId, string Code, string Name, string? Barcode,
    string? Ncm, string? Cest, int? Origin, string? Cfop, string? TaxSituation);

public record UpdateProductFiscalRequest(string? Ncm, string? Cest, int? Origin, string? Cfop, string? TaxSituation);

public class UpdateProductFiscalRequestValidator : AbstractValidator<UpdateProductFiscalRequest>
{
    public UpdateProductFiscalRequestValidator()
    {
        RuleFor(x => x.Ncm).Must(v => string.IsNullOrWhiteSpace(v) || FiscalCodes.IsNcm(FiscalCodes.Digits(v)))
            .WithMessage("NCM: 8 números.");
        RuleFor(x => x.Cest).Must(v => string.IsNullOrWhiteSpace(v) || FiscalCodes.IsCest(FiscalCodes.Digits(v)))
            .WithMessage("CEST: 7 números.");
        RuleFor(x => x.Origin).InclusiveBetween(0, 8).When(x => x.Origin is not null)
            .WithMessage("Origem da mercadoria: de 0 a 8.");
        RuleFor(x => x.Cfop).Must(v => string.IsNullOrWhiteSpace(v) || FiscalCodes.Digits(v) is { Length: 4 } d && d[0] is '5' or '6' or '7')
            .WithMessage("CFOP de venda: 4 números começando por 5, 6 ou 7.");
        RuleFor(x => x.TaxSituation).Must(v => string.IsNullOrWhiteSpace(v) || FiscalCodes.Digits(v).Length is 2 or 3 && FiscalCodes.Digits(v).Length == v.Trim().Length)
            .WithMessage("CST (2 números) ou CSOSN (3 números).");
    }
}

// ── Readiness ───────────────────────────────────────────────────────────────────

/// <summary>What still stands between this store and issuing notas — data only, nothing is sent anywhere.</summary>
public record FiscalReadinessDto(
    bool ProfileComplete,
    IReadOnlyList<string> ProfileMissing,
    int ActiveProducts,
    int ProductsWithoutNcm,
    int ProductsWithInvalidBarcode,
    IReadOnlyList<FiscalProductIssueDto> ProductsToFix,
    bool EmissionAvailable);

public record FiscalProductIssueDto(Guid ProductId, string Code, string Name, string? Barcode, string? Ncm, IReadOnlyList<string> Problems);
