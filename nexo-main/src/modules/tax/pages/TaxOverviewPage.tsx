import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Calculator, Coins, Gavel, Lock, Percent, Scale, ShieldAlert, Split, UserCog } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { formatCurrency } from "@/lib/formatters";
import { TAX_KEYS, getTaxOverview } from "../api/tax.api";
import { formatIsoDate, formatPercent } from "../lib/forms";
import { overviewAttention, overviewImpacts } from "../lib/overview";
import { POTENTIAL_CREDIT_COPY } from "../lib/labels";
import { TaxNote, TaxSubNav } from "../components/TaxShell";
import { ImpactBadge } from "../components/TaxBadges";

/**
 * Inteligência tributária — visão geral: a carga estimada dos últimos 30 dias, créditos, oportunidades
 * (cada natureza de impacto separada, nunca somadas) e o estado das regras. Só dados reais.
 */
export default function TaxOverviewPage() {
  const overview = useQuery({ queryKey: TAX_KEYS.overview, queryFn: getTaxOverview });
  const o = overview.data;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Tributos"
        description="Quanto de CBS e IBS a operação gera, os créditos que ela dá e onde existe alternativa — com a fonte de cada número."
        actions={<Button asChild><Link to="/tributos/calculos">Calcular uma operação</Link></Button>}
      />
      <TaxSubNav />

      <TaxNote>
        Em 2026 a CBS (0,9%) e o IBS (0,1%) são alíquotas de teste: o valor é compensável com PIS/Cofins ou dispensado para quem cumpre as obrigações acessórias (LC 214/2025, art. 348).
      </TaxNote>

      {overview.isLoading && <Skeleton className="h-64 w-full" />}
      {overview.isError && <ErrorState title="Não foi possível carregar a visão tributária" onRetry={() => void overview.refetch()} />}

      {o && !o.profileConfigured && (
        <SectionCard>
          <EmptyState
            icon={UserCog}
            title="Cadastre o perfil tributário"
            description="Regime, UF, município e CNAE decidem quais regras valem. Sem o perfil, nenhum cálculo é feito."
            action={<Button asChild><Link to="/tributos/perfil">Cadastrar perfil tributário</Link></Button>}
          />
        </SectionCard>
      )}

      {o?.profileConfigured && (
        <>
          {overviewAttention(o).length > 0 && (
            <div className="space-y-1 rounded-md border border-warning/40 bg-warning/5 p-3 text-sm">
              <p className="flex items-center gap-2 font-medium text-warning"><AlertTriangle className="h-4 w-4" /> O que pede atenção</p>
              <ul className="list-disc pl-5">{overviewAttention(o).map((a) => <li key={a}>{a}</li>)}</ul>
            </div>
          )}

          <section className="space-y-3">
            <h2 className="section-heading">Carga estimada · {formatIsoDate(o.periodFrom)} a {formatIsoDate(o.periodTo)}</h2>
            {o.calculations === 0 ? (
              <SectionCard>
                <EmptyState
                  icon={Calculator}
                  title="Nenhum cálculo nos últimos 30 dias"
                  description="Calcule uma venda ou uma operação para ver a carga, a base e a taxa efetiva."
                  action={<Button asChild variant="outline"><Link to="/tributos/calculos">Calcular uma operação</Link></Button>}
                  compact
                />
              </SectionCard>
            ) : (
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <MetricBlock label="Carga estimada (30 dias)" value={formatCurrency(o.estimatedTax)} icon={Coins} caption={`${o.calculations} ${o.calculations === 1 ? "cálculo" : "cálculos"}`} />
                <MetricBlock label="Base tributada" value={formatCurrency(o.taxedBase)} icon={Scale} />
                <MetricBlock label="Taxa efetiva" value={formatPercent(o.effectiveRatePercent)} icon={Percent} caption="imposto ÷ base" />
              </div>
            )}
          </section>

          <section className="space-y-3">
            <h2 className="section-heading">Créditos</h2>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <MetricBlock label="Potencial estimado" value={formatCurrency(o.creditsPotential)} caption="estimativa — não é crédito validado" />
              <MetricBlock label="A apropriar" value={formatCurrency(o.creditsToBeAppropriated)} caption="requisitos confirmados" />
              <MetricBlock label="Disponíveis" value={formatCurrency(o.creditsAvailable)} variant={o.creditsAvailable > 0 ? "success" : "default"} />
              <MetricBlock label="Bloqueados" value={formatCurrency(o.creditsBlocked)} icon={Lock} variant={o.creditsBlocked > 0 ? "warning" : "default"} />
            </div>
            {o.creditsPotential > 0 && <p className="text-xs text-muted-foreground">{POTENTIAL_CREDIT_COPY}</p>}
          </section>

          <section className="space-y-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 className="section-heading">Oportunidades abertas: {o.openOpportunities}</h2>
              <Link to="/tributos/oportunidades" className="text-sm font-medium text-primary hover:underline">Ver oportunidades</Link>
            </div>
            <p className="text-xs text-muted-foreground">Cada natureza é mostrada à parte e não se soma: diferimento não é economia, e crédito depende de condições.</p>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {overviewImpacts(o).map((t) => (
                <div key={t.nature} className="rounded-md border border-border bg-card p-4">
                  <ImpactBadge nature={t.nature} />
                  <p className="mt-2 text-2xl font-semibold tabular-nums">{formatCurrency(t.amount)}</p>
                  <p className="mt-2 text-[11px] text-muted-foreground">{t.hint}</p>
                </div>
              ))}
            </div>
            <div className="grid gap-3 sm:grid-cols-3">
              <MetricBlock label="Risco alto em aberto" value={o.openHighRisk} icon={ShieldAlert} variant={o.openHighRisk > 0 ? "danger" : "default"} caption="precisam da diretoria" />
              <MetricBlock label="Bloqueadas" value={o.blockedOpportunities} icon={Gavel} caption="estruturas proibidas, sem economia" />
              <MetricBlock label="Divergências de split" value={o.splitDivergences} icon={Split} variant={o.splitDivergences > 0 ? "warning" : "default"} caption="retido ≠ esperado" />
            </div>
          </section>

          <section className="space-y-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 className="section-heading">Regras para hoje</h2>
              <Link to="/tributos/regras" className="text-sm font-medium text-primary hover:underline">Ver regras e fontes</Link>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <MetricBlock label="Regras em vigor hoje" value={o.activeRulesToday} variant="success" />
              <MetricBlock label="Pendentes de regulamentação" value={o.pendingRulesToday} variant={o.pendingRulesToday > 0 ? "warning" : "default"} caption="existem na lei, ainda não aplicadas" />
            </div>
          </section>
        </>
      )}
    </div>
  );
}
