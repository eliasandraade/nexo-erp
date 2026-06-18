import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateSubject,
  createSubject,
  deactivateSubject,
  fetchSubjects,
  updateSubject,
  type CreateSubjectRequest,
  type SvcSubjectKind,
  type UpdateSubjectRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface SubjectsFilter {
  customerId?: string;
  kind?: SvcSubjectKind;
  active?: boolean;
}

export function useSubjects(params: SubjectsFilter = {}) {
  return useQuery({
    queryKey: serviceKeys.subjectsList(params),
    queryFn: () => fetchSubjects(params),
  });
}

export function useCreateSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSubjectRequest) => createSubject(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.subjects() }),
  });
}

export function useUpdateSubject(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateSubjectRequest) => updateSubject(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.subjects() }),
  });
}

export function useSetSubjectActive() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? activateSubject(id) : deactivateSubject(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.subjects() }),
  });
}
