// TEMPORARY CONTRACT: frontend presentation model, awaiting Claude/OpenAPI.
/** Presentation model, NOT an agreed backend DTO. Map only authorized public responses here. */
export type Phase = "waiting" | "active" | "complete" | "delayed" | "blocked";
export interface Photo {
  kind?: "image" | "video";
  zone?: string;
  id: string;
  url: string;
  thumbnail?: string;
  caption: string;
  date: string;
  stage?: string;
}
export interface Portal {
  visibility?: "public" | "private";
  indexable?: boolean;
  weather?: { date: string; description: string; impact?: string }[];
  captures?: {
    id: string;
    name: string;
    date: string;
    floor: string;
    url: string;
    hotspots: { name: string; targetId: string }[];
  }[];
  model?: TwinModel;
  company: string;
  name: string;
  logo?: string;
  cover?: string;
  color?: string;
  secondaryColor?: string;
  contact?: string;
  progress: number | null;
  planned: number | null;
  updatedAt: string | null;
  delivery: string | null;
  lifecycle: "building" | "completed" | "handover";
  forecast: string | null;
  stages: {
    id: string;
    name: string;
    status: Phase;
    progress: number | null;
    date?: string;
  }[];
  updates: {
    id: string;
    title: string;
    body: string;
    publishedAt: string;
    planned: boolean;
  }[];
  weekly: { body: string; publishedAt: string } | null;
  photos: Photo[];
  documents: { id: string; name: string; category: string; url: string }[];
  finance: null | {
    level: "summary" | "categories" | "full";
    budget: number | null;
    executed: number | null;
    categories: { name: string; amount: number }[];
    transactions: {
      id: string;
      date: string;
      description: string;
      amount: number;
      receipt?: string;
    }[];
  };
  decisions: {
    id: string;
    version: string;
    title: string;
    due: string;
    status: "pending" | "confirmed";
    selected?: string;
    options: {
      id: string;
      name: string;
      description: string;
      extra: number | null;
      days: number | null;
    }[];
  }[];
  warranties: { id: string; name: string; until: string }[];
  assistance: null | {
    categories: string[];
    tickets: { id: string; title: string; status: string }[];
  };
}
export interface TwinModel {
  status: "ready" | "processing" | "unavailable";
  url?: string;
  elements: {
    id: string;
    node: string;
    name: string;
    floor: string;
    stage: string;
    status: Phase;
    completedAt?: string;
    category?: string;
    parentId?: string;
    photos?: Photo[];
    updateId?: string;
  }[];
  snapshots: { id: string; date: string; states: Record<string, Phase> }[];
}
export type Access =
  | { kind: "ready"; portal: Portal }
  | {
      kind:
        | "private"
        | "expired"
        | "wrong-password"
        | "rate-limited"
        | "unpublished"
        | "not-found"
        | "unavailable";
      retryAt?: string;
      publicIdentity?: Pick<Portal, "company" | "name" | "logo">;
    };
export function percent(value: number | null): string {
  return value !== null && Number.isFinite(value) && value >= 0 && value <= 100
    ? `${value.toLocaleString("pt-BR")}%`
    : "Não informado";
}
export function safeUrl(value?: string): string | undefined {
  if (!value) return;
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.href : undefined;
  } catch {
    return;
  }
}
export function date(value: string | null): string {
  if (!value) return "A confirmar";
  const parsed = new Date(value.length === 10 ? `${value}T12:00:00` : value);
  return Number.isNaN(parsed.valueOf())
    ? "A confirmar"
    : parsed.toLocaleDateString("pt-BR", {
        day: "numeric",
        month: "long",
        year: "numeric",
      });
}
