import { AlertTriangle } from "lucide-react";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import type { BuildPortalAccess, BuildPortalVisibility } from "../../api/build-portal.api";
import { hasFinancialVisible } from "../../lib/portal";

type Flag = keyof BuildPortalVisibility;

/**
 * `requiresPassword`: personal/residential data — the API never serves it on a public portal (contract v1.5), so with
 * access "Público" the toggle is shown off and disabled.
 */
interface Item { flag: Flag; label: string; hint?: string; soon?: boolean; requiresPassword?: boolean }

const REQUIRES_PASSWORD_HINT = "exige portal com senha";

const VISIBILITY_GROUPS: Array<{ title: string; items: Item[]; financial?: boolean; note?: string }> = [
  {
    title: "Progresso",
    items: [
      { flag: "overallProgress",  label: "Progresso geral da obra" },
      { flag: "stageProgress",    label: "Progresso por etapa" },
      { flag: "forecastDelivery", label: "Previsão de entrega", hint: "Mostra a data prevista e a curva de previsão." },
    ],
  },
  {
    title: "Cronograma",
    items: [{ flag: "schedule", label: "Datas previstas das etapas" }],
  },
  {
    title: "Diário & mídia",
    items: [
      { flag: "dailyLogs", label: "Registros do diário", hint: "Só os registros marcados como visíveis, e só o resumo para o cliente." },
      { flag: "photos",    label: "Fotos", hint: "Só as fotos marcadas como visíveis, de registros visíveis." },
      { flag: "weather",   label: "Clima" },
      { flag: "videos",    label: "Vídeos", soon: true },
    ],
  },
  {
    title: "Financeiro",
    financial: true,
    items: [
      { flag: "financialSummary",      label: "Resumo financeiro", hint: "Orçamento aprovado × total realizado." },
      { flag: "financialCategories",   label: "Gastos por categoria", soon: true },
      { flag: "financialTransactions", label: "Lançamentos", soon: true },
      { flag: "receipts",              label: "Comprovantes", soon: true },
      { flag: "measurements",          label: "Medições aprovadas", hint: "Período e % medido por etapa. Os valores só aparecem com o resumo financeiro ligado." },
    ],
  },
  {
    title: "Comercial",
    items: [
      { flag: "changeOrders", label: "Aditivos", requiresPassword: true,
        hint: "Aditivos enviados ao cliente e decididos; o cliente aprova ou recusa. Valores só com o resumo financeiro ligado." },
      { flag: "selections",   label: "Escolhas do cliente", requiresPassword: true, hint: "Escolhas marcadas como visíveis; o cliente escolhe pelo portal." },
    ],
  },
  {
    title: "Pós-obra (Meu Imóvel)",
    items: [
      { flag: "postConstruction", label: "Entrega, garantias e assistência técnica", requiresPassword: true,
        hint: "Depois da entrega, o cliente vê o imóvel, as garantias e os chamados; abre chamados, aceita a entrega e avalia." },
    ],
  },
  {
    title: "3D e captura",
    note: "O cliente vê só o arquivo de visualização (GLB) e as cores do andamento — nunca o projeto original (IFC, RVT, DWG, PDF).",
    items: [
      { flag: "model3d",        label: "Modelo 3D / gêmeo digital", hint: "Modelo, planta mapeada ou representação simplificada, com o estado por data." },
      { flag: "realityCapture", label: "Captura da realidade", requiresPassword: true,
        hint: "Só as capturas marcadas como visíveis (fotos, 360°, vídeos, drone)." },
    ],
  },
  {
    title: "Outros",
    items: [
      { flag: "clientUpdates",  label: "Atualizações para o cliente" },
      { flag: "contact",        label: "Contato da obra" },
      { flag: "documents",      label: "Documentos", hint: "Só os marcados como visíveis ao cliente na aba Documentos.", requiresPassword: true },
      { flag: "risks",          label: "Riscos", soon: true },
      { flag: "team",           label: "Equipe", soon: true },
    ],
  },
];

interface Props {
  value: BuildPortalVisibility;
  onChange: (v: BuildPortalVisibility) => void;
  /** The portal's access mode: on "Public" the password-only sections are shown off and cannot be turned on. */
  access?: BuildPortalAccess;
}

/**
 * What the client sees, grouped. "Em breve" items keep their stored value and cannot be changed yet. Password-only items
 * on a public portal keep their stored value too (it applies again once the portal asks for a password).
 */
export function PortalVisibilityMatrix({ value, onChange, access = "Public" }: Props) {
  const financialOn = hasFinancialVisible(value);
  return (
    <div className="grid gap-3 md:grid-cols-2">
      {VISIBILITY_GROUPS.map((group) => (
        <fieldset key={group.title} className="space-y-2 rounded-md border border-border p-3">
          <legend className="px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{group.title}</legend>
          {group.financial && financialOn && (
            <div role="alert" className="flex items-start gap-1.5 rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              Atenção: o cliente passará a ver valores financeiros da obra. Confira se os lançamentos estão corretos antes de publicar.
            </div>
          )}
          {group.note && <p className="text-[11px] text-muted-foreground" data-testid="portal-3d-note">{group.note}</p>}
          {group.items.map((item) => {
            const id = `portal-vis-${item.flag}`;
            const needsPassword = !!item.requiresPassword && access === "Public";
            return (
              <div key={item.flag} className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <Label htmlFor={id} className={item.soon ? "text-sm text-muted-foreground" : "text-sm"}>
                    {item.label}
                    {item.soon && (
                      <span className="ml-1.5 rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium uppercase text-muted-foreground">em breve</span>
                    )}
                  </Label>
                  {item.hint && <p className="text-[11px] text-muted-foreground">{item.hint}</p>}
                  {needsPassword && (
                    <p className="text-[11px] font-medium text-amber-700 dark:text-amber-400" data-testid={`portal-vis-${item.flag}-password`}>
                      {REQUIRES_PASSWORD_HINT}
                    </p>
                  )}
                </div>
                <Switch id={id} checked={needsPassword ? false : value[item.flag]} disabled={item.soon || needsPassword}
                  onCheckedChange={(on) => onChange({ ...value, [item.flag]: on })} />
              </div>
            );
          })}
        </fieldset>
      ))}
    </div>
  );
}
