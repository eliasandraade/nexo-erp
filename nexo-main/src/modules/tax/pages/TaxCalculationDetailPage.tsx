import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import {
  TAX_KEY, TAX_KEYS, createSplitExpectations, detectOpportunitiesAgain, getTaxCalculation, getTaxOpportunity, listSplitExpectations,
  type SplitProcedure, type TaxDirection,
} from "../api/tax.api";
import { TaxQueryError, toastError } from "../components/TaxCommon";
import {
  COMPLIANCE_STATUS, COVERAGE_STATUS, DIRECTIONS, OPPORTUNITY_TYPES, SOURCES, SPLIT_EXPECTATION_COPY, SPLIT_PROCEDURES, SYSTEM_CALCULATION_COPY, labelOf, toneOf,
} from "../lib/labels";
import { formatIsoDate } from "../lib/forms";
import { EngineResult, TechnicalTrail } from "../components/EngineResult";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge, ToneBadge } from "../components/TaxBadges";
import { Field, TaxNote, TaxSubNav } from "../components/TaxShell";
import { SplitTable } from "../components/SplitTable";
import { ApprovalGovernance } from "../components/ApprovalGovernance";

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
      {calc.isError && <TaxQueryError error={calc.error} what="o cálculo" onRetry={() => void calc.refetch()} />}
      {c && (
        <>
          {c.reused && <TaxNote>Esta mesma operação já tinha sido calculada com as mesmas regras: o resultado guardado foi reaproveitado.</TaxNote>}
          <TaxNote>{SYSTEM_CALCULATION_COPY}</TaxNote>
          <SectionCard title="Com quais dados e premissas">
            <div className="space-y-2 text-sm">
              <p className="flex flex-wrap items-center gap-2">
                <ToneBadge label={labelOf(COVERAGE_STATUS, c.coverage.status)} tone={toneOf(COVERAGE_STATUS, c.coverage.status)} />
                <span className="text-xs text-muted-foreground">matriz {c.coverage.policyVersion}</span>
              </p>
              {c.coverage.assumptions.length > 0 && (
                <ul className="list-disc pl-5 text-muted-foreground">{c.coverage.assumptions.map((a) => <li key={a}>{a}</li>)}</ul>
              )}
              {c.advisories.length > 0 && (
                <ul className="list-disc pl-5">{c.advisories.map((a) => <li key={a}>{a}</li>)}</ul>
              )}
              {c.complianceAssessment && (
                <p className="text-xs text-muted-foreground">
                  Obrigações acessórias usadas: {labelOf(COMPLIANCE_STATUS, c.complianceAssessment.status)} de {formatIsoDate(c.complianceAssessment.periodFrom)} a{" "}
                  {formatIsoDate(c.complianceAssessment.periodTo)} ({c.complianceAssessment.scope}).
                </p>
              )}
              {c.currentComplianceAssessment && (
                <p className="text-xs text-warning">
                  Hoje está registrada outra avaliação das obrigações acessórias para esta data
                  ({labelOf(COMPLIANCE_STATUS, c.currentComplianceAssessment.status)}). Este cálculo continua com a avaliação que usou.
                </p>
              )}
            </div>
          </SectionCard>
          {c.governance && (
            <SectionCard title="Com qual aprovação das regras" description="Registrado quando o cálculo rodou: o modo de aprovação e, lado a lado, a aprovação interna para o piloto e a revisão profissional de cada regra.">
              <ApprovalGovernance governance={c.governance} />
            </SectionCard>
          )}
          <SectionCard title="Quanto custa e o que aconteceu">
            <EngineResult output={c.output} input={c.input} />
          </SectionCard>
          <Opportunities ids={c.opportunityIds} calculationId={c.id} />
          <SplitSection calculationId={c.id} direction={c.direction} />
          <TechnicalTrail
            output={c.output}
            hashes={{ calculationId: c.id, engineVersion: c.engineVersion, inputHash: c.inputHash, ruleSetHash: c.ruleSetHash, outputHash: c.outputHash, profileVersionNumber: c.profileVersionNumber }}
          />
        </>
      )}
    </div>
  );
}

function Opportunities({ ids, calculationId }: Readonly<{ ids: string[]; calculationId: string }>) {
  const qc = useQueryClient();
  const items = useQueries({ queries: ids.map((oid) => ({ queryKey: TAX_KEYS.opportunity(oid), queryFn: () => getTaxOpportunity(oid) })) });
  const detect = useMutation({
    mutationFn: () => detectOpportunitiesAgain(calculationId),
    onSuccess: (created) => {
      toast.success(created.length > 0 ? `${created.length} alternativa(s) nova(s) encontrada(s).` : "Nenhuma alternativa nova.");
      void qc.invalidateQueries({ queryKey: TAX_KEYS.calculation(calculationId) });
    },
    onError: toastError,
  });
  return (
    <SectionCard
      title="Existe alternativa?"
      description="Oportunidades encontradas neste cálculo. Nenhuma é aplicada sem revisão e aprovação."
      actions={
        <Button type="button" variant="outline" size="sm" disabled={detect.isPending} onClick={() => detect.mutate()}>
          {detect.isPending ? "Procurando…" : "Procurar de novo"}
        </Button>
      }
    >
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

function SplitSection({ calculationId, direction }: Readonly<{ calculationId: string; direction: TaxDirection }>) {
  const qc = useQueryClient();
  const [procedure, setProcedure] = useState<SplitProcedure>("Standard");
  const expectations = useQuery({
    queryKey: TAX_KEYS.splitExpectations({ calculationId }),
    queryFn: () => listSplitExpectations({ calculationId, take: 50 }),
    enabled: direction === "Outbound",
  });
  const create = useMutation({
    mutationFn: () => createSplitExpectations(calculationId, procedure),
    onSuccess: () => { toast.success("Expectativas gerenciais de segregação registradas."); void qc.invalidateQueries({ queryKey: [...TAX_KEY, "split"] }); void qc.invalidateQueries({ queryKey: TAX_KEYS.overview }); },
    onError: toastError,
  });

  if (direction !== "Outbound")
    return (
      <SectionCard title="Split payment">
        <p className="text-sm text-muted-foreground">Retenção esperada vale para operações de saída (o estabelecimento é o fornecedor).</p>
      </SectionCard>
    );
  return (
    <SectionCard title="Split payment" description={`${SPLIT_EXPECTATION_COPY} Serve para conferir depois com o que foi retido de fato, liquidação a liquidação.`}>
      <div className="space-y-3">
        {expectations.data?.items.length === 0 && (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <Field id="split-proc" label="Procedimento" className="sm:w-56">
              <Select value={procedure} onValueChange={(v) => setProcedure(v as SplitProcedure)}>
                <SelectTrigger id="split-proc"><SelectValue /></SelectTrigger>
                <SelectContent>{(Object.keys(SPLIT_PROCEDURES) as SplitProcedure[]).map((p) => <SelectItem key={p} value={p}>{SPLIT_PROCEDURES[p]}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Button type="button" variant="outline" disabled={create.isPending} onClick={() => create.mutate()}>
              {create.isPending ? "Registrando…" : "Registrar expectativa de segregação"}
            </Button>
          </div>
        )}
        {expectations.isLoading && <Skeleton className="h-16 w-full" />}
        {expectations.isError && <TaxQueryError error={expectations.error} what="as retenções" onRetry={() => void expectations.refetch()} />}
        {expectations.data && expectations.data.items.length > 0 && <SplitTable items={expectations.data.items} />}
      </div>
    </SectionCard>
  );
}
