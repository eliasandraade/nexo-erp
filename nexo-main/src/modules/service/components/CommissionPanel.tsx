import { useEffect, useMemo, useState } from "react";
import { Lock, Wallet } from "lucide-react";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { formatCurrency, formatDate, formatDateTime } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import { ApiError } from "@/services/api-client";
import { useAuth } from "@/modules/auth/context/AuthContext";
import type { SvcCommissionPayoutDto, SvcProfessionalDto } from "../api/service.api";
import { useServicePreset } from "../context/ServicePresetContext";
import {
  useCloseCommissionPayout,
  useCommissionEntries,
  useCommissionPayouts,
  useCommissionSummary,
  useMarkCommissionPayoutPaid,
} from "../hooks/useCommissions";
import {
  commissionSourceLabel,
  currentMonthPeriod,
  entryStatus,
  PAYOUT_STATUS_LABELS,
  PAYOUT_STATUS_VARIANTS,
  periodToUtcRange,
  sumCommission,
} from "../lib/commission";

interface CommissionPanelProps {
  /** Every professional (active and inactive) — commission history outlives deactivation. */
  professionals: SvcProfessionalDto[];
}

/**
 * Comissões dentro de Profissionais: a position per professional (em aberto / a pagar / pago),
 * the entries of a period, closing the period into a payout and marking a payout paid — which is
 * when the expense reaches the Financeiro. Closing and paying are manager-only (the API enforces
 * it too); everyone with access sees the numbers.
 */
export function CommissionPanel({ professionals }: CommissionPanelProps) {
  const { labels } = useServicePreset();
  const { session } = useAuth();
  const canManage = session?.role === "diretoria" || session?.role === "gerente";
  const term = labels?.professional ?? "Profissional";

  const [selectedId, setSelectedId] = useState<string | undefined>(undefined);
  const [period, setPeriod] = useState(currentMonthPeriod);
  const [confirmClose, setConfirmClose] = useState(false);
  const [payoutToPay, setPayoutToPay] = useState<SvcCommissionPayoutDto | null>(null);

  const summary = useCommissionSummary(undefined);
  const range = periodToUtcRange(period.from, period.to);
  const entries = useCommissionEntries(
    { professionalId: selectedId, from: range?.start, to: range?.end, status: "all" },
    !!selectedId && !!range,
  );
  const payouts = useCommissionPayouts(selectedId, !!selectedId);
  const closePayout = useCloseCommissionPayout();
  const markPaid = useMarkCommissionPayoutPaid();

  const byProfessional = useMemo(
    () => new Map((summary.data ?? []).map((s) => [s.professionalId, s])),
    [summary.data],
  );
  const nameOf = (id: string) => professionals.find((p) => p.id === id)?.name ?? term;

  // Overview rows: active professionals, plus inactive ones that still hold commission.
  const rows = useMemo(
    () => professionals.filter((p) => p.isActive || byProfessional.has(p.id)),
    [professionals, byProfessional],
  );

  // Preselect the professional with the most open commission (or the first one).
  useEffect(() => {
    if (selectedId || rows.length === 0 || summary.isLoading) return;
    const top = [...rows].sort(
      (a, b) => (byProfessional.get(b.id)?.openAmount ?? 0) - (byProfessional.get(a.id)?.openAmount ?? 0),
    )[0];
    setSelectedId(top.id);
  }, [rows, byProfessional, selectedId, summary.isLoading]);

  const periodEntries = entries.data ?? [];
  const openInPeriod = periodEntries.filter((e) => !e.payoutId);
  const openInPeriodTotal = sumCommission(openInPeriod);
  const selectedSummary = selectedId ? byProfessional.get(selectedId) : undefined;

  const handleClose = () => {
    setConfirmClose(false);
    if (!selectedId || !range || openInPeriod.length === 0) return;
    closePayout.mutate(
      { professionalId: selectedId, periodStart: range.start, periodEnd: range.end },
      {
        onSuccess: (d) => toast.success(`Período fechado: ${formatCurrency(d.payout.totalAmount)} a pagar.`),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível fechar o período."),
      },
    );
  };

  const handlePay = () => {
    const payout = payoutToPay;
    setPayoutToPay(null);
    if (!payout) return;
    markPaid.mutate(
      { id: payout.id },
      {
        onSuccess: () => toast.success("Repasse pago e lançado no Financeiro."),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível registrar o pagamento."),
      },
    );
  };

  if (summary.isError) {
    return (
      <SectionCard title="Comissões">
        <ErrorState onRetry={summary.refetch} />
      </SectionCard>
    );
  }

  return (
    <SectionCard
      title="Comissões"
      description="Reconhecidas quando a comanda é quitada, o agendamento sem comanda é concluído ou um pacote é consumido."
      noPadding
    >
      {/* ── Overview per professional ─────────────────────────────────────── */}
      {summary.isLoading ? (
        <div className="space-y-2 px-5 pb-5">
          {[1, 2].map((i) => <Skeleton key={i} className="h-10 w-full" />)}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState icon={Wallet} title="Sem comissões" description={`Cadastre um ${term.toLowerCase()} para acompanhar comissões.`} />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{term}</TableHead>
              <TableHead className="text-right">Em aberto</TableHead>
              <TableHead className="text-right">A pagar</TableHead>
              <TableHead className="text-right">Pago</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((p) => {
              const s = byProfessional.get(p.id);
              return (
                <TableRow
                  key={p.id}
                  onClick={() => setSelectedId(p.id)}
                  className={cn("cursor-pointer", selectedId === p.id && "bg-muted/60")}
                  aria-selected={selectedId === p.id}
                >
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: p.color ?? "#94a3b8" }} />
                      <span className="font-medium text-foreground">{p.name}</span>
                      {!p.isActive && <StatusBadge variant="neutral" label="Inativo" />}
                    </div>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(s?.openAmount ?? 0)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(s?.pendingPayoutAmount ?? 0)}</TableCell>
                  <TableCell className="text-right tabular-nums text-muted-foreground">{formatCurrency(s?.paidPayoutAmount ?? 0)}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}

      {/* ── Selected professional ─────────────────────────────────────────── */}
      {selectedId && (
        <div className="space-y-4 border-t border-border p-5">
          <div className="flex flex-wrap items-end gap-3">
            <div className="min-w-[200px] flex-1 space-y-1.5">
              <Label>{term}</Label>
              <Select value={selectedId} onValueChange={setSelectedId}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {rows.map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="comm-from">De</Label>
              <Input id="comm-from" type="date" value={period.from} max={period.to}
                onChange={(e) => setPeriod((p) => ({ ...p, from: e.target.value }))} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="comm-to">Até</Label>
              <Input id="comm-to" type="date" value={period.to} min={period.from}
                onChange={(e) => setPeriod((p) => ({ ...p, to: e.target.value }))} />
            </div>
            {canManage ? (
              <Button
                onClick={() => setConfirmClose(true)}
                disabled={!range || openInPeriod.length === 0 || closePayout.isPending}
              >
                <Lock className="mr-1.5 h-4 w-4" />
                {closePayout.isPending ? "Fechando..." : "Fechar período"}
              </Button>
            ) : null}
          </div>
          {!range && <p className="text-[12px] text-destructive">Período inválido.</p>}
          {!canManage && (
            <p className="text-[11.5px] text-muted-foreground">Fechar período e pagar repasses é restrito a gerência e diretoria.</p>
          )}

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Stat label="Em aberto no período" value={formatCurrency(openInPeriodTotal)}
              hint={`${openInPeriod.length} lançamento(s)`} accent />
            <Stat label="Fechado, a pagar" value={formatCurrency(selectedSummary?.pendingPayoutAmount ?? 0)}
              hint={`${selectedSummary?.pendingPayoutCount ?? 0} fechamento(s)`} />
            <Stat label="Total já pago" value={formatCurrency(selectedSummary?.paidPayoutAmount ?? 0)}
              hint={`${selectedSummary?.paidPayoutCount ?? 0} repasse(s)`} />
          </div>

          <Tabs defaultValue="entries">
            <TabsList>
              <TabsTrigger value="entries">Lançamentos</TabsTrigger>
              <TabsTrigger value="payouts">Fechamentos</TabsTrigger>
            </TabsList>

            <TabsContent value="entries">
              {entries.isLoading ? (
                <Skeleton className="h-24 w-full" />
              ) : entries.isError ? (
                <ErrorState onRetry={entries.refetch} />
              ) : periodEntries.length === 0 ? (
                <p className="py-6 text-center text-[12.5px] text-muted-foreground">Nenhuma comissão no período.</p>
              ) : (
                <div className="overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Data</TableHead>
                        <TableHead>Origem</TableHead>
                        <TableHead>Descrição</TableHead>
                        <TableHead className="text-right">Base</TableHead>
                        <TableHead className="text-right">%</TableHead>
                        <TableHead className="text-right">Comissão</TableHead>
                        <TableHead>Status</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {periodEntries.map((e) => {
                        const st = entryStatus(e);
                        return (
                          <TableRow key={e.id}>
                            <TableCell className="whitespace-nowrap text-muted-foreground">{formatDateTime(e.recognizedAt)}</TableCell>
                            <TableCell>{commissionSourceLabel(e.source, labels)}</TableCell>
                            <TableCell className="max-w-[260px] truncate text-muted-foreground" title={e.description ?? ""}>
                              {e.description ?? "—"}
                            </TableCell>
                            <TableCell className="text-right tabular-nums">{formatCurrency(e.baseAmount)}</TableCell>
                            <TableCell className="text-right tabular-nums">{e.commissionPercent}%</TableCell>
                            <TableCell className="text-right font-medium tabular-nums">{formatCurrency(e.commissionAmount)}</TableCell>
                            <TableCell><StatusBadge variant={st.variant} label={st.label} /></TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </div>
              )}
            </TabsContent>

            <TabsContent value="payouts">
              {payouts.isLoading ? (
                <Skeleton className="h-24 w-full" />
              ) : payouts.isError ? (
                <ErrorState onRetry={payouts.refetch} />
              ) : (payouts.data ?? []).length === 0 ? (
                <p className="py-6 text-center text-[12.5px] text-muted-foreground">Nenhum fechamento ainda.</p>
              ) : (
                <div className="overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Período</TableHead>
                        <TableHead className="text-right">Lançamentos</TableHead>
                        <TableHead className="text-right">Total</TableHead>
                        <TableHead>Status</TableHead>
                        <TableHead>Pago em</TableHead>
                        <TableHead className="w-10" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {(payouts.data ?? []).map((p) => (
                        <TableRow key={p.id}>
                          <TableCell className="whitespace-nowrap">
                            {formatDate(p.periodStart)} – {formatDate(p.periodEnd)}
                          </TableCell>
                          <TableCell className="text-right tabular-nums">{p.entryCount}</TableCell>
                          <TableCell className="text-right font-medium tabular-nums">{formatCurrency(p.totalAmount)}</TableCell>
                          <TableCell>
                            <StatusBadge variant={PAYOUT_STATUS_VARIANTS[p.status]} label={PAYOUT_STATUS_LABELS[p.status]} dot />
                          </TableCell>
                          <TableCell className="text-muted-foreground">{p.paidAt ? formatDate(p.paidAt) : "—"}</TableCell>
                          <TableCell>
                            {p.status === "Pending" && canManage && (
                              <Button size="sm" variant="outline" disabled={markPaid.isPending}
                                onClick={() => setPayoutToPay(p)}>
                                Marcar como pago
                              </Button>
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </TabsContent>
          </Tabs>
        </div>
      )}

      <ConfirmDialog
        open={confirmClose}
        title="Fechar período de comissões"
        description={
          `${nameOf(selectedId ?? "")}: ${openInPeriod.length} lançamento(s) em aberto de ` +
          `${period.from.split("-").reverse().join("/")} a ${period.to.split("-").reverse().join("/")}, ` +
          `total ${formatCurrency(openInPeriodTotal)}. Depois de fechadas, essas comissões ficam a pagar ` +
          `e não entram em outro fechamento.`
        }
        confirmLabel="Fechar período"
        onConfirm={handleClose}
        onCancel={() => setConfirmClose(false)}
      />
      <ConfirmDialog
        open={payoutToPay !== null}
        title="Marcar repasse como pago"
        description={
          payoutToPay
            ? `Registra o pagamento de ${formatCurrency(payoutToPay.totalAmount)} a ${nameOf(payoutToPay.professionalId)} ` +
              `e lança a despesa, já quitada, em Contas a Pagar no Financeiro.`
            : ""
        }
        confirmLabel="Marcar como pago"
        variant="warning"
        onConfirm={handlePay}
        onCancel={() => setPayoutToPay(null)}
      />
    </SectionCard>
  );
}

function Stat({ label, value, hint, accent }: { label: string; value: string; hint?: string; accent?: boolean }) {
  return (
    <div className="rounded-md border border-border p-3">
      <p className="text-[11px] text-muted-foreground">{label}</p>
      <p className={cn("text-[15px] font-semibold tabular-nums", accent ? "text-primary" : "text-foreground")}>{value}</p>
      {hint && <p className="text-[11px] text-muted-foreground">{hint}</p>}
    </div>
  );
}
