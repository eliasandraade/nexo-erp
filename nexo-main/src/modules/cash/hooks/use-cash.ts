import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchOpenSession,
  fetchSessionById,
  fetchAllSessions,
  openSession,
  closeSession,
  addMovement,
} from "../api/cash.api";
import type {
  OpenCashSessionRequest,
  CloseCashSessionRequest,
  AddCashMovementRequest,
} from "../types";

export const CASH_KEY = ["cash"] as const;

export function useOpenSession() {
  return useQuery({
    queryKey: [...CASH_KEY, "open"],
    queryFn: fetchOpenSession,
  });
}

export function useSessionById(id: string | undefined) {
  return useQuery({
    queryKey: [...CASH_KEY, "session", id],
    queryFn: () => fetchSessionById(id!),
    enabled: !!id,
  });
}

export function useAllSessions() {
  return useQuery({
    queryKey: [...CASH_KEY, "history"],
    queryFn: fetchAllSessions,
  });
}

export function useOpenCashSession() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: OpenCashSessionRequest) => openSession(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CASH_KEY });
    },
  });
}

export function useCloseCashSession() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: CloseCashSessionRequest }) =>
      closeSession(id, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CASH_KEY });
    },
  });
}

export function useAddCashMovement() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: AddCashMovementRequest }) =>
      addMovement(id, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CASH_KEY });
    },
  });
}
