import { useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, AlertCircle, Store, User, XCircle, FileDown, Loader2 } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/shared/EmptyState";
import { getSale, cancelSale } from "../api/sales.api";
import { downloadPdf } from "@/services/pdf.api";
import { saleToLegacy } from "../utils/saleAdapter";
import { SaleSummaryCard } from "../components/SaleSummaryCard";
import { SalePaymentSummaryCard } from "../components/SalePaymentSummaryCard";
import { SaleItemsTable } from "../components/SaleItemsTable";
import { SaleCancellationDialog } from "../components/SaleCancellationDialog";
import type { CancellationConfirmPayload } from "../components/SaleCancellationDialog";
import { toast } from "sonner";

export default function VendaDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
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

  const { data: sale, isLoading, isError } = useQuery({
    queryKey: ["sale", id],
    queryFn: () => getSale(id!).then(saleToLegacy),
    enabled: !!id,
  });

  const cancelSaleMutation = useMutation({
    mutationFn: (_payload: CancellationConfirmPayload) => cancelSale(id!),
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
    },
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

  if (isError || !sale) {
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

  const canCancelSale = sale.status === "completed" || sale.status === "partially_cancelled";

  const actions = (
    <div className="flex items-center gap-2">
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

      {/* Cancellation history */}
      {(sale.cancellationRecords?.length ?? 0) > 0 && (
        <SectionCard
          title="Histórico de cancelamentos"
          description="Registros de cancelamentos aplicados a esta venda."
        >
          <div className="space-y-3">
            {sale.cancellationRecords!.map((record) => (
              <div
                key={record.id}
                className="flex items-start justify-between text-sm border rounded-md p-3 bg-muted/20"
              >
                <div className="space-y-0.5">
                  <p className="font-medium text-foreground">
                    {record.type === "full" ? "Cancelamento total" : `Item cancelado: ${record.itemDescription}`}
                  </p>
                  <p className="text-muted-foreground text-xs">
                    Motivo: {record.reason}
                  </p>
                  <p className="text-muted-foreground text-xs">
                    Cancelado por: {record.cancelledBy} · Autorizado por: {record.authorizedBy}
                  </p>
                </div>
                <div className="text-right shrink-0 ml-4">
                  <p className="text-xs text-muted-foreground">
                    {new Date(record.cancelledAt).toLocaleString("pt-BR", {
                      day: "2-digit",
                      month: "2-digit",
                      year: "numeric",
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </p>
                </div>
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
