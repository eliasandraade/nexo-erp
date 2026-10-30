import { useEffect, useState } from "react";
import { FilePlus2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useConfirmSelection } from "../../hooks/use-build-commercial";
import type { BuildSelectionDto } from "../../api/build-commercial.api";
import { fmtSignedBRL } from "../../lib/commercial";
import { errorMessage } from "../commercial/errors";

interface Props {
  projectId: string;
  selection: BuildSelectionDto | null;
  onClose: () => void;
  onOpenChangeOrders: () => void;
}

/**
 * Confirms the client's choice (or records one for an open selection). An option that changes the price or
 * adds lead time creates a DRAFT aditivo, which the company reviews and sends to the client.
 */
export function SelectionConfirmDialog({ projectId, selection, onClose, onOpenChangeOrders }: Props) {
  const mut = useConfirmSelection(projectId);
  const [optionId, setOptionId] = useState("");
  const [createdDraft, setCreatedDraft] = useState(false);

  useEffect(() => {
    if (!selection) return;
    setOptionId(selection.chosenOptionId ?? "");
    setCreatedDraft(false);
  }, [selection]);

  const option = selection?.options.find((o) => o.id === optionId);
  const createsDraft = !!option && (option.priceDelta !== 0 || (option.leadTimeDays ?? 0) > 0);

  const confirm = () => {
    if (!selection || !optionId) return;
    mut.mutate({ id: selection.id, optionId: selection.status === "Open" ? optionId : undefined }, {
      onSuccess: (dto) => {
        if (dto.changeOrderId) {
          setCreatedDraft(true);
          toast.success("Escolha confirmada. Um aditivo em rascunho foi criado.");
        } else {
          toast.success("Escolha confirmada.");
          onClose();
        }
      },
      onError: (e) => toast.error(errorMessage(e, "Não foi possível confirmar.")),
    });
  };

  return (
    <Dialog open={!!selection} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Confirmar escolha</DialogTitle>
          <DialogDescription>{selection?.title}</DialogDescription>
        </DialogHeader>

        {createdDraft ? (
          <div className="space-y-3 text-sm">
            <p className="flex items-start gap-2 rounded-md border border-blue-200 bg-blue-50 p-3 text-blue-800 dark:border-blue-800 dark:bg-blue-900/20 dark:text-blue-300">
              <FilePlus2 className="mt-0.5 h-4 w-4 shrink-0" />
              Aditivo em rascunho criado. Revise e envie ao cliente na aba Aditivos.
            </p>
            <DialogFooter>
              <Button variant="outline" onClick={onClose}>Fechar</Button>
              <Button onClick={() => { onClose(); onOpenChangeOrders(); }}>Ver aditivos</Button>
            </DialogFooter>
          </div>
        ) : (
          <>
            <div className="space-y-2 text-sm">
              {selection?.status === "Open" ? (
                <fieldset className="space-y-1">
                  <legend className="mb-1 text-xs text-muted-foreground">
                    O cliente ainda não escolheu. Registre a opção combinada:
                  </legend>
                  {selection.options.map((o) => (
                    <label key={o.id} className="flex items-center gap-2">
                      <input type="radio" name="confirm-option" value={o.id} checked={optionId === o.id}
                        onChange={() => setOptionId(o.id)} />
                      {o.name}{o.isStandard ? " (padrão)" : ` · ${fmtSignedBRL(o.priceDelta)}`}
                    </label>
                  ))}
                </fieldset>
              ) : (
                <p>Opção escolhida pelo cliente: <strong>{option?.name ?? "—"}</strong></p>
              )}
              <p className="rounded-md border border-border bg-muted/30 p-2 text-xs text-muted-foreground" data-testid="draft-change-order-info">
                {createsDraft
                  ? `Esta opção muda o contrato (${fmtSignedBRL(option?.priceDelta ?? 0)}${
                      (option?.leadTimeDays ?? 0) > 0 ? `, +${option?.leadTimeDays} dias de prazo` : ""
                    }): ao confirmar, um aditivo em rascunho é criado na aba Aditivos para você revisar e enviar ao cliente.`
                  : "Quando a opção muda o preço ou o prazo, confirmar cria um aditivo em rascunho na aba Aditivos. Esta não muda."}
              </p>
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={onClose}>Voltar</Button>
              <Button onClick={confirm} disabled={!optionId || mut.isPending}>Confirmar escolha</Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
