import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateProfessional,
  createProfessional,
  deactivateProfessional,
  fetchProfessionals,
  updateProfessional,
  type SaveProfessionalRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export function useProfessionals(onlyActive = false) {
  return useQuery({
    queryKey: serviceKeys.professionalsList(onlyActive),
    queryFn: () => fetchProfessionals(onlyActive),
  });
}

export function useCreateProfessional() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveProfessionalRequest) => createProfessional(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.professionals() }),
  });
}

export function useUpdateProfessional(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveProfessionalRequest) => updateProfessional(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.professionals() }),
  });
}

export function useSetProfessionalActive() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? activateProfessional(id) : deactivateProfessional(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.professionals() }),
  });
}
