import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createPayment,
  fetchCustomerPackagePaymentSummary,
  fetchOrderPaymentSummary,
  fetchPayments,
  voidPayment,
  type CreatePaymentRequest,
  type SvcPaymentMethod,
  type SvcPaymentStatus,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface PaymentsFilter {
  customerId?: string;
  orderId?: string;
  customerPackageId?: string;
  method?: SvcPaymentMethod;
  status?: SvcPaymentStatus;
  from?: string;
  to?: string;
}

export function usePayments(filter: PaymentsFilter = {}) {
  return useQuery({
    queryKey: serviceKeys.paymentsList(filter),
    queryFn: () => fetchPayments(filter),
  });
}

export function useOrderPaymentSummary(orderId: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.paymentSummary("order", orderId ?? ""),
    queryFn: () => fetchOrderPaymentSummary(orderId!),
    enabled: !!orderId,
  });
}

export function useCustomerPackagePaymentSummary(customerPackageId: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.paymentSummary("customer-package", customerPackageId ?? ""),
    queryFn: () => fetchCustomerPackagePaymentSummary(customerPackageId!),
    enabled: !!customerPackageId,
  });
}

export function useCreatePayment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreatePaymentRequest) => createPayment(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.payments() }),
  });
}

export function useVoidPayment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason?: string | null }) => voidPayment(id, reason),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.payments() }),
  });
}
