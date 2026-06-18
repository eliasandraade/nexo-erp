import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  assignCustomerPackage,
  cancelCustomerPackage,
  consumeCustomerPackage,
  fetchCustomerPackage,
  fetchCustomerPackages,
  type AssignCustomerPackageRequest,
  type ConsumePackageRequest,
  type SvcCustomerPackageDto,
  type SvcCustomerPackageStatus,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface CustomerPackagesFilter {
  customerId?: string;
  subjectId?: string;
  status?: SvcCustomerPackageStatus;
  packageId?: string;
}

export function useCustomerPackages(filter: CustomerPackagesFilter = {}) {
  return useQuery({
    queryKey: serviceKeys.customerPackagesList(filter),
    queryFn: () => fetchCustomerPackages(filter),
  });
}

export function useCustomerPackage(id: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.customerPackage(id ?? ""),
    queryFn: () => fetchCustomerPackage(id!),
    enabled: !!id,
  });
}

function useCustomerPackageMutation<TArgs>(fn: (args: TArgs) => Promise<SvcCustomerPackageDto>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (cp) => {
      qc.setQueryData(serviceKeys.customerPackage(cp.id), cp);
      qc.invalidateQueries({ queryKey: serviceKeys.customerPackages() });
    },
  });
}

export const useAssignCustomerPackage = () =>
  useCustomerPackageMutation((body: AssignCustomerPackageRequest) => assignCustomerPackage(body));

export const useCancelCustomerPackage = () =>
  useCustomerPackageMutation((id: string) => cancelCustomerPackage(id));

export const useConsumeCustomerPackage = () =>
  useCustomerPackageMutation(({ id, body }: { id: string; body: ConsumePackageRequest }) =>
    consumeCustomerPackage(id, body));
