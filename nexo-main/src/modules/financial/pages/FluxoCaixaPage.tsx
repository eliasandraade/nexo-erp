import { lazy, Suspense, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlarmClock, Landmark, TrendingDown, TrendingUp, Wallet } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { ErrorState } from "@/components/shared/ErrorState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { formatCurrency } from "@/lib/formatters";
import {
  ACCOUNTS_KEY, CASH_FLOW_KEY, getCashFlow, listAccounts, setOpeningBalance,
  type CashFlowBucketDto, type CashFlowGranularity,
} from "../api/financial.api";
import { money, todayInput } from "../lib/inputs";

const CashFlowChart = lazy(() => import("../components/CashFlowChart"));

type Preset = "30d" | "month" | "90d";

function addDays(day: string, n: number) {
  const d = new Date(`${day}T12:00:00`);
  d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function range(preset: Preset): { from: string; to: string; granularity: CashFlowGranularity } {
  const today = todayInput();
  if (preset === "30d") return { from: today, to: addDays(today, 30), granularity: "day" };
  if (preset === "90d") return { from: today, to: addDays(today, 90), granularity: "week" };
  const d = new Date(`${today}T12:00:00`);
  const first = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-01`;
  const last = new Date(d.getFullYear(), d.getMonth() + 1, 0);
  return { from: first, to: `${first.slice(0, 8)}${String(last.getDate()).padStart(2, "0")}`, granularity: "day" };
}

const fmtDay = (day: string) => `${day.slice(8, 10)}/${day.slice(5, 7)}`;
const period = (b: CashFlowBucketDto) => (b.start === b.end ? fmtDay(b.start) : `${fmtDay(b.start)} a ${fmtDay(b.end)}`);

/**
 * Fluxo de caixa: o que entrou e saiu (realizado) e o que vai entrar e sair (títulos em aberto, a partir
 * de hoje), com o saldo dia a dia. Vencidos e não pagos ficam à parte — podem ou não acontecer.
 */
export default function FluxoCaixaPage() {
  const [preset, setPreset] = useState<Preset>("30d");
  const [editingOpening, setEditingOpening] = useState(false);
  const r = useMemo(() => range(preset), [preset]);

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: [...CASH_FLOW_KEY, r.from, r.to, r.granularity],
    queryFn: () => getCashFlow(r.from, r.to, r.granularity),
  });

  const totals = data?.buckets.reduce(
    (t, b) => ({ in: t.in + b.realizedIn + b.projectedIn, out: t.out + b.realizedOut + b.projectedOut }),
    { in: 0, out: 0 },
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Fluxo de caixa"
        description="O que entrou e saiu, e o que vai entrar e sair pelos vencimentos. O saldo parte do saldo inicial de caixa e banco."
        actions={<Button variant="outline" onClick={() => setEditingOpening(true)}><Landmark className="mr-1 h-4 w-4" /> Saldo inicial</Button>}
      />

      <Tabs value={preset} onValueChange={(v) => setPreset(v as Preset)}>
        <TabsList>
          <TabsTrigger value="30d">Próximos 30 dias</TabsTrigger>
          <TabsTrigger value="month">Mês atual</TabsTrigger>
          <TabsTrigger value="90d">Próximos 90 dias</TabsTrigger>
        </TabsList>
      </Tabs>

      {isLoading ? (
        <Skeleton className="h-72 w-full" />
      ) : isError || !data ? (
        <ErrorState title="Não foi possível calcular o fluxo de caixa" onRetry={() => void refetch()} />
      ) : (
        <>
          {!data.openingBalanceConfigured && (
            <p className="rounded-md border border-dashed p-3 text-sm text-muted-foreground">
              Sem saldo inicial, o saldo parte de zero no primeiro lançamento do sistema. Informe quanto havia em caixa e banco para um saldo real.
            </p>
          )}
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <MetricBlock label="Saldo no início" value={formatCurrency(data.openingBalance)} icon={Wallet} />
            <MetricBlock label="Entradas" value={formatCurrency(totals?.in ?? 0)} icon={TrendingUp} caption="Recebido + a receber no período" />
            <MetricBlock label="Saídas" value={formatCurrency(totals?.out ?? 0)} icon={TrendingDown} caption="Pago + a pagar no período" />
            <MetricBlock label="Saldo no fim" value={formatCurrency(data.closingBalance)} icon={Wallet}
              variant={data.closingBalance < 0 ? "danger" : "default"} />
          </div>
          {(data.overdueIn > 0 || data.overdueOut > 0) && (
            <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
              <AlarmClock className="h-4 w-4" />
              Vencidos e não pagos (fora do saldo): a receber {formatCurrency(data.overdueIn)} · a pagar {formatCurrency(data.overdueOut)}
            </div>
          )}

          <SectionCard title="Entradas, saídas e saldo">
            <Suspense fallback={<Skeleton className="h-[280px] w-full" />}>
              <CashFlowChart buckets={data.buckets} />
            </Suspense>
          </SectionCard>

          <SectionCard title="Por período">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Período</TableHead>
                  <TableHead className="text-right">Recebido</TableHead>
                  <TableHead className="text-right">Pago</TableHead>
                  <TableHead className="text-right">A receber</TableHead>
                  <TableHead className="text-right">A pagar</TableHead>
                  <TableHead className="text-right">Saldo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.buckets.map((b) => (
                  <TableRow key={b.start}>
                    <TableCell className="whitespace-nowrap text-sm">{period(b)}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.realizedIn ? formatCurrency(b.realizedIn) : "—"}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.realizedOut ? formatCurrency(b.realizedOut) : "—"}</TableCell>
                    <TableCell className="text-right tabular-nums text-muted-foreground">{b.projectedIn ? formatCurrency(b.projectedIn) : "—"}</TableCell>
                    <TableCell className="text-right tabular-nums text-muted-foreground">{b.projectedOut ? formatCurrency(b.projectedOut) : "—"}</TableCell>
                    <TableCell className={`text-right font-medium tabular-nums ${b.balance < 0 ? "text-destructive" : ""}`}>
                      {formatCurrency(b.balance)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </SectionCard>
        </>
      )}

      {editingOpening && <OpeningBalanceDialog onClose={() => setEditingOpening(false)} />}
    </div>
  );
}

function OpeningBalanceDialog({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const { data: accounts } = useQuery({ queryKey: ACCOUNTS_KEY, queryFn: listAccounts });
  const candidates = (accounts ?? []).filter((a) => a.isActive && (a.accountType === "Cash" || a.accountType === "Bank"));
  const [accountId, setAccountId] = useState<string>("");
  const selected = candidates.find((a) => a.id === (accountId || candidates[0]?.id));
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(todayInput());
  const value = money(amount);

  const save = useMutation({
    mutationFn: () => setOpeningBalance(selected!.id, value, date),
    onSuccess: () => {
      toast.success("Saldo inicial salvo.");
      void qc.invalidateQueries({ queryKey: ACCOUNTS_KEY });
      void qc.invalidateQueries({ queryKey: CASH_FLOW_KEY });
      onClose();
    },
    onError: (e: Error) => toast.error(e.message || "Não foi possível salvar."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !save.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Saldo inicial</DialogTitle>
          <DialogDescription>Quanto havia na conta nesta data, antes dos lançamentos do sistema.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3">
          <div className="space-y-1.5">
            <Label htmlFor="opening-account">Conta</Label>
            <Select value={selected?.id ?? ""} onValueChange={setAccountId}>
              <SelectTrigger id="opening-account" aria-label="Conta"><SelectValue placeholder="Sem conta de caixa ou banco" /></SelectTrigger>
              <SelectContent>
                {candidates.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.name}{a.openingBalanceDate ? ` — atual ${formatCurrency(a.openingBalance)}` : ""}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="opening-amount">Valor</Label>
              <Input id="opening-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0,00" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="opening-date">Em</Label>
              <Input id="opening-date" type="date" value={date} max={todayInput()} onChange={(e) => setDate(e.target.value)} />
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancelar</Button>
          <Button onClick={() => save.mutate()} disabled={!selected || Number.isNaN(value) || !date || save.isPending}>Salvar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
