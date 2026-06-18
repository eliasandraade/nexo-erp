import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activatePackage,
  addPackageItem,
  createPackage,
  deactivatePackage,
  fetchPackage,
  fetchPackages,
  removePackageItem,
  updatePackage,
  updatePackagePrice,
  type AddPackageItemRequest,
  type CreatePackageRequest,
  type SvcPackageDto,
  type UpdatePackageRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export function usePackages(active?: boolean) {
  return useQuery({
    queryKey: serviceKeys.packagesList(active),
    queryFn: () => fetchPackages(active),
  });
}

export function usePackage(id: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.package(id ?? ""),
    queryFn: () => fetchPackage(id!),
    enabled: !!id,
  });
}

function usePackageMutation<TArgs>(fn: (args: TArgs) => Promise<SvcPackageDto>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (pkg) => {
      qc.setQueryData(serviceKeys.package(pkg.id), pkg);
      qc.invalidateQueries({ queryKey: serviceKeys.packages() });
    },
  });
}

export const useCreatePackage = () =>
  usePackageMutation((body: CreatePackageRequest) => createPackage(body));

export const useUpdatePackage = () =>
  usePackageMutation(({ id, body }: { id: string; body: UpdatePackageRequest }) => updatePackage(id, body));

export const useUpdatePackagePrice = () =>
  usePackageMutation(({ id, price }: { id: string; price: number }) => updatePackagePrice(id, price));

export const useSetPackageActive = () =>
  usePackageMutation(({ id, active }: { id: string; active: boolean }) =>
    active ? activatePackage(id) : deactivatePackage(id));

export const useAddPackageItem = () =>
  usePackageMutation(({ id, body }: { id: string; body: AddPackageItemRequest }) => addPackageItem(id, body));

export const useRemovePackageItem = () =>
  usePackageMutation(({ id, itemId }: { id: string; itemId: string }) => removePackageItem(id, itemId));
