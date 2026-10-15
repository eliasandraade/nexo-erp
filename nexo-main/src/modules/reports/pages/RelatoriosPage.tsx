import { lazy, Suspense, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { Ban, Download, Package, Receipt, Tag, TrendingUp, Undo2, Users, Wallet } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { ErrorState } from "@/components/shared/ErrorState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import {
  REPORTS_KEY, exportSales, getCustomerReport, getInventoryReport, getSalesReport, type SalesReportDto,
} from "../api/reports.api";
import { OtherReports } from "../components/OtherReports";
import { fmtDay, fmtPeriod, methodLabel, periodProblem, presetPeriod, type Period, type PeriodPreset } from "../lib/period";

const SalesByDayChart = lazy(() => import("../components/SalesByDayChart"));

const PRESETS: { value: PeriodPreset; label: string }[] = [
  { value: "today", label: "Hoje" },
  { value: "7d", label: "7 dias" },
  { value: "month", label: "Este mês" },
  { value: "lastMonth", label: "Mês passado" },
  { value: "custom", label: "Escolher datas" },
];

const plural = (n: number, one: string, many: string) => (n === 1 ? one : many);
const vendas = (n: number) => `${n} ${plural(n, "venda", "vendas")}`;

/** Skeleton while loading, a retry on failure, the content once there. */
function QueryBlock<T>({ query, error, height, children }: Readonly<{
  query: UseQueryResult<T>; error: string; height: string; children: (data: T) => ReactNode;
}>) {
  if (query.isLoading) return <Skeleton className={`${height} w-full`} />;
  if (query.isError || query.data === undefined) return <ErrorState title={error} onRetry={() => void query.refetch()} />;
  return <>{children(query.data)}</>;
}

/**
 * Relatórios do núcleo: vendas de um período (dias do Brasil, pela data em que a venda foi
 * finalizada, já sem as devoluções), o estoque de agora e os clientes. Exporta as vendas em planilha.
 */
export default function RelatoriosPage() {
  const [preset, setPreset] = useState<PeriodPreset>("month");
  const [draft, setDraft] = useState<Period>(() => presetPeriod("month"));
  const [custom, setCustom] = useState<Period>(() => presetPeriod("month"));
  const [exporting, setExporting] = useState(false);

  const period = preset === "custom" ? custom : presetPeriod(preset);
  const draftProblem = periodProblem(draft);

  const sales = useQuery({
    queryKey: [...REPORTS_KEY, "sales", period.from, period.to],
    queryFn: () => getSalesReport(period.from, period.to),
  });
  const inventory = useQuery({ queryKey: [...REPORTS_KEY, "inventory"], queryFn: getInventoryReport });
  const customers = useQuery({ queryKey: [...REPORTS_KEY, "customers"], queryFn: getCustomerReport });

  async function onExport() {
    setExporting(true);
    try {
      await exportSales(period.from, period.to);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Falha ao exportar as vendas");
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="Relatórios"
        description="Quanto vendeu, o que mais saiu, quem vendeu e como recebeu. Valores já sem as devoluções, abatidas no dia da venda."
        actions={
          <Button variant="outline" onClick={() => void onExport()} disabled={exporting}>
            <Download className="mr-1 h-4 w-4" /> {exporting ? "Exportando…" : "Exportar planilha"}
          </Button>
        }
      />

      <div className="space-y-3">
        <ToggleGroup
          type="single"
          variant="outline"
          aria-label="Período do relatório"
          className="flex-wrap justify-start"
          value={preset}
          onValueChange={(v) => { if (v) setPreset(v as PeriodPreset); }}
        >
          {PRESETS.map((p) => <ToggleGroupItem key={p.value} value={p.value}>{p.label}</ToggleGroupItem>)}
        </ToggleGroup>
        {preset === "custom" && (
          <form
            className="flex flex-wrap items-end gap-3"
            onSubmit={(e) => { e.preventDefault(); if (!draftProblem) setCustom(draft); }}
          >
            <div className="space-y-1">
              <Label htmlFor="rel-from">De</Label>
              <Input id="rel-from" type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="rel-to">Até</Label>
              <Input id="rel-to" type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
            </div>
            <Button type="submit" disabled={!!draftProblem}>Ver período</Button>
            {draftProblem && <p className="w-full text-sm text-destructive">{draftProblem}</p>}
          </form>
        )}
        <p className="text-sm text-muted-foreground">Período: {fmtPeriod(period)}</p>
      </div>

      <QueryBlock query={sales} height="h-72" error="Não foi possível montar o relatório de vendas">
        {(report) => <SalesSection report={report} />}
      </QueryBlock>

      <SectionCard
        title="Estoque agora"
        description="Fotografia de hoje, não do período. Valor pelo custo do que está na loja."
        actions={<Link to="/estoque" className="text-sm font-medium text-primary hover:underline">Ver estoque →</Link>}
      >
        <QueryBlock query={inventory} height="h-24" error="Não foi possível ler o estoque">
          {(inv) => (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <MetricBlock label="Valor em estoque" value={formatCurrency(inv.totalStockValue)} icon={Package} caption="a preço de custo" />
              <MetricBlock label="Produtos ativos" value={inv.totalProducts} />
              <MetricBlock label="Sem estoque" value={inv.zeroStockCount} variant={inv.zeroStockCount > 0 ? "danger" : "default"} caption="nada disponível para vender" />
              <MetricBlock label="Abaixo do mínimo" value={inv.lowStockCount} variant={inv.lowStockCount > 0 ? "warning" : "default"} />
            </div>
          )}
        </QueryBlock>
      </SectionCard>

      <SectionCard title="Clientes" description="O cadastro é da empresa toda; as compras contadas são desta loja.">
        <QueryBlock query={customers} height="h-24" error="Não foi possível ler os clientes">
          {(cus) => (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <MetricBlock label="Clientes ativos" value={cus.totalCustomers} icon={Users} />
              <MetricBlock label="Novos neste mês" value={cus.newThisMonth} />
              <MetricBlock label="Já compraram" value={cus.withPurchases} caption="com a venda identificada" />
              <MetricBlock label="Compra média" value={formatCurrency(cus.averagePurchaseValue)} caption="das vendas com cliente" />
            </div>
          )}
        </QueryBlock>
      </SectionCard>

      <OtherReports />
    </div>
  );
}

function SalesSection({ report }: Readonly<{ report: SalesReportDto }>) {
  const paidTotal = report.payments.reduce((t, p) => t + p.amount, 0);
  const sold = report.totalSales > 0 || report.returnedSales > 0;
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricBlock label="Faturamento" value={formatCurrency(report.totalRevenue)} icon={TrendingUp}
          caption={report.returnedValue > 0 ? `${formatCurrency(report.grossRevenue)} vendidos − ${formatCurrency(report.returnedValue)} devolvidos` : "nenhuma devolução"} />
        <MetricBlock label="Vendas" value={report.totalSales} icon={Receipt}
          caption={report.returnedSales > 0 ? `mais ${report.returnedSales} ${plural(report.returnedSales, "devolvida", "devolvidas")} por inteiro` : "finalizadas no período"} />
        <MetricBlock label="Ticket médio" value={formatCurrency(report.averageTicket)} icon={Wallet} caption="por venda" />
        <MetricBlock label="Descontos dados" value={formatCurrency(report.discountValue)} icon={Tag} />
      </div>
      {(report.cancelledSales > 0 || report.returnedValue > 0) && (
        <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted-foreground">
          {report.cancelledSales > 0 && (
            <span className="inline-flex items-center gap-1"><Ban className="h-4 w-4" /> {vendas(report.cancelledSales)} {plural(report.cancelledSales, "cancelada", "canceladas")} ({formatCurrency(report.cancelledValue)}), fora do faturamento</span>
          )}
          {report.returnedValue > 0 && (
            <span className="inline-flex items-center gap-1"><Undo2 className="h-4 w-4" /> {formatCurrency(report.returnedValue)} em devoluções</span>
          )}
        </div>
      )}

      <SectionCard title="Faturamento por dia">
        {sold ? (
          <>
            <Suspense fallback={<Skeleton className="h-[260px] w-full" />}>
              <SalesByDayChart days={report.days} />
            </Suspense>
            <details className="mt-3 text-sm">
              <summary className="cursor-pointer text-muted-foreground">Ver dia a dia</summary>
              <Table>
                <TableHeader><TableRow><TableHead>Dia</TableHead><TableHead className="text-right">Vendas</TableHead><TableHead className="text-right">Faturamento</TableHead></TableRow></TableHeader>
                <TableBody>
                  {report.days.filter((d) => d.sales > 0 || d.revenue !== 0).map((d) => (
                    <TableRow key={d.date}>
                      <TableCell>{fmtDay(d.date)}</TableCell>
                      <TableCell className="text-right tabular-nums">{d.sales}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatCurrency(d.revenue)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </details>
          </>
        ) : (
          <p className="py-10 text-center text-sm text-muted-foreground">Nenhuma venda finalizada neste período.</p>
        )}
      </SectionCard>

      <div className="grid gap-6 lg:grid-cols-2">
        <SectionCard title="Produtos mais vendidos" description="Pelo faturamento, já sem as devoluções e com o desconto da venda repartido.">
          {report.topProducts.length === 0 ? <p className="text-sm text-muted-foreground">Sem produtos vendidos no período.</p> : (
            <Table>
              <TableHeader><TableRow><TableHead>Produto</TableHead><TableHead className="text-right">Qtd.</TableHead><TableHead className="text-right">Faturamento</TableHead></TableRow></TableHeader>
              <TableBody>
                {report.topProducts.map((p) => (
                  <TableRow key={p.productId}>
                    <TableCell className="max-w-[16rem] truncate">{p.productName}</TableCell>
                    <TableCell className="text-right tabular-nums">{p.quantity.toLocaleString("pt-BR")}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(p.revenue)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SectionCard>

        <SectionCard title="Vendedores">
          {report.sellers.length === 0 ? <p className="text-sm text-muted-foreground">Sem vendas no período.</p> : (
            <Table>
              <TableHeader><TableRow><TableHead>Vendedor</TableHead><TableHead className="text-right">Vendas</TableHead><TableHead className="text-right">Faturamento</TableHead></TableRow></TableHeader>
              <TableBody>
                {report.sellers.map((s) => (
                  <TableRow key={s.sellerName}>
                    <TableCell className="max-w-[16rem] truncate">{s.sellerName}</TableCell>
                    <TableCell className="text-right tabular-nums">{s.sales}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(s.revenue)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SectionCard>
      </div>

      <SectionCard title="Formas de pagamento" description="Como as vendas do período foram pagas, antes das devoluções.">
        {report.payments.length === 0 ? <p className="text-sm text-muted-foreground">Sem pagamentos no período.</p> : (
          <ul className="space-y-3">
            {report.payments.map((p) => {
              const share = paidTotal > 0 ? Math.round((p.amount / paidTotal) * 100) : 0;
              return (
                <li key={p.method} className="space-y-1">
                  <div className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                    <span className="font-medium">{methodLabel(p.method)}</span>
                    <span className="tabular-nums text-muted-foreground">{vendas(p.sales)} · <span className="font-medium text-foreground">{formatCurrency(p.amount)}</span> · {share}%</span>
                  </div>
                  <div className="h-2 rounded-full bg-muted" aria-hidden="true">
                    <div className="h-2 rounded-full bg-primary" style={{ width: `${share}%` }} />
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </SectionCard>
    </>
  );
}
