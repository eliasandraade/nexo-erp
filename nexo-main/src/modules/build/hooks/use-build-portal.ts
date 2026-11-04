import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveClientUpdate, createClientUpdate, editClientUpdate, fetchClientUpdates, fetchPortalCompany,
  fetchProjectPortal, publishClientUpdate, publishProjectPortal, removeProjectPortalPassword,
  setDailyLogClientVisibility, setPhotoClientVisibility, setProjectPortalPassword, unpublishProjectPortal,
  updatePortalCompany, updateProjectPortal,
  type BuildProjectPortalDto, type SaveBuildClientUpdateRequest, type UpdateBuildPortalCompanyRequest,
  type UpdateBuildProjectPortalRequest,
} from "../api/build-portal.api";
import { BUILD_KEYS } from "./use-build";

export const PORTAL_KEYS = {
  company:       ()                  => ["build", "portal", "company"] as const,
  project:       (projectId: string) => ["build", "portal", "project", projectId] as const,
  clientUpdates: (projectId: string) => ["build", "portal", "client-updates", projectId] as const,
} as const;

// ── Company ───────────────────────────────────────────────────────────────────

export function usePortalCompany() {
  return useQuery({ queryKey: PORTAL_KEYS.company(), queryFn: fetchPortalCompany, staleTime: 60_000 });
}

export function useUpdatePortalCompany() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateBuildPortalCompanyRequest) => updatePortalCompany(req),
    onSuccess: (data) => {
      qc.setQueryData(PORTAL_KEYS.company(), data);
      // every project's public path depends on the company slug
      return qc.invalidateQueries({ queryKey: ["build", "portal", "project"] });
    },
  });
}

// ── Project portal ────────────────────────────────────────────────────────────

export function useProjectPortal(projectId: string) {
  return useQuery({
    queryKey:  PORTAL_KEYS.project(projectId),
    queryFn:   () => fetchProjectPortal(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

/** Every portal mutation answers with the fresh portal: put it straight in the cache. */
function usePortalMutation<TVars>(projectId: string, fn: (vars: TVars) => Promise<BuildProjectPortalDto>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess:  (data) => qc.setQueryData(PORTAL_KEYS.project(projectId), data),
  });
}

export const useUpdateProjectPortal = (projectId: string) =>
  usePortalMutation(projectId, (req: UpdateBuildProjectPortalRequest) => updateProjectPortal(projectId, req));

export const useSetPortalPassword = (projectId: string) =>
  usePortalMutation(projectId, (password: string) => setProjectPortalPassword(projectId, password));

export const useRemovePortalPassword = (projectId: string) =>
  usePortalMutation(projectId, () => removeProjectPortalPassword(projectId));

export const usePublishPortal = (projectId: string) =>
  usePortalMutation(projectId, () => publishProjectPortal(projectId));

export const useUnpublishPortal = (projectId: string) =>
  usePortalMutation(projectId, () => unpublishProjectPortal(projectId));

// ── Daily log / photo visibility ──────────────────────────────────────────────

export function useSetDailyLogClientVisibility(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ logId, clientVisible, clientSummary }: { logId: string; clientVisible: boolean; clientSummary: string | null }) =>
      setDailyLogClientVisibility(logId, { clientVisible, clientSummary }),
    onSuccess: () => qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) }),
  });
}

export function useSetPhotoClientVisibility(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ photoId, clientVisible }: { photoId: string; clientVisible: boolean }) =>
      setPhotoClientVisibility(photoId, clientVisible),
    onSuccess: () => qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) }),
  });
}

// ── Client updates ────────────────────────────────────────────────────────────

export function useClientUpdates(projectId: string) {
  return useQuery({
    queryKey:  PORTAL_KEYS.clientUpdates(projectId),
    queryFn:   () => fetchClientUpdates(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

function useClientUpdateMutation<TVars>(projectId: string, fn: (vars: TVars) => Promise<unknown>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess:  () => qc.invalidateQueries({ queryKey: PORTAL_KEYS.clientUpdates(projectId) }),
  });
}

export const useCreateClientUpdate = (projectId: string) =>
  useClientUpdateMutation(projectId, (req: SaveBuildClientUpdateRequest) => createClientUpdate(projectId, req));

export const useEditClientUpdate = (projectId: string) =>
  useClientUpdateMutation(projectId, ({ id, req }: { id: string; req: SaveBuildClientUpdateRequest }) => editClientUpdate(id, req));

export const usePublishClientUpdate = (projectId: string) =>
  useClientUpdateMutation(projectId, (id: string) => publishClientUpdate(id));

export const useArchiveClientUpdate = (projectId: string) =>
  useClientUpdateMutation(projectId, (id: string) => archiveClientUpdate(id));
