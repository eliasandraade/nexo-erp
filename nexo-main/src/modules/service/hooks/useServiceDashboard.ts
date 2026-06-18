import { useQuery } from "@tanstack/react-query";
import {
  fetchAppointments,
  fetchCustomerPackages,
  fetchOrders,
  fetchPayments,
  type ServiceCapabilities,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

function todayRange() {
  const now = new Date();
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0, 0);
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 23, 59, 59, 999);
  return { from: start.toISOString(), to: end.toISOString() };
}

/**
 * Dashboard data, gated by the resolved capabilities so a vertical only fetches the sources it
 * actually uses (no orders endpoint hit for a pure-appointment clinic, etc.). Reuses the shared
 * query keys, so the cards stay in sync with the list pages.
 */
export function useServiceDashboard(capabilities: ServiceCapabilities | undefined) {
  const { from, to } = todayRange();
  const hasPaymentTarget = !!(capabilities?.orders || capabilities?.packages);

  const appointments = useQuery({
    queryKey: serviceKeys.appointmentsList({ from, to }),
    queryFn: () => fetchAppointments({ from, to }),
    enabled: !!capabilities?.appointments,
  });

  const orders = useQuery({
    queryKey: serviceKeys.ordersList({}),
    queryFn: () => fetchOrders({}),
    enabled: !!capabilities?.orders,
  });

  const payments = useQuery({
    queryKey: serviceKeys.paymentsList({ status: "Paid", from, to }),
    queryFn: () => fetchPayments({ status: "Paid", from, to }),
    enabled: hasPaymentTarget,
  });

  const customerPackages = useQuery({
    queryKey: serviceKeys.customerPackagesList({ status: "Active" }),
    queryFn: () => fetchCustomerPackages({ status: "Active" }),
    enabled: !!capabilities?.packages,
  });

  return { appointments, orders, payments, customerPackages };
}
