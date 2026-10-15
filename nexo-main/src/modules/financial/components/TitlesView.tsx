import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlarmClock, CalendarClock, HandCoins, Plus, Receipt } from "lucide-react";
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
import { useSuppliers } from "@/modules/suppliers/hooks/use-suppliers";
import {
  CATEGORY_LABELS, createInstallments, getPayables, getReceivables, LEDGER_KEY, PAYABLE_CATEGORIES, PAYABLES_KEY,
  RECEIVABLES_KEY, settleTransaction, type FinancialCategory, type TitleDto, type TitleStatusFilter,
} from "../api/financial.api";
import { dayToIso, money, todayInput } from "../lib/inputs";

export type TitleDirection = "Receivable" | "Payable";

/** Everything that changes between contas a receber and contas a pagar. */
const COPY = {
  Receivable: {
    title: "Contas a receber",
    description: "O que os clientes devem, por vencimento: vendas a prazo, parcelas e outros recebimentos.",
    newButton: "Novo recebimento",
    settledTab: "Recebidos",
    party: "Cliente",
    action: "Receber",
    paid: "Recebido",
    amountLabel: "Valor recebido",
    discountLabel: "Desconto concedido",
    confirm: "Confirmar recebimento",
    empty: "Vendas a prazo e recebimentos lançados aparecem nesta lista.",
    loadError: "Não foi possível carregar as contas a receber",
    partial: "Recebimento parcial registrado.",
    done: "Título recebido.",
    created: "Recebimento lançado.",
    placeholder: "Ex.: Serviço de instalação",
  },
  Payable: {
    title: "Contas a pagar",
    description: "O que a empresa deve, por vencimento: compras, despesas, parcelas e recorrências.",
    newButton: "Nova conta a pagar",
    settledTab: "Pagos",
    party: "Fornecedor",
    action: "Pagar",
    paid: "Pago",
    amountLabel: "Valor pago",
    discountLabel: "Desconto obtido",
    confirm: "Confirmar pagamento",
    empty: "Compras recebidas, despesas e recorrências aparecem nesta lista.",
    loadError: "Não foi possível carregar as contas a pagar",
    partial: "Pagamento parcial registrado.",
    done: "Título pago.",
    created: "Conta lançada.",
    placeholder: "Ex.: Conta de energia",
  },
} as const;

/** Títulos of one direction: aging, abas por situação, baixa e lançamento parcelado. */
export function TitlesView({ direction }: { direction: TitleDirection }) {
  const copy = COPY[direction];
  const qc = useQueryClient();
  const [status, setStatus] = useState<TitleStatusFilter>("open");
  const [settling, setSettling] = useState<TitleDto | null>(null);
  const [creating, setCreating] = useState(false);
  const key = direction === "Receivable" ? RECEIVABLES_KEY : PAYABLES_KEY;

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: [...key, status],
    queryFn: () => (direction === "Receivable" ? getReceivables(status) : getPayables(status)),
  });

  const refresh = () => {
    void qc.invalidateQueries({ queryKey: key });
    void qc.invalidateQueries({ queryKey: LEDGER_KEY });
  };

  const statusLabel = (s: TitleDto["status"]) =>
    ({ Pending: "A vencer", Overdue: "Vencido", Paid: copy.paid, Cancelled: "Cancelado", Abated: "Abatido (desconto)" })[s] ?? s;

  return (
    <div className="space-y-6">
      <PageHeader
        title={copy.title}
        description={copy.description}
        actions={<Button onClick={() => setCreating(true)}><Plus className="mr-1 h-4 w-4" /> {copy.newButton}</Button>}
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
          <TabsTrigger value="settled">{copy.settledTab}</TabsTrigger>
          <TabsTrigger value="all">Todos</TabsTrigger>
        </TabsList>
      </Tabs>

      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError || !data ? (
        <ErrorState title={copy.loadError} onRetry={() => void refetch()} />
      ) : data.items.length === 0 ? (
        <SectionCard>
          <EmptyState icon={direction === "Receivable" ? HandCoins : Receipt} title="Nada por aqui" description={copy.empty} />
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
                <TableHead>{copy.party}</TableHead>
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
                  <TableCell className="text-sm">{(direction === "Receivable" ? t.customerName : t.supplierName) ?? "—"}</TableCell>
                  <TableCell className="font-medium">{t.description}</TableCell>
                  <TableCell>
                    <Badge variant={t.status === "Overdue" ? "destructive" : t.status === "Paid" ? "secondary" : "outline"}>
                      {statusLabel(t.status)}
                    </Badge>
                    {t.daysOverdue > 0 && <span className="ml-2 text-xs text-muted-foreground">há {t.daysOverdue} dias</span>}
                  </TableCell>
                  <TableCell className="text-right font-medium tabular-nums">{formatCurrency(t.amount)}</TableCell>
                  <TableCell className="text-right">
                    {(t.status === "Pending" || t.status === "Overdue") && (
                      <Button size="sm" variant="outline" onClick={() => setSettling(t)}>{copy.action}</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      )}

      {settling && <SettleDialog direction={direction} title={settling} onClose={() => setSettling(null)} onDone={refresh} />}
      {creating && <NewTitleDialog direction={direction} onClose={() => setCreating(false)} onDone={refresh} />}
    </div>
  );
}

function SettleDialog({ direction, title, onClose, onDone }: {
  direction: TitleDirection; title: TitleDto; onClose: () => void; onDone: () => void;
}) {
  const copy = COPY[direction];
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
    onSuccess: () => { toast.success(left > 0 ? copy.partial : copy.done); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível registrar a baixa."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !settle.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{copy.action}</DialogTitle>
          <DialogDescription>{title.description} — saldo {formatCurrency(title.amount)}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="settle-amount">{copy.amountLabel}</Label>
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
            <Label htmlFor="settle-discount">{copy.discountLabel}</Label>
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
            {copy.confirm}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function NewTitleDialog({ direction, onClose, onDone }: { direction: TitleDirection; onClose: () => void; onDone: () => void }) {
  const copy = COPY[direction];
  const { data: customers } = useCustomers();
  const { data: suppliers } = useSuppliers();
  const [description, setDescription] = useState("");
  const [total, setTotal] = useState("");
  const [firstDue, setFirstDue] = useState(todayInput());
  const [installments, setInstallments] = useState("1");
  const [partyId, setPartyId] = useState<string>("none");
  const [category, setCategory] = useState<FinancialCategory>("OperatingExpense");

  const totalValue = money(total);
  const n = Number.parseInt(installments, 10);
  const valid = description.trim() && n >= 1 && n <= 60 && totalValue >= 0.01 * n && firstDue;
  const parties = direction === "Receivable"
    ? (customers ?? []).map((c) => ({ id: c.id, name: c.name }))
    : (suppliers ?? []).map((s) => ({ id: s.id, name: s.tradeName || s.name }));

  const create = useMutation({
    mutationFn: () => createInstallments({
      transactionType: direction,
      totalAmount: totalValue,
      description: description.trim(),
      firstDueDate: dayToIso(firstDue),
      installments: n,
      ...(direction === "Payable" ? { category } : {}),
      ...(direction === "Receivable"
        ? { customerId: partyId === "none" ? null : partyId }
        : { supplierId: partyId === "none" ? null : partyId }),
    }),
    onSuccess: () => { toast.success(n > 1 ? `${n} parcelas lançadas.` : copy.created); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível lançar."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !create.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{copy.newButton}</DialogTitle>
          <DialogDescription>Parcelado em meses, com o mesmo dia de vencimento. As parcelas somam o total.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="title-desc">Descrição</Label>
            <Input id="title-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder={copy.placeholder} />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="title-party">{copy.party}</Label>
            <Select value={partyId} onValueChange={setPartyId}>
              <SelectTrigger id="title-party" aria-label={copy.party}><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Sem {copy.party.toLowerCase()}</SelectItem>
                {parties.map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          {direction === "Payable" && (
            <div className="space-y-1.5 sm:col-span-2">
              <Label htmlFor="title-category">Categoria</Label>
              <Select value={category} onValueChange={(v) => setCategory(v as FinancialCategory)}>
                <SelectTrigger id="title-category" aria-label="Categoria"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {PAYABLE_CATEGORIES.map((c) => <SelectItem key={c} value={c}>{CATEGORY_LABELS[c]}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          )}
          <div className="space-y-1.5">
            <Label htmlFor="title-total">Valor total</Label>
            <Input id="title-total" inputMode="decimal" value={total} onChange={(e) => setTotal(e.target.value)} placeholder="0,00" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="title-n">Parcelas</Label>
            <Input id="title-n" inputMode="numeric" value={installments} onChange={(e) => setInstallments(e.target.value)} />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="title-due">1º vencimento</Label>
            <Input id="title-due" type="date" value={firstDue} onChange={(e) => setFirstDue(e.target.value)} />
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
