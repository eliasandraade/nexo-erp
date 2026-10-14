namespace Nexo.Api.Authorization;

/// <summary>
/// Role sets used by <c>[Authorize(Roles = ...)]</c> across the API — the backend half of the
/// frontend route map (routes.ts). Role checks combine with AND when stacked on a controller and an
/// action, so controllers whose actions need different sets declare the role per action.
/// See docs/security/rbac-matrix.md for the full area × action matrix.
/// </summary>
public static class AppRoles
{
    /// <summary>Owner level: users, audit trail.</summary>
    public const string Directors = "Diretoria";

    /// <summary>Full management: money, reports, settings, configuration of every module.</summary>
    public const string Management = "Gerente,Diretoria";

    /// <summary>Selling at the PDV: create/confirm sales, read the open cash session.</summary>
    public const string Sales = "Gerente,Diretoria,Vendedor";

    /// <summary>Catalog and inventory upkeep: products, categories, stock adjustments, purchases.</summary>
    public const string Stock = "Gerente,Diretoria,Estoquista";

    /// <summary>Stock levels: kept by stock, read by the PDV and the salão to flag what is out of stock.</summary>
    public const string StockReaders = "Gerente,Diretoria,Estoquista,Vendedor";

    /// <summary>Restaurant floor service: tables, comandas, delivery, payment of comandas.</summary>
    public const string Floor = "Gerente,Diretoria,Vendedor";

    /// <summary>Everyone running the restaurant (floor + kitchen): reading orders, menu and settings.</summary>
    public const string RestaurantOps = "Gerente,Diretoria,Vendedor,Cozinha";

}
