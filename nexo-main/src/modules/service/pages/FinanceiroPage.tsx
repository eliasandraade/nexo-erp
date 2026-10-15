import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { formatCurrency } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import { fetchServiceFinance } from "../api/service.api";
import { useServicePreset } from "../context/ServicePresetContext";
import { PAYMENT_METHOD_LABELS } from "../lib/payment";
import { pluralPt } from "../lib/plural";

type Preset = "today" | "7d" | "month" | "lastMonth" | "custom";

function ymd(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function rangeOf(preset: Exclude<Preset, "custom">): [string, string] {
  const now = new Date();
  if (preset === "today") return [ymd(now), ymd(now)];
  if (preset === "7d") { const s = new Date(now); s.setDate(now.getDate() - 6); return [ymd(s), ymd(now)]; }
  if (preset === "month") return [ymd(new Date(now.getFullYear(), now.getMonth(), 1)), ymd(now)];
  return [ymd(new Date(now.getFullYear(), now.getMonth() - 1, 1)), ymd(new Date(now.getFullYear(), now.getMonth(), 0))];
}
function dayLabel(day: string) {
  return new Date(`${day}T12:00:00`).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
}

/** Financeiro do Service: regime de caixa, só leitura. */
export default function FinanceiroPage() {
  const { labels } = useServicePreset();
  const proTerm = labels?.professional ?? "Profissional";
  const apptTerm = labels?.appointment ?? "Agendamento";
  const orderTerm = labels?.order ?? "Comanda";
  const [preset, setPreset] = useState<Preset>("month");
  const [custom, setCustom] = useState<[string, string]>(() => rangeOf("month"));
  const [from, to] = useMemo(() => (preset === "custom" ? custom : rangeOf(preset)), [preset, custom]);
  // same limits as the server (a half-typed year must not fire requests)
  const days = from && to ? (Date.parse(`${to}T12:00:00`) - Date.parse(`${from}T12:00:00`)) / 86_400_000 + 1 : 0;
  const rangeError = !from || !to || from.slice(0, 4) < "2000" || to.slice(0, 4) > "2100"
    ? "Escolha datas válidas."
    : from > to ? "O fim do período precisa ser depois do início."
    : days > 366 ? "Escolha um período de até 366 dias." : null;
  const valid = rangeError === null;

  const q = useQuery({
    queryKey: ["service", "finance", from, to],
    queryFn: () => fetchServiceFinance(from, to),
    enabled: valid,
  });
  const s = q.data;
  const maxDay = Math.max(1, ...(s?.byDay ?? []).map((d) => d.amount));
  const finished = s ? s.appointments.completed + s.appointments.noShow : 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Serviços"
        title="Financeiro"
        description="O que entrou no período (pagamentos válidos, pela data em que foram recebidos), por profissional, comissões e pacotes."
      />

      <div className="flex flex-wrap items-center gap-3">
        <Tabs value={preset} onValueChange={(v) => setPreset(v as Preset)} className="max-w-full overflow-x-auto">
          <TabsList>
            <TabsTrigger value="today">Hoje</TabsTrigger>
            <TabsTrigger value="7d">7 dias</TabsTrigger>
            <TabsTrigger value="month">Este mês</TabsTrigger>
            <TabsTrigger value="lastMonth">Mês passado</TabsTrigger>
            <TabsTrigger value="custom">Período</TabsTrigger>
          </TabsList>
        </Tabs>
        {preset === "custom" && (
          <div className="flex items-center gap-2">
            <Input type="date" aria-label="De" className="h-9 w-40" value={custom[0]} onChange={(e) => setCustom([e.target.value, custom[1]])} />
            <span className="text-muted-foreground">até</span>
            <Input type="date" aria-label="Até" className="h-9 w-40" value={custom[1]} onChange={(e) => setCustom([custom[0], e.target.value])} />
          </div>
        )}
      </div>
      {rangeError && <p role="alert" className="text-[13px] text-destructive">{rangeError}</p>}

      {q.isLoading && valid && <Skeleton className="h-64 w-full" />}
      {q.isError && (q.error instanceof ApiError && q.error.status === 422
        ? <p role="alert" className="text-[13px] text-destructive">{q.error.message}</p>
        : <ErrorState onRetry={() => void q.refetch()} />)}

      {s && (
        <>
          <div className="metric-strip">
            <Metric label="Recebido" value={formatCurrency(s.received)}
              hint={`${s.paymentsCount} ${s.paymentsCount === 1 ? "pagamento" : "pagamentos"}`} />
            <Metric label="Ticket médio" value={formatCurrency(s.averageTicket)} hint={`por ${orderTerm.toLowerCase()} ou pacote pago`} />
            <Metric label="Comissões" value={formatCurrency(s.commissionTotal)} hint="reconhecidas no período" />
            <Metric label="Pacotes vendidos" value={String(s.packagesSold)} hint={formatCurrency(s.packagesSoldValue)} />
          </div>

          <div className="grid gap-6 lg:grid-cols-2">
            <SectionCard title="Por meio de pagamento">
              {s.byMethod.length === 0 ? <Empty text="Nada recebido no período." /> : (
                <dl className="space-y-2 text-[13px]">
                  {s.byMethod.map((m) => (
                    <div key={m.method} className="flex justify-between gap-3">
                      <dt className="text-muted-foreground">{PAYMENT_METHOD_LABELS[m.method]} <span className="text-[11px]">({m.count})</span></dt>
                      <dd className="font-medium text-foreground">{formatCurrency(m.amount)}</dd>
                    </div>
                  ))}
                  <div className="flex justify-between gap-3 border-t border-border pt-2 text-[12px] text-muted-foreground">
                    <dt>{pluralPt(orderTerm)} · Pacotes</dt>
                    <dd>{formatCurrency(s.receivedFromOrders)} · {formatCurrency(s.receivedFromPackages)}</dd>
                  </div>
                  {s.voidedCount > 0 && (
                    <div className="flex justify-between gap-3 text-[12px] text-muted-foreground">
                      <dt title="Já fora do recebido do dia em que entraram">Estornados no período ({s.voidedCount})</dt>
                      <dd>{formatCurrency(s.voided)}</dd>
                    </div>
                  )}
                </dl>
              )}
            </SectionCard>

            <SectionCard title="Agenda no período">
              <dl className="space-y-2 text-[13px]">
                <Row term={`${pluralPt(apptTerm)} no período`} value={String(s.appointments.total)} />
                <Row term="Concluídos" value={String(s.appointments.completed)} />
                <Row term="Com cancelamento" value={String(s.appointments.cancelled)} />
                <Row term="Com falta" value={finished > 0
                  ? `${s.appointments.noShow} (${Math.round((s.appointments.noShow / finished) * 100)}% dos que já passaram)`
                  : String(s.appointments.noShow)} />
                <Row term="Ainda em aberto" value={String(s.appointments.open)} />
              </dl>
            </SectionCard>
          </div>

          <SectionCard title={`Por ${proTerm.toLowerCase()}`} noPadding>
            {s.byProfessional.length === 0 ? <div className="p-5"><Empty text="Nenhum movimento no período." /></div> : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[520px] text-[13px]">
                  <thead>
                    <tr className="border-b border-border text-[11px] uppercase tracking-wide text-muted-foreground">
                      <th className="px-5 py-2 text-left font-medium">{proTerm}</th>
                      <th className="px-3 py-2 text-right font-medium" title="Pagamentos rateados pelos itens de cada um (itens pagos por pacote ficam de fora)">Recebido em {pluralPt(orderTerm).toLowerCase()}</th>
                      <th className="px-3 py-2 text-right font-medium" title="Valor dos serviços feitos por pacote">Serviços por pacote</th>
                      <th className="px-5 py-2 text-right font-medium">Comissão</th>
                    </tr>
                  </thead>
                  <tbody>
                    {s.byProfessional.map((p) => (
                      <tr key={p.professionalId ?? "none"} className="border-b border-border/60">
                        <td className="px-5 py-2.5 text-foreground">{p.name}</td>
                        <td className="px-3 py-2.5 text-right">{formatCurrency(p.receivedFromOrders)}</td>
                        <td className="px-3 py-2.5 text-right">{formatCurrency(p.packageServices)}</td>
                        <td className="px-5 py-2.5 text-right font-medium text-foreground">{formatCurrency(p.commission)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </SectionCard>

          <SectionCard title="Recebido por dia">
            {s.byDay.length === 0 ? <Empty text="Nada recebido no período." /> : (
              <ul className="space-y-1.5" aria-label="Recebido por dia">
                {s.byDay.map((d) => (
                  <li key={d.day} className="flex items-center gap-3 text-[12px]">
                    <span className="w-12 shrink-0 text-muted-foreground">{dayLabel(d.day)}</span>
                    <span className="h-2 rounded-full bg-primary/70" style={{ width: `${Math.max(2, (d.amount / maxDay) * 100)}%`, maxWidth: "calc(100% - 9rem)" }} />
                    <span className="ml-auto shrink-0 font-medium text-foreground">{formatCurrency(d.amount)}</span>
                  </li>
                ))}
              </ul>
            )}
          </SectionCard>
        </>
      )}
    </div>
  );
}

function Metric({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="metric-cell">
      <p className="text-[11.5px] font-medium text-muted-foreground">{label}</p>
      <p className="mt-1.5 text-2xl font-semibold tabular-nums leading-tight text-foreground">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-muted-foreground">{hint}</p>}
    </div>
  );
}

function Row({ term, value }: { term: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted-foreground">{term}</dt>
      <dd className="font-medium text-foreground">{value}</dd>
    </div>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="py-2 text-center text-[12.5px] text-muted-foreground">{text}</p>;
}
