import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Calculator } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import {
  TAX_KEY, TAX_KEYS, calculateSale, createTaxCalculation, listTaxCalculations, previewTaxCoverage,
  type TaxCalculationDto, type TaxCoverageDecision, type TaxDirection,
} from "../api/tax.api";
import { COVERAGE_ADVICE, COVERAGE_STATUS, DIRECTIONS, NOT_SUPPORTED_COPY, SOURCES, labelOf } from "../lib/labels";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TaxPager, TaxQueryError, toastError } from "../components/TaxCommon";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { ApiError } from "@/services/api-client";
import { emptyOperation, errorMessage, formatIsoDate, isProfileMissing, operationProblems, toOperationRequest, type OperationDraft } from "../lib/forms";
import { OperationEditor } from "../components/OperationEditor";
import { Field, ProblemList, ProfileMissingNotice, TaxSubNav } from "../components/TaxShell";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const PAGE = 25;
const ALL = "__all";

/** The coverage status a refusal names between parentheses ("… (RequiresClassification)"), when it does. */
function coverageStatusOf(message: string): keyof typeof COVERAGE_ADVICE | null {
  const m = Object.keys(COVERAGE_ADVICE).find((s) => message.includes(`(${s})`));
  return (m as keyof typeof COVERAGE_ADVICE) ?? null;
}

/** Cálculos: calcular uma operação (ou uma venda já registrada) e ver os cálculos guardados. */
export default function TaxCalculationsPage() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [draft, setDraft] = useState<OperationDraft>(() => emptyOperation());
  const [touched, setTouched] = useState(false);
  const [profileMissing, setProfileMissing] = useState(false);
  const [notSupported, setNotSupported] = useState<{ status: keyof typeof COVERAGE_ADVICE | null; reasons: string[] } | null>(null);
  const [coverage, setCoverage] = useState<TaxCoverageDecision | null>(null);
  const [saleId, setSaleId] = useState("");
  const [direction, setDirection] = useState<TaxDirection | "">("");
  const [skip, setSkip] = useState(0);
  const list = useQuery({
    queryKey: [...TAX_KEYS.calculations, direction, skip],
    queryFn: () => listTaxCalculations({ direction, skip, take: PAGE }),
  });
  const problems = operationProblems(draft);

  const done = (c: TaxCalculationDto) => {
    setNotSupported(null);
    void qc.invalidateQueries({ queryKey: TAX_KEY });
    toast.success(c.reused ? "Mesma operação já calculada: resultado guardado reaproveitado." : "Cálculo feito.");
    void navigate(`/tributos/calculos/${c.id}`);
  };
  const failed = (e: unknown) => {
    if (isProfileMissing(e)) setProfileMissing(true);
    else if (e instanceof ApiError && e.code === "tax_operation_not_supported")
      setNotSupported({ status: coverageStatusOf(e.message), reasons: e.details.length > 0 ? e.details : [errorMessage(e)] });
    else toastError(e);
  };
  const check = useMutation({
    mutationFn: () => previewTaxCoverage(toOperationRequest(draft)),
    onSuccess: (d) => { setCoverage(d); setNotSupported(null); },
    onError: failed,
  });

  const calculate = useMutation({ mutationFn: () => createTaxCalculation(toOperationRequest(draft)), onSuccess: done, onError: failed });
  const fromSale = useMutation({ mutationFn: () => calculateSale(saleId.trim()), onSuccess: done, onError: failed });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Cálculos"
        description="Cálculo do sistema com as regras e os dados indicados. Cada cálculo guarda as regras, a versão do perfil e a fonte usadas — dá para reproduzir depois. Fora do que a versão atual suporta, nada é calculado."
      />
      <TaxSubNav />

      {profileMissing && <ProfileMissingNotice />}
      {notSupported && (
        <Alert variant="destructive">
          <AlertTitle>{NOT_SUPPORTED_COPY}{notSupported.status ? ` — ${labelOf(COVERAGE_STATUS, notSupported.status)}` : ""}</AlertTitle>
          <AlertDescription>
            <p>Nada foi calculado — o sistema não devolve zero para o que não modela. O motivo ficou registrado.</p>
            {notSupported.status && <p className="mt-1">{COVERAGE_ADVICE[notSupported.status]}</p>}
            <ul className="mt-1 list-disc pl-5">{notSupported.reasons.map((r) => <li key={r}>{r}</li>)}</ul>
          </AlertDescription>
        </Alert>
      )}
      {coverage && (
        <Alert>
          <AlertTitle>Prévia do recorte: {labelOf(COVERAGE_STATUS, coverage.status)} ({coverage.policyVersion})</AlertTitle>
          <AlertDescription>
            <p>{COVERAGE_ADVICE[coverage.status]} Nada foi calculado nem registrado.</p>
            {coverage.findings.length > 0 && (
              <ul className="mt-1 list-disc pl-5">
                {coverage.findings.map((f) => <li key={`${f.code}-${f.lineIndex ?? 0}`}>{f.lineIndex ? `Linha ${f.lineIndex}: ` : ""}{f.message}</li>)}
              </ul>
            )}
          </AlertDescription>
        </Alert>
      )}

      <SectionCard title="Calcular uma operação" description="Informe o que foi vendido ou comprado. Nada é lançado em vendas, livros ou financeiro.">
        <form onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) calculate.mutate(); }} className="space-y-4">
          <OperationEditor idPrefix="calc" value={draft} onChange={setDraft} />
          {touched && <ProblemList problems={problems} />}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={calculate.isPending}>{calculate.isPending ? "Calculando…" : "Calcular"}</Button>
            <Button type="button" variant="outline" disabled={check.isPending || problems.length > 0} onClick={() => check.mutate()}>
              {check.isPending ? "Conferindo…" : "Conferir se está no recorte"}
            </Button>
          </div>
        </form>
      </SectionCard>

      <SectionCard title="Calcular uma venda registrada" description="Usa os itens, descontos e a data da venda.">
        <form className="flex flex-col gap-3 sm:flex-row sm:items-end" onSubmit={(e) => { e.preventDefault(); if (GUID.test(saleId.trim())) fromSale.mutate(); else toast.error("Cole o código da venda (aparece no endereço da venda)."); }}>
          <Field id="calc-sale" label="Código da venda" className="sm:w-96"><Input id="calc-sale" value={saleId} onChange={(e) => setSaleId(e.target.value)} placeholder="00000000-0000-0000-0000-000000000000" /></Field>
          <Button type="submit" variant="outline" disabled={fromSale.isPending}>{fromSale.isPending ? "Calculando…" : "Calcular venda"}</Button>
        </form>
      </SectionCard>

      <SectionCard title="Cálculos guardados" description="Mais recentes primeiro. Venda (saída) e compra (entrada) são operações diferentes." noPadding>
        <div className="flex flex-wrap items-end gap-3 border-b p-4">
          <Field id="calc-direction" label="Direção" className="w-56">
            <Select value={direction || ALL} onValueChange={(v) => { setDirection(v === ALL ? "" : (v as TaxDirection)); setSkip(0); }}>
              <SelectTrigger id="calc-direction"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Todas</SelectItem>
                {(Object.keys(DIRECTIONS) as TaxDirection[]).map((d) => <SelectItem key={d} value={d}>{DIRECTIONS[d]}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
        </div>
        {list.isLoading && <div className="p-5"><Skeleton className="h-32 w-full" /></div>}
        {list.isError && <TaxQueryError error={list.error} what="os cálculos" onRetry={() => void list.refetch()} />}
        {list.data?.items.length === 0 && (
          <EmptyState icon={Calculator} title={direction ? "Nenhum cálculo nesta direção" : "Nenhum cálculo ainda"}
            description="O primeiro aparece aqui assim que você calcular." compact />
        )}
        {list.data && list.data.items.length > 0 && (
          <>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Data</TableHead><TableHead>Operação</TableHead><TableHead>Origem</TableHead>
                  <TableHead className="text-right">Base</TableHead><TableHead className="text-right">Imposto</TableHead><TableHead className="text-right">Oportunidades</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((c) => (
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
          <TaxPager total={list.data.total} skip={skip} take={PAGE} onSkip={setSkip} />
          </>
        )}
      </SectionCard>
    </div>
  );
}
