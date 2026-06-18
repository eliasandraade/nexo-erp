import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateCatalogItem,
  createCatalogItem,
  deactivateCatalogItem,
  fetchCatalog,
  updateCatalogItem,
  type CreateCatalogItemRequest,
  type UpdateCatalogItemRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export function useCatalog(onlyActive = false) {
  return useQuery({
    queryKey: serviceKeys.catalogList(onlyActive),
    queryFn: () => fetchCatalog(onlyActive),
  });
}

export function useCreateCatalogItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCatalogItemRequest) => createCatalogItem(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.catalog() }),
  });
}

export function useUpdateCatalogItem(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateCatalogItemRequest) => updateCatalogItem(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.catalog() }),
  });
}

export function useSetCatalogItemActive() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? activateCatalogItem(id) : deactivateCatalogItem(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.catalog() }),
  });
}
