import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Calculator } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { TAX_KEY, TAX_KEYS, calculateSale, createTaxCalculation, listTaxCalculations, type TaxCalculationDto } from "../api/tax.api";
import { DIRECTIONS, SOURCES, labelOf } from "../lib/labels";
import { emptyOperation, errorMessage, formatIsoDate, isProfileMissing, operationProblems, toOperationRequest, type OperationDraft } from "../lib/forms";
import { OperationEditor } from "../components/OperationEditor";
import { Field, ProblemList, ProfileMissingNotice, TaxSubNav } from "../components/TaxShell";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Cálculos: calcular uma operação (ou uma venda já registrada) e ver os cálculos guardados. */
export default function TaxCalculationsPage() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [draft, setDraft] = useState<OperationDraft>(() => emptyOperation());
  const [touched, setTouched] = useState(false);
  const [profileMissing, setProfileMissing] = useState(false);
  const [saleId, setSaleId] = useState("");
  const list = useQuery({ queryKey: TAX_KEYS.calculations, queryFn: () => listTaxCalculations(50) });
  const problems = operationProblems(draft);

  const done = (c: TaxCalculationDto) => {
    void qc.invalidateQueries({ queryKey: TAX_KEY });
    toast.success(c.reused ? "Mesma operação já calculada: resultado guardado reaproveitado." : "Cálculo feito.");
    navigate(`/tributos/calculos/${c.id}`);
  };
  const failed = (e: unknown) => {
    if (isProfileMissing(e)) setProfileMissing(true);
    else toast.error(errorMessage(e));
  };

  const calculate = useMutation({ mutationFn: () => createTaxCalculation(toOperationRequest(draft)), onSuccess: done, onError: failed });
  const fromSale = useMutation({ mutationFn: () => calculateSale(saleId.trim()), onSuccess: done, onError: failed });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Cálculos"
        description="Calcule a CBS e o IBS de uma operação. Cada cálculo guarda as regras, a versão do perfil e a fonte usadas — dá para reproduzir depois."
      />
      <TaxSubNav />

      {profileMissing && <ProfileMissingNotice />}

      <SectionCard title="Calcular uma operação" description="Informe o que foi vendido ou comprado. Nada é lançado em vendas, livros ou financeiro.">
        <form onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) calculate.mutate(); }} className="space-y-4">
          <OperationEditor idPrefix="calc" value={draft} onChange={setDraft} />
          {touched && <ProblemList problems={problems} />}
          <Button type="submit" disabled={calculate.isPending}>{calculate.isPending ? "Calculando…" : "Calcular"}</Button>
        </form>
      </SectionCard>

      <SectionCard title="Calcular uma venda registrada" description="Usa os itens, descontos e a data da venda.">
        <form className="flex flex-col gap-3 sm:flex-row sm:items-end" onSubmit={(e) => { e.preventDefault(); if (GUID.test(saleId.trim())) fromSale.mutate(); else toast.error("Cole o código da venda (aparece no endereço da venda)."); }}>
          <Field id="calc-sale" label="Código da venda" className="sm:w-96"><Input id="calc-sale" value={saleId} onChange={(e) => setSaleId(e.target.value)} placeholder="00000000-0000-0000-0000-000000000000" /></Field>
          <Button type="submit" variant="outline" disabled={fromSale.isPending}>{fromSale.isPending ? "Calculando…" : "Calcular venda"}</Button>
        </form>
      </SectionCard>

      <SectionCard title="Cálculos guardados" description="Os 50 mais recentes." noPadding>
        {list.isLoading && <div className="p-5"><Skeleton className="h-32 w-full" /></div>}
        {list.isError && <ErrorState title="Não foi possível listar os cálculos" onRetry={() => void list.refetch()} compact />}
        {list.data && list.data.length === 0 && <EmptyState icon={Calculator} title="Nenhum cálculo ainda" description="O primeiro aparece aqui assim que você calcular." compact />}
        {list.data && list.data.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Data</TableHead><TableHead>Operação</TableHead><TableHead>Origem</TableHead>
                  <TableHead className="text-right">Base</TableHead><TableHead className="text-right">Imposto</TableHead><TableHead className="text-right">Oportunidades</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="whitespace-nowrap">{formatIsoDate(c.referenceDate)}</TableCell>
                    <TableCell className="min-w-48">
                      <Link to={`/tributos/calculos/${c.id}`} className="font-medium hover:underline">{c.description || labelOf(DIRECTIONS, c.direction)}</Link>
                      {c.description && <p className="text-xs text-muted-foreground">{labelOf(DIRECTIONS, c.direction)}</p>}
                    </TableCell>
                    <TableCell>{labelOf(SOURCES, c.source)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(c.totalBase)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(c.totalTax)}</TableCell>
                    <TableCell className="text-right tabular-nums">{c.openOpportunities || "—"}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </SectionCard>
    </div>
  );
}
