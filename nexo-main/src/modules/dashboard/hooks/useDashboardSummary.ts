import { useQuery } from "@tanstack/react-query";
import { getDashboardSummary } from "../api/dashboard.api";

export const DASHBOARD_SUMMARY_KEY = ["dashboard", "summary"] as const;

export function useDashboardSummary() {
  return useQuery({
    queryKey: DASHBOARD_SUMMARY_KEY,
    queryFn:  getDashboardSummary,
    staleTime: 60_000,  // 1 min — dashboard data doesn't need sub-second freshness
  });
}
