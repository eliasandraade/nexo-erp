import { Link } from "react-router-dom";
import { useDashboardSummary } from "../hooks/useDashboardSummary";

/** Only facts supplied by the existing summary endpoint; no inferred financial health. */
export function OperationPriorities() {
  const { data, isLoading } = useDashboardSummary();
  if (isLoading || !data) return null;
  const issues = [
    ...(data.totalSales >= 5 && data.cancelledCount / data.totalSales > 0.1 ? [{ title: "Cancelamentos acima de 10%", detail: `${data.cancelledCount} vendas canceladas em ${data.totalSales} vendas no período.`, to: "/vendas", action: "Revisar vendas" }] : []),
    ...(!data.hasOpenCashSession ? [{ title: "Caixa fechado", detail: "Abra uma sessão antes de finalizar vendas no PDV.", to: "/caixa", action: "Abrir caixa" }] : []),
    ...(data.zeroStockCount > 0 ? [{ title: `${data.zeroStockCount} produtos sem estoque`, detail: "Confira a disponibilidade antes de vender.", to: "/estoque", action: "Ver estoque" }] : []),
    ...(data.lowStockCount > 0 ? [{ title: `${data.lowStockCount} produtos abaixo do mínimo`, detail: "Revise a necessidade de reposição.", to: "/estoque", action: "Revisar estoque" }] : []),
  ];
  return <section aria-label="Atenção na operação">
    {issues.length ? <>
      <h2 className="mb-3 text-base font-semibold">Antes de continuar</h2>
      <ul className="record-list">{issues.map(issue => <li key={issue.title} className="record-row flex-wrap">
        <div><p className="font-semibold text-warning">{issue.title}</p><p className="mt-1 text-sm text-muted-foreground">{issue.detail}</p></div>
        <Link to={issue.to} className="inline-flex min-h-10 items-center text-sm font-medium text-primary hover:underline">{issue.action} →</Link>
      </li>)}</ul>
    </> : <p className="text-sm text-muted-foreground">Caixa aberto. Nenhum alerta de estoque informado.</p>}
  </section>;
}
