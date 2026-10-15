using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Features.Fiscal;
using Nexo.Domain.Enums;
using Xunit;

namespace Nexo.UnitTests.Fiscal;

public class FiscalCodesTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("1234567890", false)]
    [InlineData("12ABC345678", false)]
    [InlineData(null, false)]
    public void Cpf(string? value, bool valid) => FiscalCodes.IsCpf(value).Should().Be(valid);

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("00.000.000/0000-00", false)]
    [InlineData("1122233300018", false)]
    [InlineData("12.ABC.345/01DE-35", true)]   // alphanumeric, issued since July 2026
    [InlineData("12abc34501de35", true)]
    [InlineData("12.ABC.345/01DE-36", false)]
    [InlineData("12.ABC.345/01DE-3A", false)]  // check digits are always numeric
    public void Cnpj(string? value, bool valid) => FiscalCodes.IsCnpj(value).Should().Be(valid);

    [Fact]
    public void Documents_are_stored_as_upper_case_letters_and_digits()
        => FiscalCodes.Document("12.abc.345/01de-35").Should().Be("12ABC34501DE35");

    [Theory]
    [InlineData("4006381333931", true)]   // EAN-13
    [InlineData("96385074", true)]        // EAN-8
    [InlineData("036000291452", true)]    // UPC-A
    [InlineData("4006381333932", false)]
    [InlineData("2000000000008", false)]  // in-store code (scale label): goes as SEM GTIN
    [InlineData("00000000", false)]
    [InlineData("SEM GTIN", false)]
    [InlineData("12345", false)]
    public void Gtin(string value, bool valid) => FiscalCodes.IsGtin(value).Should().Be(valid);

    [Theory]
    [InlineData("61091000", true)]
    [InlineData("6109.10.00", false)]
    [InlineData("6109100", false)]
    public void Ncm(string value, bool valid) => FiscalCodes.IsNcm(value).Should().Be(valid);

    [Theory]
    [InlineData("3550308", true)]   // São Paulo
    [InlineData("5300108", true)]   // Brasília
    [InlineData("9900000", false)]
    [InlineData("1900000", false)]  // 19 is not a state
    [InlineData("355030", false)]
    public void IbgeCity(string value, bool valid) => FiscalCodes.IsIbgeCity(value).Should().Be(valid);

    [Fact]
    public void An_ibge_code_belongs_to_one_state()
        => (FiscalCodes.IbgeCityInState("3550308", "sp"), FiscalCodes.IbgeCityInState("3550308", "RJ")).Should().Be((true, false));

    [Fact]
    public void Cest_is_seven_digits() => (FiscalCodes.IsCest("2803800"), FiscalCodes.IsCest("28.038.00")).Should().Be((true, false));

    [Theory]
    [InlineData("123.456.789.110", true)]
    [InlineData("isento", true)]
    [InlineData("P-01100424.3/002", false)]
    [InlineData("abc12", false)]
    [InlineData("1", false)]
    public void State_registration(string value, bool valid) => FiscalRules.IsStateRegistration(value).Should().Be(valid);

    [Theory]
    [InlineData("SimplesNacional", true)]
    [InlineData("lucroreal", true)]
    [InlineData("3", false)]
    [InlineData("SimplesNacional,Mei", false)]
    [InlineData("Imaginario", false)]
    public void Tax_regime_by_name_only(string value, bool valid) => FiscalRules.IsEnumName<TaxRegime>(value).Should().Be(valid);
}
