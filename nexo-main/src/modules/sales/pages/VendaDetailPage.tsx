import { useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, AlertCircle, Store, User, XCircle, FileDown, Loader2, Undo2 } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/shared/EmptyState";
import { getSale, cancelSale, createSaleReturn, listSaleReturns } from "../api/sales.api";
import type { CreateSaleReturnRequest } from "../api/sales.api";
import { SaleReturnDialog } from "../components/SaleReturnDialog";
import { formatCurrency } from "@/lib/formatters";
import { downloadPdf } from "@/services/pdf.api";
import { saleToLegacy } from "../utils/saleAdapter";
import { SaleSummaryCard } from "../components/SaleSummaryCard";
import { SalePaymentSummaryCard } from "../components/SalePaymentSummaryCard";
import { SaleItemsTable } from "../components/SaleItemsTable";
import { SaleCancellationDialog } from "../components/SaleCancellationDialog";
import type { CancellationConfirmPayload } from "../components/SaleCancellationDialog";
import { toast } from "sonner";

const REFUND_LABEL: Record<string, string> = {
  Cash: "Dinheiro", Pix: "Pix", Debit: "Cartão de débito", Credit: "Cartão de crédito", Transfer: "Transferência",
};

export default function VendaDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [returnDialogOpen, setReturnDialogOpen] = useState(false);
  const [returnError, setReturnError] = useState<string | null>(null);
  const [pdfLoading, setPdfLoading] = useState(false);

  const handleDownloadReceipt = async () => {
    if (!id) return;
    setPdfLoading(true);
    try {
      await downloadPdf(`/sales/${id}/receipt.pdf`, `recibo.pdf`);
    } catch {
      toast.error("Falha ao gerar PDF. Tente novamente.");
    } finally {
      setPdfLoading(false);
    }
  };

  const { data: dto, isLoading, isError } = useQuery({
    queryKey: ["sale", id],
    queryFn: () => getSale(id!),
    enabled: !!id,
  });
  const sale = dto ? saleToLegacy(dto) : undefined;
  const returnable = dto?.status === "Paid" || dto?.status === "PartiallyReturned";
  const { data: returns = [] } = useQuery({
    queryKey: ["sale", id, "returns"],
    queryFn: () => listSaleReturns(id!),
    enabled: !!id && !!dto && ["Paid", "PartiallyReturned", "Returned"].includes(dto.status),
  });

  const returnMutation = useMutation({
    mutationFn: (req: CreateSaleReturnRequest) => createSaleReturn(id!, req),
    onSuccess: (r) => {
      queryClient.invalidateQueries({ queryKey: ["sale", id] });
      queryClient.invalidateQueries({ queryKey: ["sales"] });
      queryClient.invalidateQueries({ queryKey: ["cash"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-operational"] });
      setReturnDialogOpen(false);
      setReturnError(null);
      toast.success(`Devolução registrada: ${formatCurrency(r.refundAmount)} reembolsados.`);
    },
    onError: (err: Error) => setReturnError(err.message),
  });

  const cancelSaleMutation = useMutation({
    mutationFn: (payload: CancellationConfirmPayload) => cancelSale(id!, payload.reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["sale", id] });
      queryClient.invalidateQueries({ queryKey: ["sales"] });
      // Commissions derive from sales — cancellation reverses them immediately.
      queryClient.invalidateQueries({ queryKey: ["commissions-overall"] });
      queryClient.invalidateQueries({ queryKey: ["commissions-by-seller"] });
      queryClient.invalidateQueries({ queryKey: ["commission-records"] });
      // Dashboard + insights may reflect commission/sales changes.
      queryClient.invalidateQueries({ queryKey: ["dashboard-operational"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-seller-ranking"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-insights"] });
      queryClient.invalidateQueries({ queryKey: ["insights"] });
      queryClient.invalidateQueries({ queryKey: ["insights-stats"] });
      setCancelDialogOpen(false);
      toast.success("Venda cancelada.");
    },
    onError: (err: Error) => toast.error(err.message),
  });

  const backButton = (
    <Button variant="outline" size="sm" onClick={() => navigate("/vendas")}>
      <ArrowLeft className="h-4 w-4 mr-1.5" />
      Voltar
    </Button>
  );

  if (isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Detalhes da venda" actions={backButton} />
        <div className="grid lg:grid-cols-2 gap-6">
          <Skeleton className="h-56 w-full rounded-lg" />
          <Skeleton className="h-56 w-full rounded-lg" />
        </div>
        <Skeleton className="h-48 w-full rounded-lg" />
      </div>
    );
  }

  if (isError || !sale || !dto) {
    return (
      <div className="space-y-6">
        <PageHeader title="Detalhes da venda" actions={backButton} />
        <SectionCard>
          <EmptyState
            icon={AlertCircle}
            title="Venda não encontrada"
            description={`Não foi possível localizar a venda "${id}".`}
            action={
              <Button onClick={() => navigate("/vendas")}>Voltar para Vendas</Button>
            }
          />
        </SectionCard>
      </div>
    );
  }

  // A sale with a prazo still open is cancelled; a paid one is undone by a devolução.
  const canCancelSale = dto.status === "Confirmed";

  const actions = (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        variant="outline"
        size="sm"
        onClick={handleDownloadReceipt}
        disabled={pdfLoading}
      >
        {pdfLoading ? (
          <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />
        ) : (
          <FileDown className="h-4 w-4 mr-1.5" />
        )}
        Baixar recibo
      </Button>
      {returnable && (
        <Button variant="outline" size="sm" onClick={() => { setReturnError(null); setReturnDialogOpen(true); }}>
          <Undo2 className="h-4 w-4 mr-1.5" />
          Registrar devolução
        </Button>
      )}
      {canCancelSale && (
        <Button
          variant="destructive"
          size="sm"
          onClick={() => setCancelDialogOpen(true)}
        >
          <XCircle className="h-4 w-4 mr-1.5" />
          Cancelar venda
        </Button>
      )}
      {backButton}
    </div>
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title={`Venda ${sale.id}`}
        description="Consulte os dados completos da venda registrada."
        actions={actions}
      />

      {/* Summary + Payment — side by side on large screens */}
      <div className="grid lg:grid-cols-2 gap-6">
        <SaleSummaryCard sale={sale} />
        <SalePaymentSummaryCard sale={sale} />
      </div>

      {/* Items sold */}
      <SectionCard title="Itens vendidos">
        <SaleItemsTable
          items={sale.items}
          sale={sale}
        />
      </SectionCard>

      {dto.cancellationReason && (
        <SectionCard title="Cancelamento">
          <p className="text-sm">Motivo: {dto.cancellationReason}</p>
        </SectionCard>
      )}

      {returns.length > 0 && (
        <SectionCard
          title="Devoluções"
          description={`Total reembolsado: ${formatCurrency(dto.returnedAmount ?? 0)} de ${formatCurrency(dto.total)}.`}
        >
          <div className="space-y-3">
            {returns.map((r) => (
              <div key={r.id} className="flex items-start justify-between gap-4 rounded-md border bg-muted/20 p-3 text-sm">
                <div className="min-w-0 space-y-0.5">
                  <p className="font-medium">
                    {formatCurrency(r.refundAmount)} · {REFUND_LABEL[r.refundMethod] ?? r.refundMethod}
                  </p>
                  <p className="text-xs text-muted-foreground">Motivo: {r.reason}</p>
                  <p className="text-xs text-muted-foreground">
                    {r.items.map((i) => `${i.quantity.toLocaleString("pt-BR")} × ${dto.items.find((s) => s.id === i.saleItemId)?.productName ?? "item"}`).join(" · ")}
                  </p>
                </div>
                <p className="shrink-0 text-xs text-muted-foreground">
                  {new Date(r.createdAt).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}
                </p>
              </div>
            ))}
          </div>
        </SectionCard>
      )}

      {/* Operational context */}
      <SectionCard
        title="Contexto operacional"
        description="Informações adicionais sobre o contexto desta venda."
      >
        <div className="grid sm:grid-cols-2 gap-4 text-sm">
          <div className="flex items-start gap-2 text-muted-foreground">
            <User className="h-4 w-4 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs font-medium text-foreground mb-0.5">Cliente</p>
              <p>{sale.customerName ?? "Não informado"}</p>
            </div>
          </div>
          <div className="flex items-start gap-2 text-muted-foreground">
            <Store className="h-4 w-4 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs font-medium text-foreground mb-0.5">Loja / PDV</p>
              <p>PDV — Ponto de Venda</p>
            </div>
          </div>
        </div>
      </SectionCard>

      <SaleReturnDialog
        open={returnDialogOpen}
        sale={dto}
        previousReturns={returns}
        onClose={() => setReturnDialogOpen(false)}
        onConfirm={(req) => returnMutation.mutate(req)}
        isLoading={returnMutation.isPending}
        error={returnError}
      />

      {/* Full-sale cancellation dialog */}
      <SaleCancellationDialog
        open={cancelDialogOpen}
        onClose={() => setCancelDialogOpen(false)}
        onConfirm={(payload) => cancelSaleMutation.mutate(payload)}
        mode="full"
        isLoading={cancelSaleMutation.isPending}
      />
    </div>
  );
}
