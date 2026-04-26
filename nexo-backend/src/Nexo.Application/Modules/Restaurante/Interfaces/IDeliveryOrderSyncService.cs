using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

/// <summary>
/// Sincronização unidirecional: RestOrder.Status → DeliveryOrder.Status.
/// Chamado pelo OrderService após mudanças de status.
/// No-op se não existir DeliveryOrder vinculado ao RestOrder.
/// </summary>
public interface IDeliveryOrderSyncService
{
    Task SyncFromRestOrderAsync(Guid restOrderId, RestOrderStatus newStatus, CancellationToken ct = default);
}
