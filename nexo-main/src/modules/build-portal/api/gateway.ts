import type { Access } from "../types";
// TEMPORARY CONTRACT: no HTTP routes until backend publishes its contract.
export interface PortalGateway {
  actions?: (tenant: string, project: string) => PortalActions;
  load(tenant: string, project: string, signal: AbortSignal): Promise<Access>;
  unlock(
    tenant: string,
    project: string,
    password: string,
    signal: AbortSignal,
  ): Promise<Access>;
}
// No speculative endpoint, admin token, persisted password or production fixture.
export const unavailableGateway: PortalGateway = {
  load: async () => ({ kind: "unavailable" }),
  unlock: async () => ({ kind: "unavailable" }),
};

export interface PortalActions {
  decide?: (id: string, option: string, version: string) => Promise<void>;
  requestAssistance?: (input: {
    category: string;
    description: string;
    availability: string;
    files: File[];
  }) => Promise<void>;
}
