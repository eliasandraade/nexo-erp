import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateCustomer,
  createCustomer,
  deactivateCustomer,
  fetchCustomerById,
  fetchCustomers,
  updateCustomer,
} from "../api/customers.api";
import type { CustomerFormInput } from "../types";

export const CUSTOMERS_KEY = ["customers"] as const;

export function useCustomers(includeInactive = false) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, { includeInactive }],
    queryFn:  () => fetchCustomers(includeInactive),
  });
}

export function useCustomer(id: string | undefined) {
  return useQuery({
    queryKey: [...CUSTOMERS_KEY, id],
    queryFn:  () => fetchCustomerById(id!),
    enabled:  !!id,
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (form: CustomerFormInput) => createCustomer(form),
    onSuccess:  () => { qc.invalidateQueries({ queryKey: CUSTOMERS_KEY }); },
  });
}

export function useUpdateCustomer(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (form: CustomerFormInput) => updateCustomer(id, form),
    onSuccess:  (updated) => {
      qc.setQueryData([...CUSTOMERS_KEY, id], updated);
      qc.invalidateQueries({ queryKey: CUSTOMERS_KEY });
    },
  });
}

export function useSetCustomerActive(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (active: boolean) =>
      active ? activateCustomer(id) : deactivateCustomer(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: [...CUSTOMERS_KEY, id] });
      qc.invalidateQueries({ queryKey: CUSTOMERS_KEY });
    },
  });
}
