import { useState } from "react";
import { Ban, Check, Info, Pencil, Plus, Send, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useProjectFinancial } from "../../hooks/use-build";
import { useChangeOrderAction, useChangeOrders, type ChangeOrderAction } from "../../hooks/use-build-commercial";
import type { BuildChangeOrderDto } from "../../api/build-commercial.api";
import {
  CHANGE_ORDER_STATUS, fmtDays, fmtSignedBRL, reasonLabel, signedTone,
} from "../../lib/commercial";
import { fmtBRL, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { ChangeOrderFormDialog } from "./ChangeOrderFormDialog";

const ACTION_COPY: Record<ChangeOrderAction, {
  title: string; confirm: string; done: string; destructive?: boolean; text?: string; body: string;
}> = {
  send: {
    title: "Enviar ao cliente?", confirm: "Enviar", done: "Aditivo enviado ao cliente.",
    body: "O aditivo aparece no portal do cliente. Com portal protegido por senha, o cliente aprova ou recusa por lá. Depois de enviado ele não pode mais ser editado.",
  },
  approve: {
    title: "Aprovar internamente?", confirm: "Aprovar", done: "Aditivo aprovado.", text: "Comentário (opcional)",
    body: "Use quando o cliente aprovou fora do portal. O valor entra no contrato (e no orçamento aprovado, se houver) e a etapa afetada tem o fim previsto deslocado.",
  },
  reject: {
    title: "Recusar internamente?", confirm: "Recusar", done: "Aditivo recusado.", destructive: true, text: "Comentário (opcional)",
    body: "Nada muda no contrato nem no cronograma.",
  },
  cancel: {
    title: "Cancelar aditivo?", confirm: "Cancelar aditivo", done: "Aditivo cancelado.", destructive: true,
    body: "O aditivo sai do portal e não pode mais ser decidido.",
  },
};

/** "Aditivos" of an obra: the contract changes, sent to the client or decided internally. */
export function ChangeOrdersTab({ projectId }: { projectId: string }) {
  const { data: list, isLoading, isError } = useChangeOrders(projectId);
  const { data: fin } = useProjectFinancial(projectId);
  const actionMut = useChangeOrderAction(projectId);
  const [editing, setEditing] = useState<BuildChangeOrderDto | null | undefined>(undefined);
  const [pending, setPending] = useState<{ co: BuildChangeOrderDto; action: ChangeOrderAction } | null>(null);

  const run = (comment?: string) => {
    if (!pending) return;
    const copy = ACTION_COPY[pending.action];
    actionMut.mutate({ id: pending.co.id, action: pending.action, comment }, {
      onSuccess: () => { toast.success(copy.done); setPending(null); },
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível concluir.")),
    });
  };

  const copy = pending ? ACTION_COPY[pending.action] : null;

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2 rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          Aditivos mudam o contrato e o prazo da obra. Envie ao cliente para ele decidir no portal do cliente
          (só em portal protegido por senha), ou registre a decisão aqui quando ela acontecer fora do portal.
        </p>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <div className="rounded-md border border-border p-3">
          <p className="text-[10px] uppercase tracking-wide text-muted-foreground">Orçamento aprovado</p>
          <p className="text-sm font-bold tabular-nums">{fmtBRL(fin?.approvedBudget)}</p>
        </div>
        <div className="rounded-md border border-border p-3">
          <p className="text-[10px] uppercase tracking-wide text-muted-foreground">Aditivos aprovados</p>
          <p className={`text-sm font-bold tabular-nums ${signedTone(fin?.approvedChangeOrdersTotal ?? 0)}`}
            data-testid="approved-change-orders-total">
            {fmtSignedBRL(fin?.approvedChangeOrdersTotal ?? 0)}
          </p>
        </div>
        <div className="rounded-md border border-border p-3">
          <p className="text-[10px] uppercase tracking-wide text-muted-foreground">Total do contrato</p>
          <p className="text-sm font-bold tabular-nums text-primary" data-testid="contract-total">{fmtBRL(fin?.contractTotal)}</p>
        </div>
      </div>

      <div className="flex justify-end">
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Novo aditivo</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar os aditivos.
        </p>
      ) : !list?.length ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhum aditivo nesta obra.
        </p>
      ) : (
        <ul className="space-y-2">
          {list.map((co) => {
            const st = CHANGE_ORDER_STATUS[co.status] ?? { label: co.status, tone: "gray" as const };
            const decidable = co.status === "Draft" || co.status === "SentToClient";
            return (
              <li key={co.id} className="space-y-2 rounded-md border border-border bg-card p-3" data-testid={`change-order-${co.number}`}>
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold">#{co.number} · {co.title}</p>
                    <p className="text-xs text-muted-foreground">
                      {reasonLabel(co.reason)}
                      {co.stageName ? ` · ${co.stageName}` : ""}
                      {co.sourceSelectionId ? " · vindo de uma escolha do cliente" : ""}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    {co.decisionChannel && (
                      <span className="rounded-full border border-border px-2 py-0.5 text-[10px] font-medium text-muted-foreground">
                        {co.decisionChannel === "Portal" ? "Portal do cliente" : "Interno"}
                      </span>
                    )}
                    <StatusPill label={st.label} tone={st.tone} />
                  </div>
                </div>

                <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm">
                  <p className={`font-semibold tabular-nums ${signedTone(co.costImpact)}`}>{fmtSignedBRL(co.costImpact)}</p>
                  <p className="text-muted-foreground">{fmtDays(co.scheduleImpactDays)}</p>
                  {co.items.length > 0 && <p className="text-muted-foreground">{co.items.length} item(ns)</p>}
                </div>
                {co.description && <p className="whitespace-pre-wrap text-xs text-muted-foreground">{co.description}</p>}

                {co.decidedAt && (
                  <p className="text-xs text-muted-foreground">
                    {co.status === "Approved" ? "Aprovado" : co.status === "Rejected" ? "Recusado" : "Decidido"}
                    {co.decidedBy ? ` por ${co.decidedBy}` : ""}{co.signerName ? ` (assinado: ${co.signerName})` : ""}
                    {" "}em {fmtDateTime(co.decidedAt)}
                    {co.status === "Approved" && !co.appliedToBudget && " · entrou só no total do contrato (sem orçamento aprovado)"}
                  </p>
                )}
                {co.clientComment && (
                  <p className="rounded-md bg-muted/40 p-2 text-xs">
                    <span className="font-medium">Comentário{co.decisionChannel === "Portal" ? " do cliente" : ""}:</span> {co.clientComment}
                  </p>
                )}

                {decidable && (
                  <div className="flex flex-wrap gap-2">
                    {co.status === "Draft" && (
                      <>
                        <Button size="sm" variant="outline" onClick={() => setEditing(co)}>
                          <Pencil className="mr-1 h-3.5 w-3.5" /> Editar
                        </Button>
                        <Button size="sm" variant="outline" onClick={() => setPending({ co, action: "send" })}>
                          <Send className="mr-1 h-3.5 w-3.5" /> Enviar ao cliente
                        </Button>
                      </>
                    )}
                    <Button size="sm" variant="outline"
                      className="border-emerald-200 text-emerald-600 hover:bg-emerald-50 dark:border-emerald-800"
                      onClick={() => setPending({ co, action: "approve" })}>
                      <Check className="mr-1 h-3.5 w-3.5" /> Aprovar
                    </Button>
                    <Button size="sm" variant="outline" className="border-destructive/30 text-destructive hover:bg-destructive/5"
                      onClick={() => setPending({ co, action: "reject" })}>
                      <X className="mr-1 h-3.5 w-3.5" /> Recusar
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setPending({ co, action: "cancel" })}>
                      <Ban className="mr-1 h-3.5 w-3.5" /> Cancelar
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <ChangeOrderFormDialog projectId={projectId} changeOrder={editing} onClose={() => setEditing(undefined)} />
      <ConfirmActionDialog
        open={!!pending}
        title={copy ? `${copy.title} (#${pending?.co.number})` : ""}
        description={copy && <p>{copy.body}</p>}
        confirmLabel={copy?.confirm ?? ""}
        destructive={copy?.destructive}
        textLabel={copy?.text}
        pending={actionMut.isPending}
        onConfirm={run}
        onClose={() => setPending(null)}
      />
    </div>
  );
}
