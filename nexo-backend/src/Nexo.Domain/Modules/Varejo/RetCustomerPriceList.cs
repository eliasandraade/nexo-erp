using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Varejo;

/// <summary>
/// Vínculo entre um cliente e uma lista de preço.
/// Um cliente pode ter no máximo uma lista vinculada.
/// </summary>
public class RetCustomerPriceList : TenantEntity
{
    private RetCustomerPriceList() { }
    private RetCustomerPriceList(Guid tenantId) : base(tenantId) { }

    public Guid CustomerId  { get; private set; }
    public Guid PriceListId { get; private set; }

    // Navigation
    public RetPriceList? PriceList { get; private set; }

    public static RetCustomerPriceList Create(Guid tenantId, Guid customerId, Guid priceListId)
    {
        return new RetCustomerPriceList(tenantId)
        {
            CustomerId  = customerId,
            PriceListId = priceListId,
        };
    }
}
