import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  closeCommissionPayout,
  fetchCommissionEntries,
  fetchCommissionPayouts,
  fetchCommissionSummary,
  markCommissionPayoutPaid,
  type CloseCommissionPayoutRequest,
  type CommissionEntryStatusFilter,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface CommissionEntriesFilter {
  professionalId?: string;
  from?: string;
  to?: string;
  status?: CommissionEntryStatusFilter;
  payoutId?: string;
}

export function useCommissionEntries(filter: CommissionEntriesFilter, enabled = true) {
  return useQuery({
    queryKey: serviceKeys.commissionEntries({ ...filter }),
    queryFn: () => fetchCommissionEntries(filter),
    enabled,
  });
}

export function useCommissionSummary(professionalId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: serviceKeys.commissionSummary(professionalId),
    queryFn: () => fetchCommissionSummary(professionalId),
    enabled,
  });
}

export function useCommissionPayouts(professionalId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: serviceKeys.commissionPayouts({ professionalId }),
    queryFn: () => fetchCommissionPayouts({ professionalId }),
    enabled,
  });
}

/** Both mutations change entries, payouts and the summary at once — refresh the whole branch. */
export function useCloseCommissionPayout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CloseCommissionPayoutRequest) => closeCommissionPayout(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.commissions() }),
  });
}

export function useMarkCommissionPayoutPaid() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, paidAt }: { id: string; paidAt?: string | null }) => markCommissionPayoutPaid(id, paidAt),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.commissions() }),
  });
}
