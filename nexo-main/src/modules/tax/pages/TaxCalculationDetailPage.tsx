import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import {
  TAX_KEY, TAX_KEYS, createSplitExpectations, getTaxCalculation, getTaxOpportunity, listSplitExpectations, type SplitProcedure,
} from "../api/tax.api";
import { DIRECTIONS, OPPORTUNITY_TYPES, SOURCES, SPLIT_PROCEDURES, labelOf } from "../lib/labels";
import { errorMessage, formatIsoDate } from "../lib/forms";
import { EngineResult, TechnicalTrail } from "../components/EngineResult";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge } from "../components/TaxBadges";
import { Field, TaxNote, TaxSubNav } from "../components/TaxShell";
import { SplitTable } from "../components/SplitTable";

/** One calculation: what happened, how much it costs, the opportunities it found, split payment and the technical trail. */
export default function TaxCalculationDetailPage() {
  const { id = "" } = useParams();
  const calc = useQuery({ queryKey: TAX_KEYS.calculation(id), queryFn: () => getTaxCalculation(id), enabled: !!id });
  const c = calc.data;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária · cálculo"
        title={c ? (c.description || labelOf(DIRECTIONS, c.direction)) : "Cálculo"}
        description={c ? `${labelOf(DIRECTIONS, c.direction)} de ${formatIsoDate(c.referenceDate)} · origem: ${labelOf(SOURCES, c.source)} · perfil v${c.profileVersionNumber} · calculado em ${formatDateTime(c.createdAt)}` : undefined}
        actions={<Button asChild variant="outline"><Link to="/tributos/calculos"><ArrowLeft className="mr-1.5 h-4 w-4" /> Cálculos</Link></Button>}
      />
      <TaxSubNav />

      {calc.isLoading && <Skeleton className="h-64 w-full" />}
      {calc.isError && <ErrorState title="Não foi possível abrir o cálculo" onRetry={() => void calc.refetch()} />}
      {c && (
        <>
          {c.reused && <TaxNote>Esta mesma operação já tinha sido calculada com as mesmas regras: o resultado guardado foi reaproveitado.</TaxNote>}
          <SectionCard title="Quanto custa e o que aconteceu">
            <EngineResult output={c.output} input={c.input} />
          </SectionCard>
          <Opportunities ids={c.opportunityIds} />
          <SplitSection calculationId={c.id} />
          <TechnicalTrail
            output={c.output}
            hashes={{ calculationId: c.id, engineVersion: c.engineVersion, inputHash: c.inputHash, ruleSetHash: c.ruleSetHash, outputHash: c.outputHash, profileVersionNumber: c.profileVersionNumber }}
          />
        </>
      )}
    </div>
  );
}

function Opportunities({ ids }: Readonly<{ ids: string[] }>) {
  const items = useQueries({ queries: ids.map((oid) => ({ queryKey: TAX_KEYS.opportunity(oid), queryFn: () => getTaxOpportunity(oid) })) });
  return (
    <SectionCard title="Existe alternativa?" description="Oportunidades encontradas neste cálculo. Nenhuma é aplicada sem revisão e aprovação.">
      {ids.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma oportunidade encontrada para esta operação.</p>}
      <ul className="space-y-2">
        {items.map((q, i) => (
          <li key={ids[i]} className="rounded-md border border-border p-3 text-sm">
            {q.isLoading && <Skeleton className="h-5 w-2/3" />}
            {q.isError && <span className="text-destructive">Não foi possível ler esta oportunidade.</span>}
            {q.data && (
              <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0">
                  <Link to={`/tributos/oportunidades/${q.data.id}`} className="font-medium hover:underline">{q.data.title}</Link>
                  <p className="text-xs text-muted-foreground">{labelOf(OPPORTUNITY_TYPES, q.data.type)}</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <ImpactBadge nature={q.data.impactNature} />
                  {q.data.estimatedImpact !== null && <span className="tabular-nums font-medium">{formatCurrency(q.data.estimatedImpact)}</span>}
                  <RiskBadge risk={q.data.risk} />
                  <OpportunityStatusBadge status={q.data.status} />
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}

function SplitSection({ calculationId }: Readonly<{ calculationId: string }>) {
  const qc = useQueryClient();
  const [procedure, setProcedure] = useState<SplitProcedure>("Standard");
  const expectations = useQuery({ queryKey: TAX_KEYS.splitExpectations(calculationId), queryFn: () => listSplitExpectations(calculationId) });
  const create = useMutation({
    mutationFn: () => createSplitExpectations(calculationId, procedure),
    onSuccess: () => { toast.success("Retenções esperadas registradas."); void qc.invalidateQueries({ queryKey: [...TAX_KEY, "split"] }); void qc.invalidateQueries({ queryKey: TAX_KEYS.overview }); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <SectionCard title="Split payment" description="Quanto deve ser retido na liquidação do pagamento, para conferir depois com o que foi retido de fato.">
      <div className="space-y-3">
        {expectations.data && expectations.data.length === 0 && (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <Field id="split-proc" label="Procedimento" className="sm:w-56">
              <Select value={procedure} onValueChange={(v) => setProcedure(v as SplitProcedure)}>
                <SelectTrigger id="split-proc"><SelectValue /></SelectTrigger>
                <SelectContent>{(Object.keys(SPLIT_PROCEDURES) as SplitProcedure[]).map((p) => <SelectItem key={p} value={p}>{SPLIT_PROCEDURES[p]}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Button type="button" variant="outline" disabled={create.isPending} onClick={() => create.mutate()}>
              {create.isPending ? "Registrando…" : "Registrar retenções esperadas"}
            </Button>
          </div>
        )}
        {expectations.isError && <ErrorState title="Não foi possível ler as retenções" onRetry={() => void expectations.refetch()} compact />}
        {expectations.data && expectations.data.length > 0 && <SplitTable items={expectations.data} />}
      </div>
    </SectionCard>
  );
}
