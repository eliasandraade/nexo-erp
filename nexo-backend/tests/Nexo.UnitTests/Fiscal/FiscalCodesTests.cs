using FluentAssertions;
using Nexo.Application.Common;
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
    [InlineData(null, false)]
    public void Cpf(string? value, bool valid) => FiscalCodes.IsCpf(value).Should().Be(valid);

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("00.000.000/0000-00", false)]
    [InlineData("1122233300018", false)]
    public void Cnpj(string? value, bool valid) => FiscalCodes.IsCnpj(value).Should().Be(valid);

    [Theory]
    [InlineData("4006381333931", true)]   // EAN-13
    [InlineData("96385074", true)]        // EAN-8
    [InlineData("036000291452", true)]    // UPC-A
    [InlineData("4006381333932", false)]
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
    [InlineData("355030", false)]
    public void IbgeCity(string value, bool valid) => FiscalCodes.IsIbgeCity(value).Should().Be(valid);

    [Fact]
    public void Cest_is_seven_digits() => (FiscalCodes.IsCest("2803800"), FiscalCodes.IsCest("28.038.00")).Should().Be((true, false));
}
