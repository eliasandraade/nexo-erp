import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { AlertTriangle, CalendarDays, DollarSign, Gauge, Pencil, Plus, RefreshCw, Save, ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useAiCosts, useSetTenantAiLimits } from "../../hooks/useAiOperations";
import { usePlatformTenants } from "../../hooks/usePlatformTenants";
import type { TenantAiLimits, TenantCostSummary } from "../../types/aiOperations";
import { ApiErrorPanel, formatUsd, governedErrorText } from "./aiShared";

const LIMIT_MAX = 1_000_000;

// ── Limits form ────────────────────────────────────────────────────────────────

interface LimitsDraft {
  soft: string;
  hard: string;
}

const draftOf = (item: TenantCostSummary | undefined): LimitsDraft => ({
  soft: item?.softLimitUsd == null ? "" : String(item.softLimitUsd),
  hard: item?.hardLimitUsd == null ? "" : String(item.hardLimitUsd),
});

/** Empty = no limit (null). A string is the problem to show. Same rules as the backend. */
function parseLimit(raw: string, label: string): number | null | string {
  const text = raw.trim().replace(",", ".");
  if (text === "") return null;
  const value = Number(text);
  if (!Number.isFinite(value) || value < 0) return `${label}: informe um valor em US$ maior ou igual a zero.`;
  if (value > LIMIT_MAX) return `${label}: o máximo é US$ 1.000.000.`;
  return value;
}

/** What the dialog would send, or the problem that stops it. */
function buildLimits(d: LimitsDraft): TenantAiLimits | string {
  const soft = parseLimit(d.soft, "Limite suave");
  if (typeof soft === "string") return soft;
  const hard = parseLimit(d.hard, "Limite rígido");
  if (typeof hard === "string") return hard;
  if (soft != null && hard != null && soft > hard) return "O limite suave não pode ser maior que o limite rígido.";
  return { softLimitUsd: soft, hardLimitUsd: hard };
}

/**
 * Edits the monthly limits of one company. With `tenant` fixed it edits that row; without it, the operator picks the
 * company first (one not listed yet, or any other).
 */
function LimitsDialog({
  open,
  onClose,
  tenant,
  items,
}: Readonly<{
  open: boolean;
  onClose: () => void;
  tenant: { id: string; name: string } | null;
  items: TenantCostSummary[];
}>) {
  const save = useSetTenantAiLimits();
  const tenants = usePlatformTenants();
  const [pickedId, setPickedId] = useState("");
  const tenantId = tenant?.id ?? pickedId;
  const current = items.find(i => i.tenantId === tenantId);
  const [draft, setDraft] = useState<LimitsDraft>(() => draftOf(current));
  const [failure, setFailure] = useState<string | null>(null);

  const tenantName = tenant?.name
    ?? current?.tenantName
    ?? tenants.data?.find(t => t.id === pickedId)?.companyName
    ?? "";
  const result = buildLimits(draft);
  const problem = typeof result === "string" ? result : null;

  const close = () => { setPickedId(""); setDraft(draftOf(undefined)); setFailure(null); save.reset(); onClose(); };

  const pick = (id: string) => {
    setPickedId(id);
    setDraft(draftOf(items.find(i => i.tenantId === id)));
    setFailure(null);
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!tenantId || typeof result === "string") return;
    setFailure(null);
    save.mutate({ tenantId, limits: result }, {
      onSuccess: (updated) => {
        toast.success(`Limites de ${updated.tenantName || tenantName} salvos.`);
        close();
      },
      onError: (err) => {
        const text = governedErrorText(err);
        setFailure(text);
        toast.error(text);
      },
    });
  };

  const set = (field: keyof LimitsDraft) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDraft(d => ({ ...d, [field]: e.target.value }));

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent className="max-w-md">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Gauge className="h-4 w-4 text-muted-foreground" />
              {tenant ? `Limites de IA — ${tenant.name}` : "Definir limite para empresa"}
            </DialogTitle>
            <DialogDescription>
              Valores em US$ por mês do calendário (UTC). Deixe em branco para não ter limite.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            {!tenant && (
              <div className="space-y-1.5">
                <label htmlFor="ai-limit-tenant" className="text-xs font-medium text-foreground">Empresa</label>
                <Select value={pickedId} onValueChange={pick}>
                  <SelectTrigger id="ai-limit-tenant" aria-label="Empresa" className="h-9 text-sm">
                    <SelectValue placeholder={tenants.isLoading ? "Carregando empresas..." : "Escolha a empresa"} />
                  </SelectTrigger>
                  <SelectContent>
                    {(tenants.data ?? []).map(t => (
                      <SelectItem key={t.id} value={t.id}>{t.tradeName || t.companyName}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {tenants.isError && (
                  <p className="text-[11px] text-destructive">{governedErrorText(tenants.error, "Não foi possível carregar as empresas.")}</p>
                )}
              </div>
            )}

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label htmlFor="ai-limit-soft" className="text-xs font-medium text-foreground">Limite suave (US$ por mês)</label>
                <Input id="ai-limit-soft" type="number" min={0} max={LIMIT_MAX} step="any" inputMode="decimal"
                  value={draft.soft} onChange={set("soft")} placeholder="Sem limite" disabled={!tenantId} className="h-9 text-sm" />
                <p className="text-[11px] text-muted-foreground">Só sinaliza.</p>
              </div>
              <div className="space-y-1">
                <label htmlFor="ai-limit-hard" className="text-xs font-medium text-foreground">Limite rígido (US$ por mês)</label>
                <Input id="ai-limit-hard" type="number" min={0} max={LIMIT_MAX} step="any" inputMode="decimal"
                  value={draft.hard} onChange={set("hard")} placeholder="Sem limite" disabled={!tenantId} className="h-9 text-sm" />
                <p className="text-[11px] text-muted-foreground">Recusa chamadas pagas.</p>
              </div>
            </div>

            {problem && <p className="text-[11px] text-destructive">{problem}</p>}
            {failure && (
              <p role="alert" className="flex items-start gap-1.5 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-xs text-destructive">
                <ShieldAlert className="h-3.5 w-3.5 shrink-0 mt-0.5" />{failure}
              </p>
            )}
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" size="sm" onClick={close} disabled={save.isPending}>Cancelar</Button>
            <Button type="submit" size="sm" disabled={!tenantId || !!problem || save.isPending} className="min-w-[110px]">
              {save.isPending
                ? <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                : <><Save className="h-3.5 w-3.5 mr-1.5" />Salvar limites</>}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Row pieces ─────────────────────────────────────────────────────────────────

/** Month spend plus in-flight calls against the hard limit (the one that blocks), or the soft one when there is no hard. */
function MonthUsage({ c }: Readonly<{ c: TenantCostSummary }>) {
  const base = c.hardLimitUsd ?? c.softLimitUsd;
  if (base == null) return <span className="text-xs text-muted-foreground">Sem limite</span>;

  const used = c.monthSpentUsd + c.monthReservedUsd;
  const pct = base > 0 ? Math.min(100, (used / base) * 100) : 100;
  let color = "bg-green-500";
  if (c.hardLimitHit) color = "bg-red-500";
  else if (c.softLimitHit || pct >= 80) color = "bg-amber-500";
  const of = c.hardLimitUsd == null ? "do limite suave" : "do limite rígido";

  return (
    <div className="flex items-center gap-2">
      <div
        role="progressbar"
        aria-label={`Uso do mês de ${c.tenantName}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(pct)}
        className="h-1.5 w-20 rounded-full bg-muted overflow-hidden"
      >
        <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-muted-foreground whitespace-nowrap">{Math.round(pct)}% {of}</span>
    </div>
  );
}

function HitBadges({ c }: Readonly<{ c: TenantCostSummary }>) {
  return (
    <>
      {c.hardLimitHit && (
        <span className="inline-flex items-center gap-1 text-[11px] font-medium px-2 py-0.5 rounded-full border bg-red-500/10 text-red-700 border-red-500/30">
          <AlertTriangle className="h-3 w-3" /> Limite rígido atingido
        </span>
      )}
      {c.softLimitHit && (
        <span className="inline-flex items-center gap-1 text-[11px] font-medium px-2 py-0.5 rounded-full border bg-amber-500/10 text-amber-700 border-amber-500/30">
          <AlertTriangle className="h-3 w-3" /> Limite suave atingido
        </span>
      )}
    </>
  );
}

const limitText = (v: number | null) => (v == null ? "—" : formatUsd(v));

function rowTone(c: TenantCostSummary): string {
  if (c.hardLimitHit) return "bg-red-500/5";
  if (c.softLimitHit) return "bg-amber-500/5";
  return "";
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function AiCostsPage() {
  const { data, isLoading, isError, error } = useAiCosts();
  const items = data?.items ?? [];
  // null = closed; { tenant: null } = pick a company; { tenant } = edit that row
  const [editing, setEditing] = useState<{ tenant: { id: string; name: string } | null } | null>(null);

  const withLimit = items.filter(i => i.softLimitUsd != null || i.hardLimitUsd != null).length;
  const hardHit = items.filter(i => i.hardLimitHit).length;

  return (
    <div className="p-4 sm:p-6 space-y-5 max-w-6xl">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Custos e limites de IA</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Gasto do mês por empresa contra os limites mensais, e o consumo dos últimos 30 dias.
          </p>
        </div>
        <Button size="sm" className="h-8 text-xs gap-1.5" onClick={() => setEditing({ tenant: null })}>
          <Plus className="h-3.5 w-3.5" /> Definir limite para empresa
        </Button>
      </div>

      {/* What the backend really enforces */}
      <div className="flex items-start gap-3 rounded-lg border border-border bg-muted/30 px-4 py-3">
        <ShieldAlert className="h-4 w-4 text-muted-foreground shrink-0 mt-0.5" />
        <div className="text-xs text-foreground space-y-1">
          <p>
            <span className="font-medium">Limite rígido:</span> chamadas pagas são recusadas antes de acontecer quando o gasto
            do mês (mais as chamadas em andamento) chegaria ao limite; no modo automático a análise cai para o leitor de
            regras, sem custo. <span className="font-medium">Limite suave:</span> só sinaliza.
          </p>
          <p className="text-muted-foreground">
            O mês é o mês do calendário (UTC). Os últimos 30 dias vêm da telemetria e servem só para acompanhar o consumo.
          </p>
        </div>
      </div>

      {isError && <ApiErrorPanel error={error} title="Não foi possível carregar os custos" />}

      {data && (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            { label: "Gasto no mês (UTC)",     value: formatUsd(data.monthSpentUsd), icon: CalendarDays },
            { label: "Últimos 30 dias",        value: formatUsd(data.totalUsd),      icon: DollarSign   },
            { label: "Empresas com limite",    value: String(withLimit),             icon: Gauge        },
            { label: "Limite rígido atingido", value: String(hardHit),               icon: AlertTriangle },
          ].map(({ label, value, icon: Icon }) => (
            <div key={label} className="bg-card border border-border rounded-lg p-4">
              <div className="flex items-center justify-between mb-2">
                <p className="text-xs text-muted-foreground">{label}</p>
                <Icon className="h-4 w-4 text-primary" />
              </div>
              <p className="text-xl font-semibold text-foreground tabular-nums">{value}</p>
            </div>
          ))}
        </div>
      )}

      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border bg-muted/30">
          <h2 className="text-sm font-medium text-foreground">Por empresa</h2>
        </div>

        {isLoading && <div className="p-4 sm:p-8 text-center text-sm text-muted-foreground">Carregando...</div>}

        {data && items.length === 0 && (
          <div className="p-4 sm:p-8 text-center text-sm text-muted-foreground">
            Nenhuma empresa com uso de IA nem limite definido.
          </div>
        )}

        {items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/20">
                  {["Empresa", "Gasto no mês", "Limite suave", "Limite rígido", "Uso do mês", "Últimos 30 dias", ""].map(h => (
                    <th key={h} className="text-left px-4 py-2 text-xs text-muted-foreground font-medium whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {items.map(c => (
                  <tr
                    key={c.tenantId}
                    aria-label={`Empresa ${c.tenantName}`}
                    className={rowTone(c)}
                  >
                    <td className="px-4 py-3">
                      <div className="flex flex-col gap-1">
                        <span className="text-sm text-foreground font-medium">{c.tenantName}</span>
                        <div className="flex flex-wrap gap-1"><HitBadges c={c} /></div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-xs tabular-nums whitespace-nowrap">
                      <span className="text-foreground font-medium">{formatUsd(c.monthSpentUsd)}</span>
                      {c.monthReservedUsd > 0 && (
                        <span className="block text-muted-foreground">+ {formatUsd(c.monthReservedUsd)} em andamento</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground whitespace-nowrap">{limitText(c.softLimitUsd)}</td>
                    <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground whitespace-nowrap">{limitText(c.hardLimitUsd)}</td>
                    <td className="px-4 py-3"><MonthUsage c={c} /></td>
                    <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground whitespace-nowrap">
                      {formatUsd(c.costUsd)}
                      <span className="block">{c.llmCalls.toLocaleString("pt-BR")} LLM / {c.totalCalls.toLocaleString("pt-BR")} chamadas</span>
                    </td>
                    <td className="px-4 py-3">
                      <Button
                        size="sm" variant="outline" className="h-7 text-xs gap-1.5"
                        aria-label={`Editar limites de ${c.tenantName}`}
                        onClick={() => setEditing({ tenant: { id: c.tenantId, name: c.tenantName } })}
                      >
                        <Pencil className="h-3 w-3" /> Limites
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editing && (
        <LimitsDialog
          key={editing.tenant?.id ?? "pick"}
          open
          onClose={() => setEditing(null)}
          tenant={editing.tenant}
          items={items}
        />
      )}
    </div>
  );
}
