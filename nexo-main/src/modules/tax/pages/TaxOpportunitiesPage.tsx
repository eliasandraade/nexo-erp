import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { Lightbulb } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency, formatDate } from "@/lib/formatters";
import { TAX_KEYS, listTaxOpportunities, type ImpactNature, type OpportunityStatus } from "../api/tax.api";
import { OPPORTUNITY_STATUS, OPPORTUNITY_TYPES, labelOf } from "../lib/labels";
import { impactsByNature } from "../lib/overview";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge } from "../components/TaxBadges";
import { Field, TaxSubNav } from "../components/TaxShell";

const ALL = "__all";

/** Oportunidades encontradas nos cálculos, com natureza do impacto, risco e situação da revisão. */
export default function TaxOpportunitiesPage() {
  const [status, setStatus] = useState<OpportunityStatus | "">("");
  const list = useQuery({ queryKey: TAX_KEYS.opportunities(status), queryFn: () => listTaxOpportunities(status, 200) });
  const byNature = list.data ? impactsByNature(list.data) : {};

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Oportunidades"
        description="Onde a operação pode pagar menos, pagar depois ou aproveitar crédito — cada uma com risco, fonte e o que é preciso cumprir. Nada é aplicado sem aprovação."
      />
      <TaxSubNav />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <Field id="op-status" label="Situação" className="sm:w-56">
          <Select value={status || ALL} onValueChange={(v) => setStatus(v === ALL ? "" : (v as OpportunityStatus))}>
            <SelectTrigger id="op-status"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Todas</SelectItem>
              {(Object.keys(OPPORTUNITY_STATUS) as OpportunityStatus[]).map((s) => <SelectItem key={s} value={s}>{OPPORTUNITY_STATUS[s].label}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        {Object.keys(byNature).length > 0 && (
          <div className="space-y-1">
            <p className="text-xs text-muted-foreground">Impacto estimado por natureza (não se somam):</p>
            <ul className="flex flex-wrap gap-2">
              {(Object.entries(byNature) as [ImpactNature, number][]).map(([n, v]) => (
                <li key={n} className="flex items-center gap-1.5 text-sm"><ImpactBadge nature={n} /><span className="tabular-nums font-medium">{formatCurrency(v)}</span></li>
              ))}
            </ul>
          </div>
        )}
      </div>

      <SectionCard noPadding>
        {list.isLoading && <div className="p-5"><Skeleton className="h-40 w-full" /></div>}
        {list.isError && <ErrorState title="Não foi possível listar as oportunidades" onRetry={() => void list.refetch()} compact />}
        {list.data?.length === 0 && (
          <EmptyState icon={Lightbulb} title="Nenhuma oportunidade" description="Elas aparecem quando um cálculo encontra uma alternativa permitida." compact />
        )}
        {list.data && list.data.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Oportunidade</TableHead><TableHead>Natureza</TableHead><TableHead className="text-right">Impacto estimado</TableHead>
                  <TableHead>Risco</TableHead><TableHead className="text-right">Confiança</TableHead><TableHead>Situação</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.map((o) => (
                  <TableRow key={o.id}>
                    <TableCell className="min-w-56">
                      <Link to={`/tributos/oportunidades/${o.id}`} className="font-medium hover:underline">{o.title}</Link>
                      <p className="text-xs text-muted-foreground">{labelOf(OPPORTUNITY_TYPES, o.type)} · {formatDate(o.createdAt)}</p>
                    </TableCell>
                    <TableCell><ImpactBadge nature={o.impactNature} /></TableCell>
                    <TableCell className="text-right tabular-nums">{o.estimatedImpact === null ? "—" : formatCurrency(o.estimatedImpact)}</TableCell>
                    <TableCell><RiskBadge risk={o.risk} /></TableCell>
                    <TableCell className="text-right tabular-nums">{o.confidence}%</TableCell>
                    <TableCell><OpportunityStatusBadge status={o.status} /></TableCell>
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
