import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addOrderItem,
  changeOrderStatus,
  createOrder,
  createOrderFromAppointment,
  fetchOrder,
  fetchOrders,
  removeOrderItem,
  updateOrder,
  updateOrderItem,
  type AddOrderItemRequest,
  type CreateOrderRequest,
  type SvcOrderDto,
  type SvcOrderStatus,
  type UpdateOrderItemRequest,
  type UpdateOrderRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface OrdersFilter {
  status?: SvcOrderStatus;
  customerId?: string;
  subjectId?: string;
  professionalId?: string;
  appointmentId?: string;
}

export function useOrders(filter: OrdersFilter = {}) {
  return useQuery({
    queryKey: serviceKeys.ordersList(filter),
    queryFn: () => fetchOrders(filter),
  });
}

export function useOrder(id: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.order(id ?? ""),
    queryFn: () => fetchOrder(id!),
    enabled: !!id,
  });
}

function useOrderMutation<TArgs>(fn: (args: TArgs) => Promise<SvcOrderDto>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (order) => {
      qc.setQueryData(serviceKeys.order(order.id), order);
      qc.invalidateQueries({ queryKey: serviceKeys.orders() });
    },
  });
}

export const useCreateOrder = () =>
  useOrderMutation((body: CreateOrderRequest) => createOrder(body));

export const useCreateOrderFromAppointment = () =>
  useOrderMutation((appointmentId: string) => createOrderFromAppointment(appointmentId));

export const useUpdateOrder = () =>
  useOrderMutation(({ id, body }: { id: string; body: UpdateOrderRequest }) => updateOrder(id, body));

export const useChangeOrderStatus = () =>
  useOrderMutation(({ id, status, reason }: { id: string; status: SvcOrderStatus; reason?: string | null }) =>
    changeOrderStatus(id, { status, reason }));

export const useAddOrderItem = () =>
  useOrderMutation(({ id, body }: { id: string; body: AddOrderItemRequest }) => addOrderItem(id, body));

export const useUpdateOrderItem = () =>
  useOrderMutation(({ id, itemId, body }: { id: string; itemId: string; body: UpdateOrderItemRequest }) =>
    updateOrderItem(id, itemId, body));

export const useRemoveOrderItem = () =>
  useOrderMutation(({ id, itemId }: { id: string; itemId: string }) => removeOrderItem(id, itemId));
