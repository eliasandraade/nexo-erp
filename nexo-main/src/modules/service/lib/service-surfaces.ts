import {
  BookMarked,
  CalendarClock,
  ClipboardList,
  CreditCard,
  Package,
  Users,
  Boxes,
  type LucideIcon,
} from "lucide-react";
import type { ServicePresetDto, SvcSubjectKind } from "../api/service.api";

/**
 * The navigable Service surfaces. Which ones are active is driven entirely by the resolved
 * preset's capability flags (decision D2 — one set of screens, adapted per vertical). This is
 * the single source of truth consumed by both the overview page and the sidebar nav, so the
 * two never drift.
 *
 * Records are intentionally absent: they are contextual (attached to a subject / customer /
 * order) and the backend has no list-all endpoint, so they live inside detail screens, not
 * top-level nav.
 */
export type ServiceSurfaceKey =
  | "professionals"
  | "catalog"
  | "subjects"
  | "agenda"
  | "orders"
  | "packages"
  | "payments";

/** Extra context (beyond the preset) that can switch a surface on. */
export interface SurfaceContext {
  /** Public booking is on — keeps the Agenda discoverable even without the appointments capability. */
  publicBookingEnabled?: boolean;
}

export interface ServiceSurface {
  key: ServiceSurfaceKey;
  path: string;
  icon: LucideIcon;
  /** Nav/overview label, adapted from the preset where the term varies per vertical. */
  label: (preset: ServicePresetDto) => string;
  /** Short description for the overview cards. */
  description: (preset: ServicePresetDto) => string;
  /** Whether this surface is active for the resolved preset (+ optional context). */
  isEnabled: (preset: ServicePresetDto, ctx?: SurfaceContext) => boolean;
}

export const SERVICE_HOME = "/service";

/** Sensible Portuguese plurals for the subject entity, keyed by the preset's subjectKind. */
const SUBJECT_PLURAL: Record<SvcSubjectKind, string> = {
  Pet: "Pets",
  Vehicle: "Veículos",
  Student: "Alunos",
  Dependent: "Dependentes",
  Other: "Cadastros",
};

function subjectLabel(preset: ServicePresetDto): string {
  return SUBJECT_PLURAL[preset.capabilities.subjectKind ?? "Other"];
}

/** Declaration order doubles as nav order. */
export const SERVICE_SURFACES: ServiceSurface[] = [
  {
    key: "agenda",
    path: "/service/agenda",
    icon: CalendarClock,
    label: () => "Agenda",
    description: (p) => `Agende e acompanhe ${p.labels.appointment.toLowerCase()}s.`,
    // Public booking creates appointments even for verticals without the capability, so the
    // owner must be able to see the Agenda once the portal is on.
    isEnabled: (p, ctx) => p.capabilities.appointments || !!ctx?.publicBookingEnabled,
  },
  {
    key: "orders",
    path: "/service/ordens",
    icon: ClipboardList,
    label: (p) => p.labels.order,
    description: (p) => `Abra e feche ${p.labels.order.toLowerCase()}s com itens e total.`,
    isEnabled: (p) => p.capabilities.orders,
  },
  {
    key: "packages",
    path: "/service/pacotes",
    icon: Package,
    label: () => "Pacotes",
    description: () => "Pacotes vendidos e saldos por cliente.",
    isEnabled: (p) => p.capabilities.packages,
  },
  {
    key: "payments",
    path: "/service/pagamentos",
    icon: CreditCard,
    label: () => "Pagamentos",
    description: () => "Pagamentos recebidos e saldos em aberto.",
    // SvcPayment targets an order XOR a customer-package — needs at least one to attach to.
    isEnabled: (p) => p.capabilities.orders || p.capabilities.packages,
  },
  {
    key: "professionals",
    path: "/service/profissionais",
    icon: Users,
    label: () => "Profissionais",
    description: (p) => `Equipe de ${p.labels.professional.toLowerCase()}s e comissões.`,
    isEnabled: () => true,
  },
  {
    key: "catalog",
    path: "/service/catalogo",
    icon: BookMarked,
    label: () => "Catálogo",
    description: (p) => `${p.labels.catalogItem}s, duração e preço.`,
    isEnabled: () => true,
  },
  {
    key: "subjects",
    path: "/service/subjects",
    icon: Boxes,
    label: subjectLabel,
    description: (p) => `Cadastro de ${subjectLabel(p).toLowerCase()} dos clientes.`,
    isEnabled: (p) => p.capabilities.subjectKind !== null,
  },
];

/** Surfaces active for the resolved preset (+ optional context), in nav order. */
export function enabledSurfaces(preset: ServicePresetDto, ctx?: SurfaceContext): ServiceSurface[] {
  return SERVICE_SURFACES.filter((s) => s.isEnabled(preset, ctx));
}
