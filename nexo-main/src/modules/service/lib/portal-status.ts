/**
 * Pure derivation of the Portal readiness checklist shown at the top of the Service Portal page.
 * Each item is OK / pending / attention — never invented; everything is computed from real state
 * (preset, slug, booking settings, professionals with hours, active services).
 */

export type ChecklistStatus = "ok" | "pending" | "warn";

export interface ChecklistItem {
  key:    string;
  label:  string;
  status: ChecklistStatus;
  hint?:  string;
}

export interface PortalStatusInput {
  isConfigured:           boolean;
  presetDisplayName:      string | null;
  hasSlug:                boolean;
  bookingEnabled:         boolean;
  activeProfessionals:    number;
  professionalsWithHours: number;
  activeServices:         number;
}

export function buildPortalChecklist(i: PortalStatusInput): ChecklistItem[] {
  return [
    { key: "module", label: "Módulo Service ativo", status: "ok" },
    {
      key: "preset", label: "Ramo escolhido",
      status: i.isConfigured ? "ok" : "pending",
      hint: i.isConfigured ? (i.presetDisplayName ?? undefined) : "Escolha o ramo no onboarding.",
    },
    {
      key: "slug", label: "Endereço público configurado",
      status: i.hasSlug ? "ok" : "pending",
      hint: i.hasSlug ? undefined : "Defina o endereço do portal.",
    },
    {
      key: "booking", label: "Agendamento público ativo",
      status: i.bookingEnabled ? "ok" : "pending",
      hint: i.bookingEnabled ? undefined : "Ative o agendamento nas configurações.",
    },
    {
      key: "hours", label: "Profissionais com horário",
      status: i.professionalsWithHours > 0 ? "ok" : (i.activeProfessionals > 0 ? "warn" : "pending"),
      hint: i.professionalsWithHours > 0
        ? `${i.professionalsWithHours} de ${i.activeProfessionals} com horário configurado`
        : (i.activeProfessionals > 0
            ? "Configure os horários de atendimento."
            : "Cadastre profissionais ativos."),
    },
    {
      key: "services", label: "Serviços ativos cadastrados",
      status: i.activeServices > 0 ? "ok" : "pending",
      hint: i.activeServices > 0 ? `${i.activeServices} serviço(s) ativo(s)` : "Cadastre ao menos um serviço.",
    },
  ];
}

/** Items still needing attention before the portal can take a real booking. */
export function pendingItems(items: ChecklistItem[]): ChecklistItem[] {
  return items.filter((x) => x.status !== "ok");
}

/** The minimum that must be true for the public portal to actually take a booking. */
export function isPortalPublishable(i: PortalStatusInput): boolean {
  return i.isConfigured
    && i.hasSlug
    && i.bookingEnabled
    && i.professionalsWithHours > 0
    && i.activeServices > 0;
}
