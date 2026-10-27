using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

/// <summary>
/// Golden outputs of engine 1.0.0 on the seeded catalogue. A stored calculation is reproducible only
/// while the engine produces byte-identical output for the same input — if one of these hashes
/// changes, the change altered results and needs a new <see cref="TaxEngineV1.VersionId"/> (and the old
/// evaluator kept for past calculations), not a new pinned value.
/// </summary>
public class TaxEngineGoldenTests
{
    public static TheoryData<string, TaxEngineInput, string> Cases() => new()
    {
        { "regular-2026", Input(), "27ce3268053a7adce993a985a69c226d9c33a74f762f3697dfd3881b5e677591" },
        { "simples-2026", Input(TaxRegime.SimplesNacional), "c5e91a5b6621eb9244e644fa12cc85c6c7b097673abe9ede283a5e83e45d4fcb" },
        { "compliant-special-line", Input(compliant: true, lines: [Line(), Line(2, informed: false, special: "reducao-60")]), "bf8126201e3e1b3a4255f1a0114eda2a4e9e0c7048e259f186ece68e53d995ff" },
        { "inbound-unknown-supplier", Input(direction: TaxDirection.Inbound), "6066bbd9aec56a7ab0b8ee7b0d01d51a5fc768222e190fbbe331492e1891ea04" },
        { "pending-2027", Input(date: new DateOnly(2027, 3, 1)), "1a7c49df7decc5465fe684bcd8a5160c560d48692e803d715927491eaa0a79cb" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Engine_output_is_unchanged(string name, TaxEngineInput input, string expectedHash)
        => TaxCanonicalV1.Instance.HashOf(V1.Run(input, Catalogue())).Should().Be(expectedHash, name);
}
