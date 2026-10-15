namespace Nexo.Domain.Enums;

/// <summary>Indicador da IE do destinatário, como a NF-e escreve (indIEDest).</summary>
public enum TaxpayerIndicator
{
    ContribuinteIcms = 1,
    Isento = 2,
    NaoContribuinte = 9,
}
