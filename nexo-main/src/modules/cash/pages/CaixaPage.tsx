import { useState } from "react";
import { ArrowDownLeft, ArrowUpRight, Wallet, X } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "sonner";
import { cashService } from "../services/cashService";
import { CashStatusBanner } from "../components/CashStatusBanner";
import { CashKpiCards } from "../components/CashKpiCards";
import { CashMovementsTable } from "../components/CashMovementsTable";
import { CashOpenModal } from "../components/CashOpenModal";
import { CashMovementModal } from "../components/CashMovementModal";
import { CashCloseModal } from "../components/CashCloseModal";
import type { CashMovementInput } from "../types";

export default function CaixaPage() {
  const queryClient = useQueryClient();
  const [openModal, setOpenModal] = useState(false);
  const [movementModal, setMovementModal] = useState(false);
  const [movementDefaultType, setMovementDefaultType] = useState<CashMovementInput["type"]>("withdrawal");
  const [closeModal, setCloseModal] = useState(false);

  const { data: session, isLoading: sessionLoading } = useQuery({
    queryKey: ["cash-session"],
    queryFn: () => cashService.getCurrentSession(),
  });

  const { data: movements = [], isLoading: movementsLoading } = useQuery({
    queryKey: ["cash-movements", session?.id],
    queryFn: () => cashService.listMovements(),
    enabled: !!session,
  });

  const openMutation = useMutation({
    mutationFn: ({ amount, operator }: { amount: number; operator: string }) =>
      cashService.openSession({ openingAmount: amount, operator }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cash-session"] });
      queryClient.invalidateQueries({ queryKey: ["cash-movements"] });
      setOpenModal(false);
      toast.success("Caixa aberto com sucesso.");
    },
    onError: (err: Error) => {
      toast.error(err.message);
    },
  });

  const movementMutation = useMutation({
    mutationFn: (input: CashMovementInput) => cashService.addMovement(input),
    onSuccess: (_, input) => {
      queryClient.invalidateQueries({ queryKey: ["cash-session"] });
      queryClient.invalidateQueries({ queryKey: ["cash-movements"] });
      setMovementModal(false);
      const labels: Record<string, string> = {
        reinforcement: "Suprimento registrado.",
        withdrawal: "Sangria registrada.",
        adjustment: "Ajuste registrado.",
      };
      toast.success(labels[input.type] ?? "Movimentação registrada.");
    },
    onError: (err: Error) => {
      toast.error(err.message);
    },
  });

  const closeMutation = useMutation({
    mutationFn: ({ counted, notes }: { counted: number; notes: string }) =>
      cashService.closeSession({ countedAmount: counted, notes }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cash-session"] });
      queryClient.invalidateQueries({ queryKey: ["cash-movements"] });
      setCloseModal(false);
      toast.success("Caixa fechado com sucesso.");
    },
    onError: (err: Error) => {
      toast.error(err.message);
    },
  });

  const isOpen = session?.status === "open";

  function openMovementModal(type: CashMovementInput["type"]) {
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
                <Button variant="outline" onClick={() => openMovementModal("withdrawal")}>
                  <ArrowDownLeft className="h-4 w-4 mr-1.5" />
                  Sangria
                </Button>
                <Button variant="outline" onClick={() => openMovementModal("reinforcement")}>
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
        <CashStatusBanner session={session ?? null} />
      )}

      {isOpen && session && (
        <CashKpiCards
          session={session}
          movementsCount={movements.length}
        />
      )}

      {isOpen && (
        <SectionCard title="Movimentações do dia">
          {movementsLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : (
            <CashMovementsTable movements={movements} />
          )}
        </SectionCard>
      )}

      <CashOpenModal
        open={openModal}
        onOpenChange={setOpenModal}
        onConfirm={(amount, operator) => openMutation.mutate({ amount, operator })}
        isLoading={openMutation.isPending}
      />

      <CashMovementModal
        open={movementModal}
        onOpenChange={setMovementModal}
        defaultType={movementDefaultType}
        onConfirm={(input) => movementMutation.mutate(input)}
        isLoading={movementMutation.isPending}
      />

      {session && (
        <CashCloseModal
          open={closeModal}
          onOpenChange={setCloseModal}
          expectedBalance={session.expectedBalance}
          onConfirm={(counted, notes) => closeMutation.mutate({ counted, notes })}
          isLoading={closeMutation.isPending}
        />
      )}
    </div>
  );
}
