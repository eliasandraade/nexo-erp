/** Presentation model. Backend v1.5 DTOs are confined to api/generated and api/adapter. */
export type Phase =
  | "waiting"
  | "active"
  | "complete"
  | "delayed"
  | "blocked"
  | "review"
  | "unknown";
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
  sections?: string[];
  sectionErrors?: { section: string; message: string }[];
  refreshAt?: string;
  title?: string;
  welcome?: string;
  curves?: {
    actual: { date: string; percent: number }[];
    planned?: { date: string; percent: number }[];
    forecast?: { date: string; percent: number }[];
  };
  schedule?: {
    name: string;
    order: number;
    start: string | null;
    end: string | null;
  }[];
  changeOrders?: {
    id: string;
    number: number;
    title: string;
    description?: string;
    status: string;
    canDecide: boolean;
    cost: number | null;
    days: number;
    items: { name: string; quantity: number; unit: string }[];
  }[];
  measurements?: {
    number: number;
    start: string;
    end: string;
    amount: number | null;
    items: { name: string; previous: number; measured: number }[];
  }[];
  handover?: {
    status: string;
    scheduled?: string;
    delivered?: string;
    accepted?: string;
    canAccept: boolean;
    keys: boolean;
    documents: { name: string; url?: string }[];
    inspection?: { status: string; items: { text: string; result: string }[] };
  };
  warrantyNotice?: string;
  downloads?: { name: string; url?: string; date: string }[];
  pagination?: { timeline?: string; photos?: number; captures?: number };
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
    canChoose?: boolean;
    statusLabel?: string;
    description?: string;
    id: string;
    version: string;
    title: string;
    due: string;
    status: "pending" | "confirmed";
    selected?: string;
    options: {
      image?: string;
      leadTime?: boolean;
      id: string;
      name: string;
      description: string;
      extra: number | null;
      days: number | null;
    }[];
  }[];
  warranties: { id: string; name: string; until: string }[];
  assistance: null | {
    canOpen?: boolean;
    categories: string[];
    tickets: {
      id: string;
      title: string;
      status: string;
      number?: number;
      description?: string;
      canAddPhotos?: boolean;
      canGiveFeedback?: boolean;
      photos?: string[];
      rating?: number;
      resolution?: string;
      scheduled?: string;
      timeline?: { status: string; at: string }[];
    }[];
  };
}
export interface TwinModel {
  geometrySource?: string;
  disclaimer?: string;
  minDate?: string;
  maxDate?: string;
  zones?: {
    id: string;
    name: string;
    kind: string;
    node?: string;
    parentId?: string;
    box?: { width: number; height: number; depth: number; elevation: number };
  }[];
  zoneStates?: Record<string, Phase>;
  unbound?: number;
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
  snapshots: TwinSnapshot[];
}
export interface TwinSnapshot {
  id: string;
  date: string;
  states: Record<string, Phase>;
  zoneStates?: Record<string, Phase>;
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
