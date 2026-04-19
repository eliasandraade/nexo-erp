import { apiClient } from "@/services/api-client";
import type { AuditRecord, AuditFilters } from "../types";

interface AuditApiRecord {
  id: string;
  timestamp: string;
  actionType: string;
  severity: string;
  actorName: string | null;
  actorType: string;
  entityType: string;
  entityId: string;
  description: string;
  metadataJson: string | null;
  ipAddress: string | null;
}

export interface AuditStats {
  total: number;
  critical: number;
  warning: number;
  info: number;
}

function mapRecord(r: AuditApiRecord): AuditRecord {
  return {
    id:          r.id,
    timestamp:   r.timestamp,
    actionType:  r.actionType as AuditRecord["actionType"],
    severity:    r.severity as AuditRecord["severity"],
    actor:       r.actorName ?? r.actorType,
    entityType:  r.entityType,
    entityId:    r.entityId,
    description: r.description,
    metadata:    r.metadataJson ? (JSON.parse(r.metadataJson) as Record<string, unknown>) : undefined,
  };
}

export async function fetchAuditRecords(filters?: Partial<AuditFilters>): Promise<AuditRecord[]> {
  const params = new URLSearchParams();
  if (filters?.actionType && filters.actionType !== "all")
    params.set("actionType", filters.actionType);
  if (filters?.severity && filters.severity !== "all")
    params.set("severity", filters.severity);
  if (filters?.actor && filters.actor !== "all")
    params.set("actor", filters.actor);

  const url = `/audit${params.size > 0 ? `?${params}` : ""}`;
  const records = await apiClient.get<AuditApiRecord[]>(url);
  return records.map(mapRecord);
}

export async function fetchAuditStats(): Promise<AuditStats> {
  return apiClient.get<AuditStats>("/audit/stats");
}

export async function fetchAuditActors(): Promise<string[]> {
  const records = await fetchAuditRecords();
  const actors = new Set(records.map((r) => r.actor).filter(Boolean));
  return Array.from(actors).sort();
}
