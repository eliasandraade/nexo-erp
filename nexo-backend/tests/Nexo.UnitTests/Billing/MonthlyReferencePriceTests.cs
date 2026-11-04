using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Orken;

namespace Nexo.UnitTests.Billing;

/// <summary>
/// A base do MRR estimado (painel da plataforma e contrato de operações da Orken): preço de tabela em valor mensal.
/// Vitalício, avaliação e cortesia valem zero.
/// </summary>
public class MonthlyReferencePriceTests
{
    private static ModuleDefinition Module() =>
        ModuleDefinition.Create("varejo", "Varejo", priceMonthly: 100m, priceAnnual: 960m, priceLifetime: 3000m);

    [Theory]
    [InlineData(PlanType.Monthly, 100)]
    [InlineData(PlanType.Quarterly, 0)]   // sem preço trimestral na tabela
    [InlineData(PlanType.Semiannual, 0)]  // sem preço semestral na tabela
    [InlineData(PlanType.Annual, 80)]
    [InlineData(PlanType.Lifetime, 0)]
    [InlineData(PlanType.Trial, 0)]
    [InlineData(PlanType.AdminGrant, 0)]
    public void Each_plan_becomes_a_monthly_reference_value(PlanType plan, decimal expected) =>
        Module().MonthlyReferencePrice(plan).Should().Be(expected);

    [Fact]
    public void A_plan_without_a_reference_price_counts_zero() =>
        ModuleDefinition.Create("build", "Build").MonthlyReferencePrice(PlanType.Monthly).Should().Be(0m);

    [Theory]
    [InlineData("Active", "ACTIVE")]
    [InlineData("InProgress", "IN_PROGRESS")]
    [InlineData("Done", "DONE")]
    public void Status_keys_are_upper_snake(string name, string expected) => OrkenOperationsQuery.UpperSnake(name).Should().Be(expected);

    [Theory]
    [InlineData("RuleBased", "RuleBased")]
    [InlineData("academia-musculacao", "academia-musculacao")]
    [InlineData("nome com espaço/<x>", "nome_com_espa_o__x_")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void Free_keys_keep_only_safe_characters(string? raw, string expected) => OrkenOperationsQuery.Key(raw).Should().Be(expected);
}
