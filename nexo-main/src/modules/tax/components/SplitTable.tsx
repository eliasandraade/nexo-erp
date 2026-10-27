import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import { TAX_KEY, recordSplitSettlement, type SplitExpectationDto } from "../api/tax.api";
import { SPLIT_PROCEDURES, SPLIT_STATUS, TAX_NAMES, labelOf, toneOf } from "../lib/labels";
import { brazilTodayIso, errorMessage, parseDecimal } from "../lib/forms";
import { ToneBadge } from "./TaxBadges";
import { Field, ProblemList } from "./TaxShell";

/** Expected vs retained per tax, with a manual record of what the payment actually retained. */
export function SplitTable({ items, showCalculation = false }: Readonly<{ items: SplitExpectationDto[]; showCalculation?: boolean }>) {
  const [recording, setRecording] = useState<SplitExpectationDto | null>(null);
  return (
    <>
      <div className="overflow-x-auto rounded-md border border-border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Tributo</TableHead>
              {showCalculation && <TableHead>Cálculo</TableHead>}
              <TableHead className="text-right">Esperado</TableHead>
              <TableHead className="text-right">Retido</TableHead>
              <TableHead className="text-right">Diferença</TableHead>
              <TableHead>Situação</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((s) => (
              <TableRow key={s.id}>
                <TableCell className="whitespace-nowrap">
                  {labelOf(TAX_NAMES, s.tax)}
                  <p className="text-xs text-muted-foreground">{labelOf(SPLIT_PROCEDURES, s.procedure)}</p>
                </TableCell>
                {showCalculation && <TableCell><Link to={`/tributos/calculos/${s.calculationId}`} className="text-sm hover:underline">Abrir</Link></TableCell>}
                <TableCell className="text-right tabular-nums">{formatCurrency(s.expected)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCurrency(s.actual)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCurrency(s.difference)}</TableCell>
                <TableCell>
                  <ToneBadge label={labelOf(SPLIT_STATUS, s.status)} tone={toneOf(SPLIT_STATUS, s.status)} />
                  {s.settlements.length > 0 && (
                    <p className="mt-1 text-xs text-muted-foreground">
                      {s.settlements.length} {s.settlements.length === 1 ? "registro" : "registros"} · último {formatDateTime(s.settlements[s.settlements.length - 1].settledAt)}
                    </p>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  <Button type="button" size="sm" variant="outline" onClick={() => setRecording(s)}>Registrar retenção</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <SettlementDialog expectation={recording} onClose={() => setRecording(null)} />
    </>
  );
}

function SettlementDialog({ expectation, onClose }: Readonly<{ expectation: SplitExpectationDto | null; onClose: () => void }>) {
  const qc = useQueryClient();
  const [amount, setAmount] = useState("");
  const [reference, setReference] = useState("");
  const [date, setDate] = useState(brazilTodayIso());
  const [touched, setTouched] = useState(false);
  const value = parseDecimal(amount);
  const problems = [
    ...(value === null || Number.isNaN(value) || value < 0 ? ["Informe o valor retido."] : []),
    ...(!reference.trim() ? ["Informe a referência (ex.: identificador da liquidação no banco)."] : []),
    ...(!/^\d{4}-\d{2}-\d{2}$/.test(date) ? ["Informe a data."] : []),
  ];
  const save = useMutation({
    mutationFn: () => recordSplitSettlement(expectation!.id, value!, reference.trim(), `${date}T12:00:00Z`),
    onSuccess: () => {
      toast.success("Retenção registrada.");
      void qc.invalidateQueries({ queryKey: TAX_KEY });
      setAmount(""); setReference(""); setTouched(false);
      onClose();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Dialog open={expectation !== null} onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Registrar retenção</DialogTitle>
          <DialogDescription>
            {expectation && `${labelOf(TAX_NAMES, expectation.tax)}: esperado ${formatCurrency(expectation.expected)}. `}
            Lançamento manual do que o pagamento reteve.
          </DialogDescription>
        </DialogHeader>
        <form id="split-settle" className="space-y-3" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
          <Field id="ss-amount" label="Valor retido (R$)"><Input id="ss-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} /></Field>
          <Field id="ss-ref" label="Referência"><Input id="ss-ref" maxLength={100} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
          <Field id="ss-date" label="Data da liquidação"><Input id="ss-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></Field>
          {touched && <ProblemList problems={problems} />}
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="submit" form="split-settle" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
