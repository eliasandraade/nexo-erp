import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { Lightbulb } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency, formatDate } from "@/lib/formatters";
import {
  TAX_KEYS, listTaxOpportunities, type ImpactNature, type OpportunityFilter, type OpportunityStatus, type OpportunityType, type RiskLevel,
} from "../api/tax.api";
import { OPPORTUNITY_STATUS, OPPORTUNITY_TYPES, RISK_LEVELS, labelOf } from "../lib/labels";
import { impactsByNature } from "../lib/overview";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge } from "../components/TaxBadges";
import { Field, TaxSubNav } from "../components/TaxShell";
import { TaxPager, TaxQueryError } from "../components/TaxCommon";

const ALL = "__all";
const PAGE = 25;

/** Oportunidades encontradas nos cálculos, com natureza do impacto, risco, responsável e andamento da análise. */
export default function TaxOpportunitiesPage() {
  const [filter, setFilter] = useState<OpportunityFilter>({ status: "", risk: "", type: "", search: "", skip: 0, take: PAGE });
  const set = (patch: Partial<OpportunityFilter>) => setFilter((f) => ({ ...f, ...patch, skip: patch.skip ?? 0 }));
  const list = useQuery({ queryKey: TAX_KEYS.opportunities(filter), queryFn: () => listTaxOpportunities(filter) });
  const byNature = list.data ? impactsByNature(list.data.items) : {};
  const filtered = !!(filter.status || filter.risk || filter.type || filter.search || filter.open || filter.mine);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Oportunidades"
        description="Alternativas encontradas nos cálculos — cada uma com natureza do impacto, risco, fonte e o que precisa ser cumprido. Nada é aplicado sem análise e aprovação."
      />
      <TaxSubNav />

      <div className="flex flex-wrap items-end gap-3">
        <Field id="op-search" label="Buscar" className="w-full sm:w-64">
          <Input id="op-search" value={filter.search ?? ""} onChange={(e) => set({ search: e.target.value })} placeholder="Título da oportunidade" />
        </Field>
        <Field id="op-status" label="Situação" className="w-full sm:w-52">
          <Select value={filter.status || ALL} onValueChange={(v) => set({ status: v === ALL ? "" : (v as OpportunityStatus) })}>
            <SelectTrigger id="op-status"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Todas</SelectItem>
              {(Object.keys(OPPORTUNITY_STATUS) as OpportunityStatus[]).map((s) => <SelectItem key={s} value={s}>{OPPORTUNITY_STATUS[s].label}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id="op-risk" label="Risco" className="w-full sm:w-40">
          <Select value={filter.risk || ALL} onValueChange={(v) => set({ risk: v === ALL ? "" : (v as RiskLevel) })}>
            <SelectTrigger id="op-risk"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Todos</SelectItem>
              {(Object.keys(RISK_LEVELS) as RiskLevel[]).map((r) => <SelectItem key={r} value={r}>{RISK_LEVELS[r].label}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id="op-type" label="Tipo" className="w-full sm:w-60">
          <Select value={filter.type || ALL} onValueChange={(v) => set({ type: v === ALL ? "" : (v as OpportunityType) })}>
            <SelectTrigger id="op-type"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Todos</SelectItem>
              {(Object.keys(OPPORTUNITY_TYPES) as OpportunityType[]).map((k) => <SelectItem key={k} value={k}>{OPPORTUNITY_TYPES[k]}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <label className="flex items-center gap-2 pb-2 text-sm">
          <Checkbox checked={!!filter.open} onCheckedChange={(v) => set({ open: v === true || undefined })} /> Só em aberto
        </label>
        <label className="flex items-center gap-2 pb-2 text-sm">
          <Checkbox checked={!!filter.mine} onCheckedChange={(v) => set({ mine: v === true || undefined })} /> Sob minha responsabilidade
        </label>
      </div>

      {Object.keys(byNature).length > 0 && (
        <div className="space-y-1">
          <p className="text-xs text-muted-foreground">Impacto estimado por natureza, nesta página (naturezas diferentes não se somam):</p>
          <ul className="flex flex-wrap gap-2">
            {(Object.entries(byNature) as [ImpactNature, number][]).map(([n, v]) => (
              <li key={n} className="flex items-center gap-1.5 text-sm"><ImpactBadge nature={n} /><span className="tabular-nums font-medium">{formatCurrency(v)}</span></li>
            ))}
          </ul>
        </div>
      )}

      <SectionCard noPadding>
        {list.isLoading && <div className="p-5"><Skeleton className="h-40 w-full" /></div>}
        {list.isError && <TaxQueryError error={list.error} what="as oportunidades" onRetry={() => void list.refetch()} />}
        {list.data?.items.length === 0 && (
          <EmptyState icon={Lightbulb} title={filtered ? "Nenhuma oportunidade com estes filtros" : "Nenhuma oportunidade"}
            description={filtered ? "Ajuste os filtros para ver outras." : "Elas aparecem quando um cálculo encontra uma alternativa permitida."} compact />
        )}
        {list.data && list.data.items.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Oportunidade</TableHead><TableHead>Natureza</TableHead><TableHead className="text-right">Impacto estimado</TableHead>
                    <TableHead>Risco</TableHead><TableHead className="text-right">Confiança</TableHead><TableHead>Requisitos</TableHead><TableHead>Situação</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {list.data.items.map((o) => (
                    <TableRow key={o.id}>
                      <TableCell className="min-w-56">
                        <Link to={`/tributos/oportunidades/${o.id}`} className="font-medium hover:underline">{o.title}</Link>
                        <p className="text-xs text-muted-foreground">
                          {labelOf(OPPORTUNITY_TYPES, o.type)} · {formatDate(o.createdAt)}{o.assignedToName ? ` · responsável: ${o.assignedToName}` : ""}
                        </p>
                      </TableCell>
                      <TableCell><ImpactBadge nature={o.impactNature} /></TableCell>
                      <TableCell className="text-right tabular-nums">{o.estimatedImpact === null ? "—" : formatCurrency(o.estimatedImpact)}</TableCell>
                      <TableCell><RiskBadge risk={o.risk} /></TableCell>
                      <TableCell className="text-right tabular-nums">{o.confidence}%</TableCell>
                      <TableCell className="whitespace-nowrap text-sm tabular-nums">
                        {o.requirementsTotal ? `${o.requirementsMet ?? 0}/${o.requirementsTotal} atendidos` : "—"}
                      </TableCell>
                      <TableCell><OpportunityStatusBadge status={o.status} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <TaxPager total={list.data.total} skip={filter.skip ?? 0} take={PAGE} onSkip={(skip) => set({ skip })} />
          </>
        )}
      </SectionCard>
    </div>
  );
}
