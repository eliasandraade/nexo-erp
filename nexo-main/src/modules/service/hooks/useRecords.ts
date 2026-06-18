import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createRecord,
  deleteRecord,
  fetchRecords,
  type CreateRecordRequest,
  type SvcRecordContextType,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

/** Records for one context (e.g. a Subject). Both contextType and contextId are required. */
export function useRecords(contextType: SvcRecordContextType, contextId: string | undefined) {
  return useQuery({
    queryKey: serviceKeys.records(contextType, contextId ?? ""),
    queryFn: () => fetchRecords(contextType, contextId!),
    enabled: !!contextId,
  });
}

export function useCreateRecord(contextType: SvcRecordContextType, contextId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRecordRequest) => createRecord(body),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: serviceKeys.records(contextType, contextId) }),
  });
}

export function useDeleteRecord(contextType: SvcRecordContextType, contextId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteRecord(id),
    onSuccess: () =>
      qc.invalidateQueries({ queryKey: serviceKeys.records(contextType, contextId) }),
  });
}
