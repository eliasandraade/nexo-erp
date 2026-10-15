import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Repeat } from "lucide-react";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { formatCurrency } from "@/lib/formatters";
import { useSuppliers } from "@/modules/suppliers/hooks/use-suppliers";
import { TitlesView } from "../components/TitlesView";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  CATEGORY_LABELS, createRecurrence, deactivateRecurrence, LEDGER_KEY, listRecurrences, PAYABLE_CATEGORIES, PAYABLES_KEY,
  RECURRENCES_KEY, type FinancialCategory, type RecurrenceDto,
} from "../api/financial.api";
import { money, monthInput } from "../lib/inputs";

/**
 * Contas a pagar: compras, despesas e parcelas por vencimento — e as despesas recorrentes, que viram
 * um título por mês (até o fim do mês seguinte) e se pagam como qualquer outro.
 */
export default function ContasPagarPage() {
  return (
    <div className="space-y-6">
      <TitlesView direction="Payable" />
      <RecurrencesSection />
    </div>
  );
}

function RecurrencesSection() {
  const qc = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [ending, setEnding] = useState<RecurrenceDto | null>(null);
  const { data } = useQuery({ queryKey: RECURRENCES_KEY, queryFn: listRecurrences });

  const stop = useMutation({
    mutationFn: deactivateRecurrence,
    onSuccess: () => {
      toast.success("Recorrência encerrada. Os títulos já gerados continuam na lista.");
      setEnding(null);
      void qc.invalidateQueries({ queryKey: RECURRENCES_KEY });
    },
    onError: (e: Error) => toast.error(e.message || "Não foi possível encerrar."),
  });

  return (
    <SectionCard
      title="Despesas recorrentes"
      actions={<Button variant="outline" size="sm" onClick={() => setCreating(true)}><Plus className="mr-1 h-4 w-4" /> Nova recorrência</Button>}
    >
      {!data?.length ? (
        <EmptyState icon={Repeat} title="Nenhuma recorrência" description="Aluguel, internet, mensalidades: cadastre uma vez e o título de cada mês aparece sozinho." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Descrição</TableHead>
              <TableHead>Fornecedor</TableHead>
              <TableHead>Vence todo dia</TableHead>
              <TableHead className="text-right">Valor</TableHead>
              <TableHead className="w-28" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  <div className="font-medium">{r.description}</div>
                  <div className="text-xs text-muted-foreground">
                    {CATEGORY_LABELS[r.category] ?? r.category} · desde {r.startMonth.slice(5, 7)}/{r.startMonth.slice(0, 4)}
                  </div>
                </TableCell>
                <TableCell className="text-sm">{r.supplierName ?? "—"}</TableCell>
                <TableCell className="text-sm">{r.dayOfMonth}</TableCell>
                <TableCell className="text-right font-medium tabular-nums">{formatCurrency(r.amount)}</TableCell>
                <TableCell className="text-right">
                  <Button size="sm" variant="ghost" onClick={() => setEnding(r)}>Encerrar</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <AlertDialog open={!!ending} onOpenChange={(v) => { if (!v && !stop.isPending) setEnding(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Encerrar “{ending?.description}”?</AlertDialogTitle>
            <AlertDialogDescription>
              Os próximos meses deixam de ser gerados. Os títulos já lançados continuam na lista para pagar ou cancelar.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={stop.isPending}>Voltar</AlertDialogCancel>
            <AlertDialogAction disabled={stop.isPending} onClick={(e) => { e.preventDefault(); if (ending) stop.mutate(ending.id); }}>
              Encerrar recorrência
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      {creating && (
        <NewRecurrenceDialog
          onClose={() => setCreating(false)}
          onDone={() => {
            void qc.invalidateQueries({ queryKey: RECURRENCES_KEY });
            void qc.invalidateQueries({ queryKey: PAYABLES_KEY });
            void qc.invalidateQueries({ queryKey: LEDGER_KEY });
          }}
        />
      )}
    </SectionCard>
  );
}

function NewRecurrenceDialog({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const { data: suppliers } = useSuppliers();
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [day, setDay] = useState("10");
  const [start, setStart] = useState(monthInput());
  const [supplierId, setSupplierId] = useState("none");
  const [category, setCategory] = useState<FinancialCategory>("OperatingExpense");

  const value = money(amount);
  const dayN = /^\d{1,2}$/.test(day.trim()) ? Number(day.trim()) : NaN;
  // type="month" is a plain text box on some desktop browsers: accept only yyyy-MM inside the allowed window
  const startValid = /^\d{4}-\d{2}$/.test(start) && start >= monthInput(-12) && start <= monthInput(24);
  const valid = description.trim() && value > 0 && dayN >= 1 && dayN <= 31 && startValid;

  const create = useMutation({
    mutationFn: () => createRecurrence({
      transactionType: "Payable",
      description: description.trim(),
      amount: value,
      dayOfMonth: dayN,
      startMonth: `${start}-01`,
      category,
      supplierId: supplierId === "none" ? null : supplierId,
    }),
    onSuccess: () => { toast.success("Recorrência criada. Os títulos deste mês e do próximo já estão na lista."); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível criar a recorrência."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !create.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Nova recorrência</DialogTitle>
          <DialogDescription>Um título por mês, no dia escolhido (no último dia, quando o mês é mais curto).</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-description">Descrição da recorrência</Label>
            <Input id="rec-description" value={description} maxLength={480} onChange={(e) => setDescription(e.target.value)} placeholder="Ex.: Aluguel da loja" />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-supplier">Fornecedor da recorrência</Label>
            <Select value={supplierId} onValueChange={setSupplierId}>
              <SelectTrigger id="rec-supplier" aria-label="Fornecedor da recorrência"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Sem fornecedor</SelectItem>
                {(suppliers ?? []).map((s) => <SelectItem key={s.id} value={s.id}>{s.tradeName || s.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-category">Categoria da recorrência</Label>
            <Select value={category} onValueChange={(v) => setCategory(v as FinancialCategory)}>
              <SelectTrigger id="rec-category" aria-label="Categoria da recorrência"><SelectValue /></SelectTrigger>
              <SelectContent>
                {PAYABLE_CATEGORIES.map((c) => <SelectItem key={c} value={c}>{CATEGORY_LABELS[c]}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rec-amount">Valor mensal</Label>
            <Input id="rec-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0,00" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rec-day">Dia do vencimento</Label>
            <Input id="rec-day" inputMode="numeric" value={day} onChange={(e) => setDay(e.target.value)} />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="rec-start">A partir de</Label>
            <Input id="rec-start" type="month" value={start} min={monthInput(-12)} max={monthInput(24)}
              placeholder="aaaa-mm" onChange={(e) => setStart(e.target.value)} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>Cancelar</Button>
          <Button onClick={() => create.mutate()} disabled={!valid || create.isPending}>Criar recorrência</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
