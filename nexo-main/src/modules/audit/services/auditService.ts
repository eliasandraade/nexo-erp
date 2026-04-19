import type { AuditRecord, AuditFilters } from "../types";
import { fetchAuditRecords, fetchAuditStats, fetchAuditActors, type AuditStats } from "../api/audit.api";

export const auditService = {
  /** @deprecated Backend handles all audit writes. This is a no-op. */
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  addAuditRecord(_input: unknown): void {
    // no-op — audit records are written server-side
  },

  async listAuditRecords(filters?: AuditFilters): Promise<AuditRecord[]> {
    return fetchAuditRecords(filters);
  },

  async getAuditByEntity(entityType: string, entityId: string): Promise<AuditRecord[]> {
    const all = await fetchAuditRecords();
    return all.filter((r) => r.entityType === entityType && r.entityId === entityId);
  },

  async listActors(): Promise<string[]> {
    return fetchAuditActors();
  },

  async getStats(): Promise<AuditStats> {
    return fetchAuditStats();
  },
};
