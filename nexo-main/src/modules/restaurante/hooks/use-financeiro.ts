import { useQuery } from "@tanstack/react-query";
import { fetchCmvReport, fetchFinanceiroSummary } from "../api/financeiro.api";

export const CMV_REPORT_KEY = ["financeiro", "cmv-report"] as const;

export const FINANCEIRO_SUMMARY_KEY = (from: string, to: string) =>
  ["financeiro", "summary", from, to] as const;

/** CMV report — independent of period, reflects current recipe card costs. */
export function useCmvReport() {
  return useQuery({
    queryKey: CMV_REPORT_KEY,
    queryFn:  fetchCmvReport,
    staleTime: 2 * 60_000, // 2 min — costs change rarely
  });
}

/** Financial KPIs for the given period (yyyy-MM-dd strings). */
export function useFinanceiroSummary(from: string, to: string) {
  return useQuery({
    queryKey: FINANCEIRO_SUMMARY_KEY(from, to),
    queryFn:  () => fetchFinanceiroSummary(from, to),
    enabled:  !!from && !!to,
  });
}
