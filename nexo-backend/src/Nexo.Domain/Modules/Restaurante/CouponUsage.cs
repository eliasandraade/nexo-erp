using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

public class CouponUsage : StoreEntity
{
    private CouponUsage() { }
    private CouponUsage(Guid tenantId) : base(tenantId) { }

    public Guid     CouponId        { get; private set; }
    public string   CustomerPhone   { get; private set; } = string.Empty;
    public Guid     DeliveryOrderId { get; private set; }
    public DateTime UsedAt          { get; private set; }

    /// <summary>
    /// Factory overload with explicit storeId for portal flow (no JWT store context).
    /// SetStoreId is internal to Nexo.Domain, so this factory can call it directly.
    /// </summary>
    public static CouponUsage Create(
        Guid   tenantId,
        Guid   storeId,
        Guid   couponId,
        string customerPhone,
        Guid   deliveryOrderId)
    {
        var usage = new CouponUsage(tenantId)
        {
            CouponId        = couponId,
            CustomerPhone   = customerPhone,
            DeliveryOrderId = deliveryOrderId,
            UsedAt          = DateTime.UtcNow,
        };
        usage.SetStoreId(storeId);  // internal — same assembly (Nexo.Domain)
        return usage;
    }
}
