import { useState } from "react";
import { ArrowDownLeft, ArrowUpRight, Wallet, X, FileDown, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "sonner";
import { deriveExpectedBalance } from "../types";
import {
  useOpenSession,
  useSessionById,
  useOpenCashSession,
  useCloseCashSession,
  useAddCashMovement,
} from "../hooks/use-cash";
import { CashStatusBanner } from "../components/CashStatusBanner";
import { CashKpiCards } from "../components/CashKpiCards";
import { CashMovementsTable } from "../components/CashMovementsTable";
import { CashOpenModal } from "../components/CashOpenModal";
import { CashMovementModal } from "../components/CashMovementModal";
import { CashCloseModal } from "../components/CashCloseModal";
import type { AddCashMovementRequest } from "../types";
import { downloadPdf } from "@/services/pdf.api";

export default function CaixaPage() {
  const [openModal, setOpenModal] = useState(false);
  const [movementModal, setMovementModal] = useState(false);
  const [movementDefaultType, setMovementDefaultType] =
    useState<AddCashMovementRequest["movementType"]>("Withdrawal");
  const [closeModal, setCloseModal] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  const handleDownloadReport = async () => {
    if (!openSession?.id) return;
    setPdfLoading(true);
    try {
      await downloadPdf(`/cash/sessions/${openSession.id}/close-report.pdf`, `fechamento.pdf`);
    } catch {
      toast.error("Falha ao gerar PDF. Tente novamente.");
    } finally {
      setPdfLoading(false);
    }
  };

  const { data: openSession, isLoading: sessionLoading } = useOpenSession();
  const { data: sessionDetail } = useSessionById(openSession?.id);

  const movements = sessionDetail?.movements ?? [];
  const expectedBalance = openSession
    ? deriveExpectedBalance(openSession.openingBalance, movements)
    : 0;

  const openMutation = useOpenCashSession();
  const closeMutation = useCloseCashSession();
  const movementMutation = useAddCashMovement();

  const isOpen = openSession?.status === "Open";

  function openMovementModal(type: AddCashMovementRequest["movementType"]) {
    setMovementDefaultType(type);
    setMovementModal(true);
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="Caixa"
        description="Controle de abertura, fechamento e movimentações do caixa."
        actions={
          <div className="flex items-center gap-2">
            {!isOpen && !sessionLoading && (
              <Button onClick={() => setOpenModal(true)}>
                <Wallet className="h-4 w-4 mr-1.5" />
                Abrir caixa
              </Button>
            )}
            {isOpen && (
              <>
                <Button
                  variant="outline"
                  onClick={handleDownloadReport}
                  disabled={pdfLoading}
                >
                  {pdfLoading ? (
                    <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />
                  ) : (
                    <FileDown className="h-4 w-4 mr-1.5" />
                  )}
                  Baixar fechamento
                </Button>
                <Button variant="outline" onClick={() => openMovementModal("Withdrawal")}>
                  <ArrowDownLeft className="h-4 w-4 mr-1.5" />
                  Sangria
                </Button>
                <Button variant="outline" onClick={() => openMovementModal("Deposit")}>
                  <ArrowUpRight className="h-4 w-4 mr-1.5" />
                  Suprimento
                </Button>
                <Button variant="destructive" onClick={() => setCloseModal(true)}>
                  <X className="h-4 w-4 mr-1.5" />
                  Fechar caixa
                </Button>
              </>
            )}
          </div>
        }
      />

      {sessionLoading ? (
        <Skeleton className="h-16 w-full rounded-lg" />
      ) : (
        <CashStatusBanner session={openSession ?? null} expectedBalance={expectedBalance} />
      )}

      {isOpen && openSession && (
        <CashKpiCards
          openingBalance={openSession.openingBalance}
          expectedBalance={expectedBalance}
          movementsCount={movements.length}
        />
      )}

      {isOpen && (
        <SectionCard title="Movimentações do dia">
          <CashMovementsTable movements={movements} />
        </SectionCard>
      )}

      <CashOpenModal
        open={openModal}
        onOpenChange={setOpenModal}
        onConfirm={(openingBalance, notes) =>
          openMutation.mutate(
            { openingBalance, notes },
            {
              onSuccess: () => {
                setOpenModal(false);
                toast.success("Caixa aberto com sucesso.");
              },
              onError: (err: Error) => toast.error(err.message),
            }
          )
        }
        isLoading={openMutation.isPending}
      />

      <CashMovementModal
        open={movementModal}
        onOpenChange={setMovementModal}
        defaultType={movementDefaultType}
        onConfirm={(req) =>
          openSession &&
          movementMutation.mutate(
            { id: openSession.id, req },
            {
              onSuccess: () => {
                setMovementModal(false);
                const labels: Record<string, string> = {
                  Deposit:    "Suprimento registrado.",
                  Withdrawal: "Sangria registrada.",
                };
                toast.success(labels[req.movementType] ?? "Movimentação registrada.");
              },
              onError: (err: Error) => toast.error(err.message),
            }
          )
        }
        isLoading={movementMutation.isPending}
      />

      {openSession && (
        <CashCloseModal
          open={closeModal}
          onOpenChange={setCloseModal}
          expectedBalance={expectedBalance}
          onConfirm={(closingBalance, _notes) =>
            closeMutation.mutate(
              { id: openSession.id, req: { closingBalance } },
              {
                onSuccess: () => {
                  setCloseModal(false);
                  toast.success("Caixa fechado com sucesso.");
                },
                onError: (err: Error) => toast.error(err.message),
              }
            )
          }
          isLoading={closeMutation.isPending}
        />
      )}
    </div>
  );
}
