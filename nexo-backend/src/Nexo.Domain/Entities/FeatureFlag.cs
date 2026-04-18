using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Catalog of available feature flags. Defines the global default.
/// Override per tenant via TenantFeatureOverride.
///
/// Resolution: TenantFeatureOverride.IsEnabled ?? FeatureFlag.DefaultEnabled
/// </summary>
public class FeatureFlag : BaseEntity
{
    private FeatureFlag() { }

    /// <summary>Unique kebab-case key. Examples: "pdv-desconto-gerente", "restaurante-taxa-servico"</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Global default value. Applies to all tenants without an explicit override.</summary>
    public bool DefaultEnabled { get; private set; }

    /// <summary>Grouping category: "pdv" | "restaurante" | "estoque" | "financeiro" | "geral"</summary>
    public string Category { get; private set; } = "geral";

    public static FeatureFlag Create(
        string key,
        string name,
        string? description = null,
        bool defaultEnabled = false,
        string category = "geral")
    {
        return new FeatureFlag
        {
            Key            = key.Trim().ToLowerInvariant(),
            Name           = name.Trim(),
            Description    = description?.Trim(),
            DefaultEnabled = defaultEnabled,
            Category       = category.Trim().ToLowerInvariant(),
        };
    }

    public void Update(string name, string? description, bool defaultEnabled, string category)
    {
        Name           = name.Trim();
        Description    = description?.Trim();
        DefaultEnabled = defaultEnabled;
        Category       = category.Trim().ToLowerInvariant();
        SetUpdatedAt();
    }

    public void SetDefault(bool enabled)
    {
        DefaultEnabled = enabled;
        SetUpdatedAt();
    }
}
