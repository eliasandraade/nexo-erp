import { useQuery } from "@tanstack/react-query";
import {
  fetchApiEndpoints,
  fetchAuditLog,
  fetchPlatformHealth,
  fetchPlatformStats,
} from "../services/platformApi";
import type { AuditLogParams } from "../services/platformApi";

export function usePlatformStats() {
  return useQuery({
    queryKey: ["platform", "stats"],
    queryFn: fetchPlatformStats,
    staleTime: 60_000,
  });
}

export function usePlatformHealth() {
  return useQuery({
    queryKey: ["platform", "health"],
    queryFn: fetchPlatformHealth,
    staleTime: 15_000,
    refetchInterval: 30_000, // auto-refresh every 30s
  });
}

export function useApiEndpoints() {
  return useQuery({
    queryKey: ["platform", "endpoints"],
    queryFn: fetchApiEndpoints,
    staleTime: 5 * 60_000, // 5 min — routes don't change often
  });
}

export function useAuditLog(params: AuditLogParams) {
  return useQuery({
    queryKey: ["platform", "audit", params],
    queryFn: () => fetchAuditLog(params),
    staleTime: 15_000,
    placeholderData: (prev) => prev,
  });
}
