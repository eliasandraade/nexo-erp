import { apiClient } from "@/services/api-client";
import type {
  CashSessionDto,
  CashMovementDto,
  OpenCashSessionRequest,
  CloseCashSessionRequest,
  AddCashMovementRequest,
} from "../types";

export function fetchOpenSession(): Promise<CashSessionDto | null> {
  return apiClient.get<CashSessionDto | null>("/api/cash/sessions/open");
}

export function fetchSessionById(id: string): Promise<CashSessionDto> {
  return apiClient.get<CashSessionDto>(`/api/cash/sessions/${id}`);
}

export function fetchAllSessions(): Promise<CashSessionDto[]> {
  return apiClient.get<CashSessionDto[]>("/api/cash/sessions");
}

export function openSession(req: OpenCashSessionRequest): Promise<CashSessionDto> {
  return apiClient.post<CashSessionDto>("/api/cash/sessions/open", req);
}

export function closeSession(id: string, req: CloseCashSessionRequest): Promise<CashSessionDto> {
  return apiClient.post<CashSessionDto>(`/api/cash/sessions/${id}/close`, req);
}

export function addMovement(id: string, req: AddCashMovementRequest): Promise<CashMovementDto> {
  return apiClient.post<CashMovementDto>(`/api/cash/sessions/${id}/movements`, req);
}
