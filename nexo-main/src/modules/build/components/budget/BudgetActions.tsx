import { useState } from "react";
import { ArrowRightLeft, Check, Copy, Download, GitBranch, Save, Send, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useApproveBudget, useRejectBudget, useSendBudget } from "../../hooks/use-build";
import { useDuplicateBudget, useNewBudgetVersion } from "../../hooks/use-build-commercial";
import { downloadBudgetPdf } from "../../api/build-commercial.api";
import type { BuildBudgetDto } from "../../api/build.api";
import { fmtDate } from "../../lib/format";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { SaveAsTemplateDialog } from "./SaveAsTemplateDialog";
import { ConvertBudgetDialog } from "./ConvertBudgetDialog";

interface Props {
  budget: BuildBudgetDto;
  projectId?: string;
  /** Called with the id of a budget created here (duplicate / new version) so the tab selects it. */
  onCreated: (id: string) => void;
}

type Pending = "approve" | "reject" | null;

/** Lifecycle (send, approve, reject) and commercial actions (duplicate, version, PDF, model, convert). */
export function BudgetActions({ budget, projectId, onCreated }: Props) {
  const sendMut    = useSendBudget(projectId);
  const approveMut = useApproveBudget(projectId);
  const rejectMut  = useRejectBudget(projectId);
  const dupMut     = useDuplicateBudget();
  const versionMut = useNewBudgetVersion();
  const [confirm, setConfirm] = useState<Pending>(null);
  const [templateOpen, setTemplateOpen] = useState(false);
  const [convertOpen, setConvertOpen] = useState(false);
  const [downloading, setDownloading] = useState(false);

  const open = budget.status === "Draft" || budget.status === "Sent";
  const expired = !!budget.isExpired;
  const canVersion = budget.status === "Sent" || budget.status === "Approved" || budget.status === "Rejected";

  const send = () => sendMut.mutate(budget.id, {
    onSuccess: () => toast.success("Orçamento marcado como enviado."),
    onError:   (e) => toast.error(errorMessage(e, "Erro ao enviar.")),
  });

  const approve = () => approveMut.mutate(budget.id, {
    onSuccess: () => { setConfirm(null); toast.success("Orçamento aprovado!"); },
    onError:   (e) => toast.error(errorMessage(e, "Erro ao aprovar.")),
  });

  const reject = (reason?: string) => rejectMut.mutate({ id: budget.id, reason }, {
    onSuccess: () => { setConfirm(null); toast.success("Orçamento recusado."); },
    onError:   (e) => toast.error(errorMessage(e, "Erro ao recusar.")),
  });

  const duplicate = () => dupMut.mutate({ id: budget.id }, {
    onSuccess: (dto) => { onCreated(dto.id); toast.success(`Cópia criada: ${dto.name}.`); },
    onError:   (e) => toast.error(errorMessage(e, "Erro ao duplicar.")),
  });

  const newVersion = () => versionMut.mutate(budget.id, {
    onSuccess: (dto) => { onCreated(dto.id); toast.success(`Versão ${dto.version ?? ""} criada como rascunho.`); },
    onError:   (e) => toast.error(errorMessage(e, "Erro ao criar nova versão.")),
  });

  const pdf = async () => {
    setDownloading(true);
    try {
      await downloadBudgetPdf(budget);
    } catch (e) {
      toast.error(errorMessage(e, "Falha ao gerar o PDF."));
    } finally {
      setDownloading(false);
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {budget.status === "Draft" && (
          <Button size="sm" variant="outline" onClick={send} disabled={sendMut.isPending}>
            <Send className="mr-1 h-3.5 w-3.5" /> Marcar como enviado
          </Button>
        )}
        {open && (
          <>
            <Button size="sm" variant="outline" disabled={expired || approveMut.isPending}
              className="border-emerald-200 text-emerald-600 hover:bg-emerald-50 dark:border-emerald-800"
              onClick={() => setConfirm("approve")}>
              <Check className="mr-1 h-3.5 w-3.5" /> Aprovar
            </Button>
            <Button size="sm" variant="outline" disabled={rejectMut.isPending}
              className="border-destructive/30 text-destructive hover:bg-destructive/5"
              onClick={() => setConfirm("reject")}>
              <X className="mr-1 h-3.5 w-3.5" /> Recusar
            </Button>
          </>
        )}
        {budget.status === "Approved" && (
          <Button size="sm" onClick={() => setConvertOpen(true)}>
            <ArrowRightLeft className="mr-1 h-3.5 w-3.5" /> Converter em obra
          </Button>
        )}
        <Button size="sm" variant="outline" onClick={() => void pdf()} disabled={downloading}>
          <Download className="mr-1 h-3.5 w-3.5" /> {downloading ? "Gerando…" : "Baixar PDF"}
        </Button>
        <Button size="sm" variant="outline" onClick={duplicate} disabled={dupMut.isPending}>
          <Copy className="mr-1 h-3.5 w-3.5" /> Duplicar
        </Button>
        {canVersion && (
          <Button size="sm" variant="outline" onClick={newVersion} disabled={versionMut.isPending}>
            <GitBranch className="mr-1 h-3.5 w-3.5" /> Nova versão
          </Button>
        )}
        <Button size="sm" variant="outline" onClick={() => setTemplateOpen(true)} disabled={budget.items.length === 0}>
          <Save className="mr-1 h-3.5 w-3.5" /> Salvar como modelo
        </Button>
      </div>

      {open && expired && (
        <p role="alert" className="rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
          A validade deste orçamento venceu em {fmtDate(budget.validUntil)}. Para aprovar, altere a validade em
          “Editar condições” ou crie uma nova versão.
        </p>
      )}

      <ConfirmActionDialog
        open={confirm === "approve"}
        title="Aprovar orçamento?"
        description={<p>O valor aprovado da obra passa a ser o total deste orçamento. Fica registrado quem aprovou e quando.</p>}
        confirmLabel="Aprovar"
        pending={approveMut.isPending}
        onConfirm={approve}
        onClose={() => setConfirm(null)}
      />
      <ConfirmActionDialog
        open={confirm === "reject"}
        title="Recusar orçamento?"
        description={<p>Você pode criar uma nova versão depois. O motivo é opcional e fica no histórico.</p>}
        confirmLabel="Recusar"
        destructive
        textLabel="Motivo (opcional)"
        pending={rejectMut.isPending}
        onConfirm={reject}
        onClose={() => setConfirm(null)}
      />
      <SaveAsTemplateDialog budget={budget} open={templateOpen} onClose={() => setTemplateOpen(false)} />
      <ConvertBudgetDialog budget={budget} defaultProjectId={projectId} open={convertOpen} onClose={() => setConvertOpen(false)} />
    </div>
  );
}
