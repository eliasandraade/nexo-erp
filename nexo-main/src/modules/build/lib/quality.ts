import type {
  BuildAssistanceStatus, BuildChannel, BuildHandoverStatus, BuildInspectionItemDto, BuildInspectionItemResult,
  BuildInspectionStatus, BuildNcSeverity, BuildNcStatus, BuildNonConformityDto, BuildWarrantyTermDto,
  SaveBuildNonConformityRequest,
} from "../api/build-quality.api";
import type { TONE_CLASS } from "./commercial";

type Tone = keyof typeof TONE_CLASS;

/** Labels of the Wave 3 enums (the API sends the English names). */
export const INSPECTION_STATUS: Record<BuildInspectionStatus, { label: string; tone: Tone }> = {
  Planned:           { label: "Planejada",        tone: "gray" },
  InProgress:        { label: "Em andamento",     tone: "blue" },
  Passed:            { label: "Aprovada",         tone: "green" },
  PassedWithRemarks: { label: "Aprovada com ressalvas", tone: "amber" },
  Failed:            { label: "Reprovada",        tone: "red" },
  Cancelled:         { label: "Cancelada",        tone: "gray" },
};

export const ITEM_RESULTS: Array<{ value: Exclude<BuildInspectionItemResult, "Pending">; label: string }> = [
  { value: "Ok",            label: "Ok" },
  { value: "NotOk",         label: "Não ok" },
  { value: "NotApplicable", label: "N/A" },
];

export const NC_SEVERITIES: Array<{ value: BuildNcSeverity; label: string; tone: Tone }> = [
  { value: "Low",      label: "Baixa",   tone: "gray" },
  { value: "Medium",   label: "Média",   tone: "blue" },
  { value: "High",     label: "Alta",    tone: "amber" },
  { value: "Critical", label: "Crítica", tone: "red" },
];

export const severityInfo = (s: BuildNcSeverity | null | undefined) =>
  NC_SEVERITIES.find((x) => x.value === s) ?? { value: s ?? "Medium", label: s ?? "—", tone: "gray" as Tone };

export const NC_STATUS: Record<BuildNcStatus, { label: string; tone: Tone }> = {
  Open:        { label: "Aberta",          tone: "red" },
  InTreatment: { label: "Em tratamento",   tone: "amber" },
  Resolved:    { label: "Resolvida",       tone: "blue" },
  Verified:    { label: "Verificada",      tone: "green" },
  Cancelled:   { label: "Cancelada",       tone: "gray" },
};

export const HANDOVER_STATUS: Record<BuildHandoverStatus, { label: string; tone: Tone }> = {
  Scheduled:      { label: "Agendada",           tone: "blue" },
  InspectionDone: { label: "Vistoria concluída", tone: "purple" },
  Delivered:      { label: "Entregue",           tone: "green" },
};

export const ASSISTANCE_STATUS: Record<BuildAssistanceStatus, { label: string; tone: Tone }> = {
  Open:       { label: "Aberto",        tone: "red" },
  Triaged:    { label: "Triado",        tone: "amber" },
  Scheduled:  { label: "Agendado",      tone: "blue" },
  InProgress: { label: "Em atendimento", tone: "purple" },
  Resolved:   { label: "Resolvido",     tone: "green" },
  Closed:     { label: "Encerrado",     tone: "gray" },
  Rejected:   { label: "Recusado",      tone: "gray" },
};

export const channelLabel = (c: BuildChannel | null | undefined) =>
  c === "Portal" ? "Portal" : "Interno";

/** The warranty system the backend accepts for any request without a matching term. */
export const OTHER_CATEGORY = "Outros";

export const WARRANTY_DISCLAIMER =
  "Prazos de referência, não é orientação jurídica: ajuste ao contrato e à legislação aplicável.";

/** "Coberta pela garantia" / "Fora da garantia" / "Sem garantia para a categoria". */
export function coverageLabel(covered: boolean | null | undefined): { label: string; tone: Tone } {
  if (covered === true) return { label: "Coberto pela garantia", tone: "green" };
  if (covered === false) return { label: "Fora da garantia", tone: "red" };
  return { label: "Cobertura a decidir", tone: "gray" };
}

/** Badge of a warranty term: not started (until the delivery), active or expired. */
export function warrantyState(t: Pick<BuildWarrantyTermDto, "startsAt" | "isActive">): { label: string; tone: Tone } {
  if (!t.startsAt) return { label: "Começa na entrega", tone: "gray" };
  return t.isActive ? { label: "Ativa", tone: "green" } : { label: "Expirada", tone: "red" };
}

/** Problems that block completing an inspection, mirroring the server (422) — empty when it can be completed. */
export interface InspectionProblems {
  empty:        boolean;
  pending:      BuildInspectionItemDto[];
  missingPhoto: BuildInspectionItemDto[];
}

export function inspectionProblems(items: BuildInspectionItemDto[]): InspectionProblems {
  return {
    empty:        items.length === 0,
    pending:      items.filter((i) => i.required && i.result === "Pending"),
    missingPhoto: items.filter((i) => i.requiresPhoto && (i.result === "Ok" || i.result === "NotOk") && i.photoKeys.length === 0),
  };
}

export const canComplete = (p: InspectionProblems) => !p.empty && p.pending.length === 0 && p.missingPhoto.length === 0;

/** The outcome the server will compute, with the reason in plain Portuguese. */
export function previewOutcome(items: BuildInspectionItemDto[]): { status: BuildInspectionStatus; reason: string } {
  const requiredNotOk = items.filter((i) => i.required && i.result === "NotOk").length;
  const notOk = items.filter((i) => i.result === "NotOk").length;
  if (requiredNotOk > 0)
    return { status: "Failed", reason: `${requiredNotOk} item(ns) obrigatório(s) marcado(s) como “Não ok”.` };
  if (notOk > 0)
    return { status: "PassedWithRemarks", reason: `${notOk} item(ns) opcional(is) marcado(s) como “Não ok”.` };
  return { status: "Passed", reason: "Todos os itens julgados estão Ok ou não se aplicam." };
}

/** Severity the NC opened by a NotOk item gets when none is chosen: High if required, Medium otherwise. */
export const defaultItemSeverity = (required: boolean): BuildNcSeverity => (required ? "High" : "Medium");

/** yyyy-MM-ddTHH:mm (datetime-local) → ISO instant; empty → null. */
export function localDateTimeToIso(v: string): string | null {
  if (!v) return null;
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** The whole PUT body of an NC from its DTO (PUT replaces), with optional overrides. */
export function ncRequestFrom(nc: BuildNonConformityDto, over: Partial<SaveBuildNonConformityRequest> = {}): SaveBuildNonConformityRequest {
  return {
    title: nc.title, description: nc.description, severity: nc.severity, stageId: nc.stageId,
    responsibleName: nc.responsibleName, dueDate: nc.dueDate, costImpact: nc.costImpact,
    photoKeys: Array.isArray(nc.photoKeys) ? nc.photoKeys : [], rootCause: nc.rootCause,
    correctiveAction: nc.correctiveAction, ...over,
  };
}

/** A checklist line being edited (template or ad-hoc inspection items). */
export interface EditableItem { key: string; text: string; required: boolean; requiresPhoto: boolean }

let editableSeq = 0;
export const newEditableItem = (over: Partial<EditableItem> = {}): EditableItem =>
  ({ key: `it-${++editableSeq}`, text: "", required: true, requiresPhoto: false, ...over });

/** Items with text, trimmed, as the API expects them. */
export const toItemRequests = (items: EditableItem[]) =>
  items.filter((i) => i.text.trim()).map((i) => ({ text: i.text.trim(), required: i.required, requiresPhoto: i.requiresPhoto }));
