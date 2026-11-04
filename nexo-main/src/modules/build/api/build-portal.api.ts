import { apiClient } from "@/services/api-client";
import type { BuildDailyLogDto, BuildDailyLogPhotoDto } from "./build.api";

/** Public address of the client portal (served by the external site). */
export const PORTAL_PUBLIC_ORIGIN = "https://www.orken.com.br";

export type BuildPortalAccess = "Public" | "Password";
export type BuildClientUpdateKind = "Update" | "Milestone" | "WeeklyDigest";
export type BuildClientUpdateStatus = "Draft" | "Published" | "Archived";

/** What the client portal of a project shows. Every flag the PUT omits takes the safe default. */
export interface BuildPortalVisibility {
  overallProgress:       boolean;
  stageProgress:         boolean;
  schedule:              boolean;
  forecastDelivery:      boolean;
  dailyLogs:             boolean;
  photos:                boolean;
  videos:                boolean;
  documents:             boolean;
  weather:               boolean;
  financialSummary:      boolean;
  financialCategories:   boolean;
  financialTransactions: boolean;
  receipts:              boolean;
  model3d:               boolean;
  realityCapture:        boolean;
  clientUpdates:         boolean;
  risks:                 boolean;
  team:                  boolean;
  contact:               boolean;
  /** Wave 2: change orders sent/decided (deciding needs a password portal). */
  changeOrders:          boolean;
  /** Wave 2: the client's selections (choosing needs a password portal). */
  selections:            boolean;
  /** Wave 2: approved measurements (values only together with financialSummary). */
  measurements:          boolean;
  /** Wave 3: "Meu Imóvel" — handover, warranty and technical assistance (writes need a password portal). */
  postConstruction:      boolean;
}

/** Mirrors the server's safe defaults (BuildPortalVisibility.Defaults). */
export const DEFAULT_PORTAL_VISIBILITY: BuildPortalVisibility = {
  overallProgress: true, stageProgress: true, schedule: true, forecastDelivery: false,
  dailyLogs: false, photos: true, videos: false, documents: false, weather: true,
  financialSummary: false, financialCategories: false, financialTransactions: false, receipts: false,
  model3d: false, realityCapture: false, clientUpdates: true, risks: false, team: false, contact: true,
  changeOrders: true, selections: true, measurements: false, postConstruction: true,
};

export interface BuildPortalCompanyDto {
  configured:   boolean;
  slug:         string | null;
  displayName:  string | null;
  logoKey:      string | null;
  logoUrl:      string | null;
  contactEmail: string | null;
  contactPhone: string | null;
}

export interface UpdateBuildPortalCompanyRequest {
  slug:          string;
  displayName:   string;
  logoKey?:      string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
}

/** Never carries the password or its hash — only `hasPassword`. */
export interface BuildProjectPortalDto {
  projectId:       string;
  exists:          boolean;
  slug:            string | null;
  companySlug:     string | null;
  /** "/{companySlug}/{projectSlug}" when both slugs exist. */
  publicPath:      string | null;
  isPublished:     boolean;
  publishedAt:     string | null;
  access:          BuildPortalAccess;
  hasPassword:     boolean;
  isLocked:        boolean;
  title:           string | null;
  welcomeMessage:  string | null;
  logoKey:         string | null;
  logoUrl:         string | null;
  coverKey:        string | null;
  coverUrl:        string | null;
  primaryColor:    string | null;
  accentColor:     string | null;
  contactName:     string | null;
  contactPhone:    string | null;
  contactEmail:    string | null;
  contactWhatsApp: string | null;
  visibility:      BuildPortalVisibility;
}

/** PUT replaces everything: always send the current values of the fields you are not editing. */
export interface UpdateBuildProjectPortalRequest {
  slug:            string | null;
  access:          BuildPortalAccess;
  title:           string | null;
  welcomeMessage:  string | null;
  logoKey:         string | null;
  coverKey:        string | null;
  primaryColor:    string | null;
  accentColor:     string | null;
  contactName:     string | null;
  contactPhone:    string | null;
  contactEmail:    string | null;
  contactWhatsApp: string | null;
  visibility:      BuildPortalVisibility;
}

export interface BuildClientUpdateDto {
  id:            string;
  projectId:     string;
  kind:          BuildClientUpdateKind;
  title:         string;
  body:          string;
  status:        BuildClientUpdateStatus;
  publishedAt:   string | null;
  source:        string;
  createdByName: string;
  createdAt:     string;
  updatedAt:     string;
}

export interface SaveBuildClientUpdateRequest {
  title: string;
  body:  string;
  kind?: BuildClientUpdateKind;
}

// ── Company ───────────────────────────────────────────────────────────────────

export const fetchPortalCompany = (): Promise<BuildPortalCompanyDto> =>
  apiClient.get(`/v1/build/portal/company`);

export const updatePortalCompany = (req: UpdateBuildPortalCompanyRequest): Promise<BuildPortalCompanyDto> =>
  apiClient.put(`/v1/build/portal/company`, req);

// ── Project portal ────────────────────────────────────────────────────────────

export const fetchProjectPortal = (projectId: string): Promise<BuildProjectPortalDto> =>
  apiClient.get(`/v1/build/projects/${projectId}/portal`);

export const updateProjectPortal = (projectId: string, req: UpdateBuildProjectPortalRequest): Promise<BuildProjectPortalDto> =>
  apiClient.put(`/v1/build/projects/${projectId}/portal`, req);

export const setProjectPortalPassword = (projectId: string, password: string): Promise<BuildProjectPortalDto> =>
  apiClient.put(`/v1/build/projects/${projectId}/portal/password`, { password });

export const removeProjectPortalPassword = (projectId: string): Promise<BuildProjectPortalDto> =>
  apiClient.delete(`/v1/build/projects/${projectId}/portal/password`);

export const publishProjectPortal = (projectId: string): Promise<BuildProjectPortalDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/portal/publish`);

export const unpublishProjectPortal = (projectId: string): Promise<BuildProjectPortalDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/portal/unpublish`);

// ── What of the daily log the client sees ─────────────────────────────────────

export const setDailyLogClientVisibility = (
  logId: string,
  req: { clientVisible: boolean; clientSummary: string | null },
): Promise<BuildDailyLogDto> =>
  apiClient.put(`/v1/build/daily-logs/${logId}/client-visibility`, req);

export const setPhotoClientVisibility = (photoId: string, clientVisible: boolean): Promise<BuildDailyLogPhotoDto> =>
  apiClient.put(`/v1/build/daily-log-photos/${photoId}/client-visibility`, { clientVisible });

// ── Client updates ────────────────────────────────────────────────────────────

export const fetchClientUpdates = (projectId: string): Promise<BuildClientUpdateDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/client-updates`);

export const createClientUpdate = (projectId: string, req: SaveBuildClientUpdateRequest): Promise<BuildClientUpdateDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/client-updates`, req);

export const editClientUpdate = (id: string, req: SaveBuildClientUpdateRequest): Promise<BuildClientUpdateDto> =>
  apiClient.put(`/v1/build/client-updates/${id}`, req);

export const publishClientUpdate = (id: string): Promise<BuildClientUpdateDto> =>
  apiClient.post(`/v1/build/client-updates/${id}/publish`);

export const archiveClientUpdate = (id: string): Promise<BuildClientUpdateDto> =>
  apiClient.post(`/v1/build/client-updates/${id}/archive`);
