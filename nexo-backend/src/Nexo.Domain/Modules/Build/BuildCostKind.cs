namespace Nexo.Domain.Modules.Build;

/// <summary>
/// What a budget / template / change order line is: material, labor, service, equipment or other.
/// Legacy budget items (before Wave 2) are <see cref="Other"/>; their free-text category is kept.
/// </summary>
public enum BuildCostKind
{
    Material  = 1,
    Labor     = 2,
    Service   = 3,
    Equipment = 4,
    Other     = 5,
}

/// <summary>Shared money/text helpers of the Build commercial entities.</summary>
public static class BuildMoney
{
    /// <summary>Money is always kept with 2 decimal places (half away from zero, like the rest of the ERP).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Quantities keep 3 decimal places (column numeric(12,3)).</summary>
    public static decimal RoundQuantity(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    public static string? Clean(string? text, int max, string field)
    {
        var t = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (t is not null && t.Length > max)
            throw new Exceptions.DomainException($"{field} pode ter até {max} caracteres.");
        return t;
    }

    public static string Required(string? text, int max, string field)
        => Clean(text, max, field) ?? throw new Exceptions.DomainException($"{field} é obrigatório.");

    public static BuildCostKind Kind(BuildCostKind kind)
        => Enum.IsDefined(kind) ? kind : throw new Exceptions.DomainException("Tipo de item inválido.");
}
