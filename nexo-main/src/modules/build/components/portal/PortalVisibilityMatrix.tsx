import { AlertTriangle } from "lucide-react";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import type { BuildPortalVisibility } from "../../api/build-portal.api";
import { hasFinancialVisible } from "../../lib/portal";

type Flag = keyof BuildPortalVisibility;

interface Item { flag: Flag; label: string; hint?: string; soon?: boolean }

const VISIBILITY_GROUPS: Array<{ title: string; items: Item[]; financial?: boolean }> = [
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
    ],
  },
  {
    title: "Outros",
    items: [
      { flag: "clientUpdates",  label: "Atualizações para o cliente" },
      { flag: "contact",        label: "Contato da obra" },
      { flag: "documents",      label: "Documentos", soon: true },
      { flag: "model3d",        label: "Modelo 3D", soon: true },
      { flag: "realityCapture", label: "Captura da realidade (scan)", soon: true },
      { flag: "risks",          label: "Riscos", soon: true },
      { flag: "team",           label: "Equipe", soon: true },
    ],
  },
];

interface Props {
  value: BuildPortalVisibility;
  onChange: (v: BuildPortalVisibility) => void;
}

/** What the client sees, grouped. "Em breve" items keep their stored value and cannot be changed yet. */
export function PortalVisibilityMatrix({ value, onChange }: Props) {
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
          {group.items.map((item) => {
            const id = `portal-vis-${item.flag}`;
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
                </div>
                <Switch id={id} checked={value[item.flag]} disabled={item.soon}
                  onCheckedChange={(on) => onChange({ ...value, [item.flag]: on })} />
              </div>
            );
          })}
        </fieldset>
      ))}
    </div>
  );
}
