import { useQuery } from "@tanstack/react-query";
import { fetchSuppliersPaged, type SuppliersPagedParams } from "../api/suppliers.api";

export function useSuppliersList(params: SuppliersPagedParams) {
  return useQuery({
    queryKey: ["suppliers", "paged", params],
    queryFn:  () => fetchSuppliersPaged(params),
    staleTime: 30_000,
    placeholderData: (prev) => prev,
  });
}
