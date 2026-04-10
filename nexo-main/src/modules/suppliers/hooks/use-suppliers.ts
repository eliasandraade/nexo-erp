import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateSupplier,
  createSupplier,
  deactivateSupplier,
  fetchSupplierById,
  fetchSuppliers,
  updateSupplier,
} from "../api/suppliers.api";
import type { SupplierFormInput } from "../types";

export const SUPPLIERS_KEY = ["suppliers"] as const;

export function useSuppliers(includeInactive = false) {
  return useQuery({
    queryKey: [...SUPPLIERS_KEY, { includeInactive }],
    queryFn:  () => fetchSuppliers(includeInactive),
  });
}

export function useSupplier(id: string | undefined) {
  return useQuery({
    queryKey: [...SUPPLIERS_KEY, id],
    queryFn:  () => fetchSupplierById(id!),
    enabled:  !!id,
  });
}

export function useCreateSupplier() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (form: SupplierFormInput) => createSupplier(form),
    onSuccess:  () => { qc.invalidateQueries({ queryKey: SUPPLIERS_KEY }); },
  });
}

export function useUpdateSupplier(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (form: SupplierFormInput) => updateSupplier(id, form),
    onSuccess:  (updated) => {
      qc.setQueryData([...SUPPLIERS_KEY, id], updated);
      qc.invalidateQueries({ queryKey: SUPPLIERS_KEY });
    },
  });
}

export function useSetSupplierActive(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (active: boolean) =>
      active ? activateSupplier(id) : deactivateSupplier(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: [...SUPPLIERS_KEY, id] });
      qc.invalidateQueries({ queryKey: SUPPLIERS_KEY });
    },
  });
}
