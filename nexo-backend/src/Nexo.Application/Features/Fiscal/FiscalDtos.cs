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
    DateTime UpdatedAt,
    string OperationNature,
    int? NfceSeries,
    long? NfceNextNumber,
    int? NfeSeries,
    long? NfeNextNumber);

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
    string ZipCode,
    string? OperationNature = null,
    int? NfceSeries = null,
    long? NfceNextNumber = null,
    int? NfeSeries = null,
    long? NfeNextNumber = null);

/// <summary>Shared rules of the fiscal forms.</summary>
public static class FiscalRules
{
    public const long MaxNoteNumber = 999_999_999;

    /// <summary>IE: digits (masks allowed) or ISENTO. Letters are refused rather than silently dropped.</summary>
    public static bool IsStateRegistration(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Equals("ISENTO", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Any(c => !(char.IsAsciiDigit(c) || c is '.' or '-' or '/' or ' '))) return false;
        return FiscalCodes.Digits(v).Length is >= 2 and <= 14;
    }

    /// <summary>"ISENTO" or the digits of the IE; null for blank.</summary>
    public static string? NormalizeStateRegistration(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        return v.Equals("ISENTO", StringComparison.OrdinalIgnoreCase) ? "ISENTO" : FiscalCodes.Digits(v);
    }

    /// <summary>An enum by its name only — "3" or "1,2" would otherwise parse as a value.</summary>
    public static bool IsEnumName<T>(string? value) where T : struct, Enum
        => !string.IsNullOrWhiteSpace(value) && !char.IsAsciiDigit(value.Trim()[0]) && !value.Contains(',')
           && Enum.TryParse<T>(value, true, out var v) && Enum.IsDefined(v);
}

public class SaveFiscalProfileRequestValidator : AbstractValidator<SaveFiscalProfileRequest>
{
    public SaveFiscalProfileRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().WithMessage("Informe a razão social.").MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.Cnpj).Must(FiscalCodes.IsCnpj).WithMessage("CNPJ inválido. Confira os números.");
        RuleFor(x => x.StateRegistration).NotEmpty().WithMessage("Informe a inscrição estadual (ou ISENTO).")
            .Must(FiscalRules.IsStateRegistration).WithMessage("Inscrição estadual: só números (até 14) ou ISENTO.");
        RuleFor(x => x.MunicipalRegistration).MaximumLength(20);
        RuleFor(x => x.TaxRegime).Must(FiscalRules.IsEnumName<TaxRegime>).WithMessage("Escolha o regime tributário.");
        RuleFor(x => x.Street).NotEmpty().WithMessage("Informe o logradouro.").MaximumLength(200);
        RuleFor(x => x.Number).NotEmpty().WithMessage("Informe o número (ou S/N).").MaximumLength(20);
        RuleFor(x => x.Complement).MaximumLength(100);
        RuleFor(x => x.District).NotEmpty().WithMessage("Informe o bairro.").MaximumLength(100);
        RuleFor(x => x.CityName).NotEmpty().WithMessage("Informe o município.").MaximumLength(100);
        RuleFor(x => x.State).Must(s => FiscalCodes.IbgeStateCodes.ContainsKey((s ?? "").Trim().ToUpperInvariant())).WithMessage("UF inválida.");
        RuleFor(x => x.CityIbgeCode).Must(FiscalCodes.IsIbgeCity).WithMessage("Código IBGE do município: 7 números.");
        RuleFor(x => x).Must(x => FiscalCodes.IbgeCityInState(x.CityIbgeCode, x.State))
            .When(x => FiscalCodes.IsIbgeCity(x.CityIbgeCode) && FiscalCodes.IbgeStateCodes.ContainsKey((x.State ?? "").Trim().ToUpperInvariant()))
            .WithMessage("O código IBGE não é de um município dessa UF.");
        RuleFor(x => x.ZipCode).Must(z => FiscalCodes.Digits(z).Length == 8).WithMessage("CEP: 8 números.");
        RuleFor(x => x.OperationNature).MaximumLength(60).WithMessage("Natureza da operação: até 60 caracteres.");
        RuleFor(x => x.NfceSeries).InclusiveBetween(0, 889).When(x => x.NfceSeries is not null).WithMessage("Série da NFC-e: de 0 a 889.");
        RuleFor(x => x.NfeSeries).InclusiveBetween(0, 889).When(x => x.NfeSeries is not null).WithMessage("Série da NF-e: de 0 a 889.");
        RuleFor(x => x.NfceNextNumber).InclusiveBetween(1, FiscalRules.MaxNoteNumber).When(x => x.NfceNextNumber is not null)
            .WithMessage("Próximo número da NFC-e: de 1 a 999.999.999.");
        RuleFor(x => x.NfeNextNumber).InclusiveBetween(1, FiscalRules.MaxNoteNumber).When(x => x.NfeNextNumber is not null)
            .WithMessage("Próximo número da NF-e: de 1 a 999.999.999.");
    }
}

// ── Product fiscal data ─────────────────────────────────────────────────────────

public record ProductFiscalDto(Guid ProductId, string Code, string Name, string? Barcode,
    string? Ncm, string? Cest, int? Origin, string? Cfop, string? TaxSituation, string? TaxUnit = null, string? Unit = null);

public record UpdateProductFiscalRequest(string? Ncm, string? Cest, int? Origin, string? Cfop, string? TaxSituation, string? TaxUnit = null);

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
        RuleFor(x => x.TaxSituation).Must(v => string.IsNullOrWhiteSpace(v) || (v.Trim().Length is 2 or 3 && v.Trim().All(char.IsAsciiDigit)))
            .WithMessage("CST (2 números) ou CSOSN (3 números).");
        RuleFor(x => x.TaxUnit).Must(v => string.IsNullOrWhiteSpace(v) || (v.Trim().Length <= 6 && v.Trim().All(char.IsAsciiLetterOrDigit)))
            .WithMessage("Unidade tributável: até 6 letras ou números (ex.: UN, KG).");
    }
}

// ── Customer fiscal data ────────────────────────────────────────────────────────

public record CustomerFiscalDto(Guid CustomerId, string Name, string DocumentType, string DocumentNumber, bool DocumentValid,
    string? StateRegistration, string? TaxpayerIndicator, string? CityIbgeCode);

public record UpdateCustomerFiscalRequest(string? StateRegistration, string? TaxpayerIndicator, string? CityIbgeCode);

public class UpdateCustomerFiscalRequestValidator : AbstractValidator<UpdateCustomerFiscalRequest>
{
    public UpdateCustomerFiscalRequestValidator()
    {
        RuleFor(x => x.StateRegistration).Must(v => string.IsNullOrWhiteSpace(v) || FiscalRules.IsStateRegistration(v))
            .WithMessage("Inscrição estadual: só números (até 14) ou ISENTO.");
        RuleFor(x => x.TaxpayerIndicator).Must(v => string.IsNullOrWhiteSpace(v) || FiscalRules.IsEnumName<TaxpayerIndicator>(v))
            .WithMessage("Indicador de IE: contribuinte, isento ou não contribuinte.");
        RuleFor(x => x)
            .Must(x => !string.Equals(x.TaxpayerIndicator?.Trim(), "ContribuinteIcms", StringComparison.OrdinalIgnoreCase)
                       || FiscalRules.NormalizeStateRegistration(x.StateRegistration) is { } ie && ie != "ISENTO")
            .WithMessage("Contribuinte do ICMS precisa da inscrição estadual (números).");
        RuleFor(x => x.CityIbgeCode).Must(v => string.IsNullOrWhiteSpace(v) || FiscalCodes.IsIbgeCity(v))
            .WithMessage("Código IBGE do município: 7 números.");
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
    bool EmissionAvailable,
    string Provider = "none");

public record FiscalProductIssueDto(Guid ProductId, string Code, string Name, string? Barcode, string? Ncm, IReadOnlyList<string> Problems);
