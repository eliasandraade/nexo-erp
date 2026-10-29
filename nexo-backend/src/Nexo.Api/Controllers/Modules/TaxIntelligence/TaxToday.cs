using Nexo.Application.Common;

namespace Nexo.Api.Controllers.Modules.TaxIntelligence;

// Tax Intelligence API. Permissions map onto the existing role sets (docs/security/rbac-matrix.md):
//   tax.read / tax.calculate / tax.simulate / tax.opportunities.read / .review / tax.credits.manage → Management
//   tax.opportunities.approve and new tax profile versions (a change of regime)                    → Directors
//   tax.rules.manage (catalogue and legal sources)                                                  → platform admin only
// Every tenant-scoped query runs under the tenant + store filter, so an id from another tenant is a 404.

internal static class TaxToday
{
    public static DateOnly Brazil() => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));
}
