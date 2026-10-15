import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlarmClock, CalendarClock, HandCoins, Plus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
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
import { formatCurrency, formatDate } from "@/lib/formatters";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import {
  createInstallments, getReceivables, LEDGER_KEY, RECEIVABLES_KEY, settleTransaction,
  type TitleDto, type TitleStatusFilter,
} from "../api/financial.api";

/** "12,50" / "12.50" → 12.5 in cents precision; empty → 0; invalid → NaN. */
function money(value: string): number {
  if (!value.trim()) return 0;
  // "1.234,56" (dots are thousands when there is a comma) or "12.50"
  // pt-BR: "1.000" is one thousand (dot groups of 3 digits), "12.50" is twelve and a half
  const thousandsOnly = /^\d{1,3}(\.\d{3})+$/.test(value.trim());
  const normalized = value.includes(",") || thousandsOnly ? value.replace(/\./g, "").replace(",", ".") : value;
  const n = Number(normalized.trim());
  return Number.isFinite(n) ? Math.round(n * 100) / 100 : NaN;
}

function todayInput() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** A local calendar day at noon, so the date never shifts a day across time zones. */
const dayToIso = (day: string) => new Date(`${day}T12:00:00`).toISOString();

const STATUS_LABEL: Record<TitleDto["status"], string> = {
  Pending: "A vencer",
  Overdue: "Vencido",
  Paid: "Recebido",
  Cancelled: "Cancelado",
  Abated: "Abatido (desconto)",
};

/**
 * Contas a receber: o que os clientes devem, por vencimento. Receber dá baixa total ou parcial —
 * o que sobra continua em aberto —, com juros, multa e desconto lançados à parte no financeiro.
 */
export default function ContasReceberPage() {
  const qc = useQueryClient();
  const [status, setStatus] = useState<TitleStatusFilter>("open");
  const [settling, setSettling] = useState<TitleDto | null>(null);
  const [creating, setCreating] = useState(false);

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: [...RECEIVABLES_KEY, status],
    queryFn: () => getReceivables(status),
  });

  const refresh = () => {
    void qc.invalidateQueries({ queryKey: RECEIVABLES_KEY });
    void qc.invalidateQueries({ queryKey: LEDGER_KEY });
  };

  return (
    <div className="space-y-6">
      <PageHeader
        title="Contas a receber"
        description="O que os clientes devem, por vencimento: vendas a prazo, parcelas e outros recebimentos."
        actions={<Button onClick={() => setCreating(true)}><Plus className="mr-1 h-4 w-4" /> Novo recebimento</Button>}
      />

      {data && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <MetricBlock label="A vencer" value={formatCurrency(data.aging.notDue)} icon={CalendarClock} />
          <MetricBlock label="Vencido até 30 dias" value={formatCurrency(data.aging.overdue1To30)} icon={AlarmClock}
            variant={data.aging.overdue1To30 > 0 ? "warning" : "default"} />
          <MetricBlock label="Vencido 31 a 60 dias" value={formatCurrency(data.aging.overdue31To60)} icon={AlarmClock}
            variant={data.aging.overdue31To60 > 0 ? "danger" : "default"} />
          <MetricBlock label="Vencido há mais de 60 dias" value={formatCurrency(data.aging.overdueOver60)} icon={AlarmClock}
            variant={data.aging.overdueOver60 > 0 ? "danger" : "default"}
            caption={`Total em aberto: ${formatCurrency(data.aging.totalOpen)}`} />
        </div>
      )}

      <Tabs value={status} onValueChange={(v) => setStatus(v as TitleStatusFilter)}>
        <TabsList>
          <TabsTrigger value="open">Em aberto</TabsTrigger>
          <TabsTrigger value="overdue">Vencidos</TabsTrigger>
          <TabsTrigger value="settled">Recebidos</TabsTrigger>
          <TabsTrigger value="all">Todos</TabsTrigger>
        </TabsList>
      </Tabs>

      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError || !data ? (
        <ErrorState title="Não foi possível carregar as contas a receber" onRetry={() => void refetch()} />
      ) : data.items.length === 0 ? (
        <SectionCard>
          <EmptyState icon={HandCoins} title="Nada por aqui" description="Vendas a prazo e recebimentos lançados aparecem nesta lista." />
        </SectionCard>
      ) : (
        <SectionCard>
          {data.truncated && (
            <p className="mb-3 text-sm text-muted-foreground">Mostrando os primeiros {data.items.length} títulos.</p>
          )}
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Vencimento</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Descrição</TableHead>
                <TableHead>Situação</TableHead>
                <TableHead className="text-right">Valor</TableHead>
                <TableHead className="w-28" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((t) => (
                <TableRow key={t.id}>
                  <TableCell className="whitespace-nowrap text-sm">{formatDate(t.dueDate)}</TableCell>
                  <TableCell className="text-sm">{t.customerName ?? "—"}</TableCell>
                  <TableCell className="font-medium">{t.description}</TableCell>
                  <TableCell>
                    <Badge variant={t.status === "Overdue" ? "destructive" : t.status === "Paid" ? "secondary" : "outline"}>
                      {STATUS_LABEL[t.status] ?? t.status}
                    </Badge>
                    {t.daysOverdue > 0 && <span className="ml-2 text-xs text-muted-foreground">há {t.daysOverdue} dias</span>}
                  </TableCell>
                  <TableCell className="text-right font-medium tabular-nums">{formatCurrency(t.amount)}</TableCell>
                  <TableCell className="text-right">
                    {(t.status === "Pending" || t.status === "Overdue") && (
                      <Button size="sm" variant="outline" onClick={() => setSettling(t)}>Receber</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      )}

      {settling && <SettleDialog title={settling} onClose={() => setSettling(null)} onDone={refresh} />}
      {creating && <NewReceivableDialog onClose={() => setCreating(false)} onDone={refresh} />}
    </div>
  );
}

function SettleDialog({ title, onClose, onDone }: { title: TitleDto; onClose: () => void; onDone: () => void }) {
  const [amount, setAmount] = useState(title.amount.toFixed(2).replace(".", ","));
  const [interest, setInterest] = useState("");
  const [fine, setFine] = useState("");
  const [discount, setDiscount] = useState("");
  const [day, setDay] = useState(todayInput());

  const values = { amount: money(amount), interest: money(interest), fine: money(fine), discount: money(discount) };
  const invalid = Object.values(values).some((v) => Number.isNaN(v) || v < 0);
  const covered = Math.round((values.amount + values.discount) * 100) / 100;
  const left = Math.round((title.amount - covered) * 100) / 100;

  const settle = useMutation({
    mutationFn: () => settleTransaction(title.id, { ...values, paidAt: dayToIso(day) }),
    onSuccess: () => { toast.success(left > 0 ? "Recebimento parcial registrado." : "Título recebido."); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível registrar o recebimento."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !settle.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Receber</DialogTitle>
          <DialogDescription>{title.description} — saldo {formatCurrency(title.amount)}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="settle-amount">Valor recebido</Label>
            <Input id="settle-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="settle-day">Data</Label>
            <Input id="settle-day" type="date" value={day} max={todayInput()} onChange={(e) => setDay(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="settle-interest">Juros</Label>
            <Input id="settle-interest" inputMode="decimal" value={interest} onChange={(e) => setInterest(e.target.value)} placeholder="0,00" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="settle-fine">Multa</Label>
            <Input id="settle-fine" inputMode="decimal" value={fine} onChange={(e) => setFine(e.target.value)} placeholder="0,00" />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="settle-discount">Desconto concedido</Label>
            <Input id="settle-discount" inputMode="decimal" value={discount} onChange={(e) => setDiscount(e.target.value)} placeholder="0,00" />
          </div>
        </div>
        <p className="text-sm text-muted-foreground">
          {left > 0
            ? `Fica em aberto: ${formatCurrency(left)} (mesmo vencimento).`
            : left === 0 ? "O título será quitado." : "O valor passa do saldo do título."}
        </p>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={settle.isPending}>Cancelar</Button>
          <Button onClick={() => settle.mutate()} disabled={invalid || covered <= 0 || left < 0 || !day || settle.isPending}>
            Confirmar recebimento
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function NewReceivableDialog({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const { data: customers } = useCustomers();
  const [description, setDescription] = useState("");
  const [total, setTotal] = useState("");
  const [firstDue, setFirstDue] = useState(todayInput());
  const [installments, setInstallments] = useState("1");
  const [customerId, setCustomerId] = useState<string>("none");

  const totalValue = money(total);
  const n = Number.parseInt(installments, 10);
  const valid = description.trim() && n >= 1 && n <= 60 && totalValue >= 0.01 * n && firstDue;

  const create = useMutation({
    mutationFn: () => createInstallments({
      transactionType: "Receivable",
      totalAmount: totalValue,
      description: description.trim(),
      firstDueDate: dayToIso(firstDue),
      installments: n,
      customerId: customerId === "none" ? null : customerId,
    }),
    onSuccess: () => { toast.success(n > 1 ? `${n} parcelas lançadas.` : "Recebimento lançado."); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível lançar."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !create.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Novo recebimento</DialogTitle>
          <DialogDescription>Parcelado em meses, com o mesmo dia de vencimento. As parcelas somam o total.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-desc">Descrição</Label>
            <Input id="rec-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Ex.: Serviço de instalação" />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-customer">Cliente</Label>
            <Select value={customerId} onValueChange={setCustomerId}>
              <SelectTrigger id="rec-customer" aria-label="Cliente"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Sem cliente</SelectItem>
                {(customers ?? []).map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rec-total">Valor total</Label>
            <Input id="rec-total" inputMode="decimal" value={total} onChange={(e) => setTotal(e.target.value)} placeholder="0,00" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rec-n">Parcelas</Label>
            <Input id="rec-n" inputMode="numeric" value={installments} onChange={(e) => setInstallments(e.target.value)} />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-due">1º vencimento</Label>
            <Input id="rec-due" type="date" value={firstDue} onChange={(e) => setFirstDue(e.target.value)} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>Cancelar</Button>
          <Button onClick={() => create.mutate()} disabled={!valid || create.isPending}>Lançar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
