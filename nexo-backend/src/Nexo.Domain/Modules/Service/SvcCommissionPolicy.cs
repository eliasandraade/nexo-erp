namespace Nexo.Domain.Modules.Service;

/// <summary>
/// The commission rules shared by every trigger (order item, appointment, package usage), kept in
/// one place so the three paths can never disagree.
/// </summary>
public static class SvcCommissionPolicy
{
    /// <summary>
    /// Rate that applies to a service: the catalog item's own rate first, then the professional's
    /// default; null when neither exists. A catalog rate of 0 is an explicit "this service pays no
    /// commission" and is NOT overridden by the professional default.
    /// </summary>
    public static decimal? ResolvePercent(decimal? catalogPercent, decimal? professionalDefaultPercent)
        => catalogPercent ?? professionalDefaultPercent;

    /// <summary>True when the rate actually earns something — null and 0 never create an entry.</summary>
    public static bool Earns(decimal? percent) => percent is > 0m;

    /// <summary>
    /// Value of <paramref name="quantity"/> units of a customer package, prorated evenly over all the
    /// units the package includes. Uses only snapshotted values (price and quantities frozen when
    /// the package was assigned), so a later catalog or package edit never moves the base.
    /// </summary>
    public static decimal ProratePackageValue(decimal packagePrice, decimal totalUnits, decimal quantity)
    {
        if (totalUnits <= 0m || quantity <= 0m || packagePrice <= 0m) return 0m;
        return Math.Round(packagePrice * quantity / totalUnits, 2, MidpointRounding.AwayFromZero);
    }
}
