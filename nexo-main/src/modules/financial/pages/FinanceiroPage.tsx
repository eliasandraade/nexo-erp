import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowDownLeft, ArrowUpRight, Landmark, Scale } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency, formatDate } from "@/lib/formatters";
import { CATEGORY_LABELS, getLedger, LEDGER_KEY, type LedgerEntryDto, type LedgerRegime } from "../api/financial.api";

const STATUS_LABEL: Record<LedgerEntryDto["status"], string> = {
  Pending: "Em aberto",
  Overdue: "Vencido",
  Paid: "Quitado",
  Cancelled: "Cancelado",
};

const ORIGIN_LABEL: Record<string, string> = {
  Sale: "Venda",
  SaleCancellation: "Cancelamento de venda",
  SaleReturn: "Devolução",
  Purchase: "Compra",
  PurchaseCancellation: "Cancelamento de compra",
  SvcPayment: "Service",
  SvcPaymentVoid: "Service — estorno",
  SvcCommissionPayout: "Service — comissão",
};

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

/** The chosen month in the operator's local time: [first day 00:00, first day of next month). */
function monthRange(month: string) {
  const [y, m] = month.split("-").map(Number);
  return { from: new Date(y, m - 1, 1), to: new Date(y, m, 1) };
}

/**
 * O livro financeiro: todo dinheiro dos módulos num lugar só, em dois regimes.
 * Competência mostra o que aconteceu no mês (inclusive o que ainda vai entrar ou sair);
 * caixa mostra o que de fato entrou e saiu.
 */
export default function FinanceiroPage() {
  const [regime, setRegime] = useState<LedgerRegime>("accrual");
  const [month, setMonth] = useState(currentMonth);
  const range = useMemo(() => monthRange(month || currentMonth()), [month]);

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: [...LEDGER_KEY, regime, month],
    queryFn: () => getLedger(regime, range.from, range.to),
  });

  const dateOf = (e: LedgerEntryDto) => (regime === "cash" ? e.paidAt ?? e.competenceDate : e.competenceDate);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Financeiro"
        description="Todo o dinheiro da empresa num livro só: vendas, devoluções, compras e Service. Competência mostra o que aconteceu no mês; caixa, o que de fato entrou e saiu."
      />

      <div className="flex flex-wrap items-end gap-4">
        <Tabs value={regime} onValueChange={(v) => setRegime(v as LedgerRegime)}>
          <TabsList>
            <TabsTrigger value="accrual">Competência</TabsTrigger>
            <TabsTrigger value="cash">Caixa</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="space-y-1.5">
          <Label htmlFor="fin-month">Mês</Label>
          <Input id="fin-month" type="month" value={month} onChange={(e) => setMonth(e.target.value)} className="w-44" />
        </div>
      </div>

      {isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : isError || !data ? (
        <ErrorState title="Não foi possível carregar o financeiro" onRetry={() => void refetch()} />
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <MetricBlock label="Entradas" value={formatCurrency(data.totalIn)} icon={ArrowDownLeft} />
            <MetricBlock label="Saídas" value={formatCurrency(data.totalOut)} icon={ArrowUpRight} />
            <MetricBlock
              label="Resultado"
              value={formatCurrency(data.result)}
              icon={Scale}
              variant={data.result < 0 ? "danger" : "default"}
              caption={regime === "accrual" ? "Inclui o que está em aberto; compras de mercadoria vão para o estoque, não para o resultado" : "Só o que foi quitado"}
            />
          </div>

          {data.byCategory.length === 0 ? (
            <SectionCard>
              <EmptyState icon={Landmark} title="Nenhum lançamento no mês" description="Vendas, compras e pagamentos aparecem aqui assim que acontecem." />
            </SectionCard>
          ) : (
            <>
              <SectionCard title="Por categoria">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Categoria</TableHead>
                      <TableHead className="text-right">Entradas</TableHead>
                      <TableHead className="text-right">Saídas</TableHead>
                      <TableHead className="text-right">Líquido</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {data.byCategory.map((c) => (
                      <TableRow key={c.category}>
                        <TableCell>{CATEGORY_LABELS[c.category] ?? c.category}</TableCell>
                        <TableCell className="text-right tabular-nums">{c.in ? formatCurrency(c.in) : "—"}</TableCell>
                        <TableCell className="text-right tabular-nums">{c.out ? formatCurrency(c.out) : "—"}</TableCell>
                        <TableCell className="text-right font-medium tabular-nums">{formatCurrency(c.net)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </SectionCard>

              <SectionCard title="Lançamentos">
                {data.truncated && (
                  <p className="mb-3 text-sm text-muted-foreground">
                    Mostrando os {data.entries.length} lançamentos mais recentes. Os totais acima cobrem o mês inteiro.
                  </p>
                )}
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Data</TableHead>
                      <TableHead>Descrição</TableHead>
                      <TableHead>Categoria</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead className="text-right">Valor</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {data.entries.map((e) => (
                      <TableRow key={e.id}>
                        <TableCell className="whitespace-nowrap text-sm">{formatDate(dateOf(e))}</TableCell>
                        <TableCell>
                          <div className="font-medium">{e.description}</div>
                          {e.referenceType && (
                            <div className="text-xs text-muted-foreground">{ORIGIN_LABEL[e.referenceType] ?? e.referenceType}</div>
                          )}
                        </TableCell>
                        <TableCell className="text-sm">{CATEGORY_LABELS[e.category] ?? e.category}</TableCell>
                        <TableCell>
                          <Badge variant={e.status === "Paid" ? "secondary" : "outline"}>{STATUS_LABEL[e.status] ?? e.status}</Badge>
                        </TableCell>
                        <TableCell className={`text-right font-medium tabular-nums ${e.direction === "Out" ? "text-destructive" : ""}`}>
                          {e.direction === "Out" ? "−" : "+"} {formatCurrency(e.amount)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </SectionCard>
            </>
          )}
        </>
      )}
    </div>
  );
}
