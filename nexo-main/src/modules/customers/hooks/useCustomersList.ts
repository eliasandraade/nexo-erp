import { useQuery } from "@tanstack/react-query";
import { fetchCustomersPaged, type CustomersPagedParams } from "../api/customers.api";

export function useCustomersList(params: CustomersPagedParams) {
  return useQuery({
    queryKey: ["customers", "paged", params],
    queryFn:  () => fetchCustomersPaged(params),
    staleTime: 30_000,
    placeholderData: (prev) => prev,
  });
}
