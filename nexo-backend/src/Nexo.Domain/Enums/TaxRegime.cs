namespace Nexo.Domain.Enums;

/// <summary>Regime tributário da empresa, como o contador informa.</summary>
public enum TaxRegime
{
    SimplesNacional = 1,
    SimplesNacionalExcessoSublimite = 2,
    Mei = 3,
    LucroPresumido = 4,
    LucroReal = 5,
}
