import { useQuery } from "@tanstack/react-query";
import { listSalesPaged, type SalesPagedParams } from "../api/sales.api";

export function useSalesList(params: SalesPagedParams) {
  return useQuery({
    queryKey: ["sales", "paged", params],
    queryFn:  () => listSalesPaged(params),
    staleTime: 30_000,
    placeholderData: (prev) => prev,
  });
}
