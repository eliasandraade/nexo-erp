import type { AuditRecord, AuditFilters } from "../types";
import { mockAuditRecords } from "../data/mockAuditRecords";

/**
 * In-memory audit record store.
 * Seeded with historical mock records; new records are prepended via unshift.
 *
 * Design rules:
 * - addAuditRecord is SYNCHRONOUS — no delay — so it never blocks service operations.
 * - This service has NO imports from other business modules to prevent circular deps.
 * - Services (cash, inventory, pos, users) import audit; audit imports nothing from them.
 */
const records: AuditRecord[] = [...mockAuditRecords];
let auditSeq = mockAuditRecords.length + 1;

const delay = (ms = 200) => new Promise((r) => setTimeout(r, ms));

export const auditService = {
  /**
   * Writes an audit record synchronously.
   * Called from service-layer operations — no async delay intentionally.
   */
  addAuditRecord(input: Omit<AuditRecord, "id" | "timestamp">): void {
    const record: AuditRecord = {
      id: `audit-${String(auditSeq++).padStart(4, "0")}`,
      timestamp: new Date().toISOString(),
      ...input,
    };
    records.unshift(record);
  },

  async listAuditRecords(filters?: AuditFilters): Promise<AuditRecord[]> {
    await delay();
    let result = [...records];
    if (!filters) return result;

    if (filters.actionType !== "all") {
      result = result.filter((r) => r.actionType === filters.actionType);
    }
    if (filters.severity !== "all") {
      result = result.filter((r) => r.severity === filters.severity);
    }
    if (filters.actor && filters.actor !== "all") {
      const q = filters.actor.toLowerCase();
      result = result.filter((r) => r.actor.toLowerCase().includes(q));
    }
    return result;
  },

  async getAuditByEntity(
    entityType: string,
    entityId: string
  ): Promise<AuditRecord[]> {
    await delay(100);
    return records.filter(
      (r) => r.entityType === entityType && r.entityId === entityId
    );
  },

  async listActors(): Promise<string[]> {
    await delay(50);
    const actors = new Set(records.map((r) => r.actor));
    return Array.from(actors).sort();
  },

  async getStats(): Promise<{
    total: number;
    critical: number;
    warning: number;
    info: number;
  }> {
    await delay(50);
    return {
      total: records.length,
      critical: records.filter((r) => r.severity === "critical").length,
      warning: records.filter((r) => r.severity === "warning").length,
      info: records.filter((r) => r.severity === "info").length,
    };
  },
};
