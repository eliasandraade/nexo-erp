import type { Access, TwinSnapshot } from "../types";
export interface PortalGateway {
  actions?: (tenant: string, project: string) => PortalActions;
  load(tenant: string, project: string, signal: AbortSignal): Promise<Access>;
  unlock(
    tenant: string,
    project: string,
    password: string,
    signal: AbortSignal,
  ): Promise<Access>;
  subscribe?: (listener: (event: Access | "changed") => void) => () => void;
  dispose?: () => void;
}
export interface PortalActions {
  decide?: (id: string, option: string, version?: string) => Promise<void>;
  changeOrder?: (
    id: string,
    decision: "approve" | "reject",
    signer: string,
    comment: string,
  ) => Promise<void>;
  acceptHandover?: (signer: string, comment: string) => Promise<void>;
  requestAssistance?: (input: {
    category: string;
    title?: string;
    description: string;
    location?: string;
    availability?: string;
    files: File[];
  }) => Promise<void>;
  uploadEvidence?: (id: string, files: File[]) => Promise<void>;
  feedback?: (id: string, rating: number, comment: string) => Promise<void>;
  twinState?: (date: string, signal: AbortSignal) => Promise<TwinSnapshot>;
  more?: (section: "timeline" | "photos" | "captures") => Promise<void>;
  refresh?: () => Promise<void>;
  logout?: () => Promise<void>;
}
