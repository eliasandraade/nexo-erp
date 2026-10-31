import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchPlatformBuildJobs, fetchPlatformBuildOverview, retryPlatformBuildJob, type PlatformBuildJobStatus,
} from "../services/platformApi";

export const PLATFORM_BUILD_KEYS = {
  all:      ["platform", "build"] as const,
  overview: () => ["platform", "build", "overview"] as const,
  jobs:     (status: PlatformBuildJobStatus) => ["platform", "build", "jobs", status] as const,
};

export function usePlatformBuildOverview() {
  return useQuery({ queryKey: PLATFORM_BUILD_KEYS.overview(), queryFn: fetchPlatformBuildOverview, staleTime: 30_000 });
}

export function usePlatformBuildJobs(status: PlatformBuildJobStatus = "Failed") {
  return useQuery({ queryKey: PLATFORM_BUILD_KEYS.jobs(status), queryFn: () => fetchPlatformBuildJobs(status), staleTime: 15_000 });
}

export function useRetryPlatformBuildJob() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => retryPlatformBuildJob(jobId),
    onSuccess: () => qc.invalidateQueries({ queryKey: PLATFORM_BUILD_KEYS.all }),
  });
}
