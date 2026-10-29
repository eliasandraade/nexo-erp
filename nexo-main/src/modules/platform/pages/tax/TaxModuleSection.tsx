import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CircleAlert, Info, OctagonX, TriangleAlert } from "lucide-react";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { formatDateTime } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import type { TaxApprovalMode } from "@/modules/tax/api/tax.api";
import { MODULE_STATUS, labelOf, toneOf } from "@/modules/tax/lib/labels";
import { ToneBadge } from "@/modules/tax/components/TaxBadges";
import { Field } from "@/modules/tax/components/TaxShell";
import { TaxQueryError, TaxRefusal, toastError } from "@/modules/tax/components/TaxCommon";
import {
  DEACTIVATE_PHRASE, PLATFORM_TAX_KEY, PLATFORM_TAX_KEYS, SUSPEND_PHRASE, activateModule, deactivateModule, getActivationChecklist,
  getModuleState, getReadiness, suspendModule, type TaxReadinessItemDto, type TaxReadinessLevel,
} from "../../services/taxAdminApi";

const MODE_LABEL: Record<TaxApprovalMode, string> = {
  Disabled: "Desligado",
  ControlledPilot: "Piloto controlado",
  ProfessionalFinal: "Modo definitivo (revisão profissional)",
};

const LEVEL_LOOK: Record<TaxReadinessLevel, { Icon: typeof Info; color: string; label: string }> = {
  Ok: { Icon: CheckCircle2, color: "text-success", label: "ok" },
  Info: { Icon: Info, color: "text-muted-foreground", label: "informação" },
  Warning: { Icon: TriangleAlert, color: "text-warning", label: "atenção" },
  Blocking: { Icon: OctagonX, color: "text-destructive", label: "bloqueia" },
};

export function ReadinessList({ items }: Readonly<{ items: TaxReadinessItemDto[] }>) {
  return (
    <ul className="space-y-2">
      {items.map((i) => {
        const { Icon, color, label } = LEVEL_LOOK[i.level];
        return (
          <li key={i.key} className="flex gap-2 text-sm">
            <Icon className={cn("mt-0.5 h-4 w-4 shrink-0", color)} aria-label={label} />
            <div className="min-w-0">
              <p className="font-medium">{i.label} <span className="text-xs font-normal text-muted-foreground">· {i.area}</span></p>
              <p className="break-words text-muted-foreground">{i.detail}</p>
            </div>
          </li>
        );
      })}
    </ul>
  );
}

/** Module state, readiness, and the guarded acts: activation (wizard), suspension and deactivation. */
export function TaxModuleSection() {
  const state = useQuery({ queryKey: PLATFORM_TAX_KEYS.module, queryFn: getModuleState });
  const readiness = useQuery({ queryKey: PLATFORM_TAX_KEYS.readiness, queryFn: getReadiness });
  const [wizard, setWizard] = useState<TaxApprovalMode | null>(null);
  const [stop, setStop] = useState<"suspend" | "deactivate" | null>(null);
  const s = state.data;

  return (
    <div className="space-y-6">
      <SectionCard title="Estado do módulo" description="A configuração da instalação é lida aqui, nunca alterada por esta tela.">
        {state.isLoading && <Skeleton className="h-24 w-full" />}
        {state.isError && <TaxQueryError error={state.error} what="o estado do módulo" onRetry={() => void state.refetch()} />}
        {s && (
          <div className="space-y-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <ToneBadge label={labelOf(MODULE_STATUS, s.status)} tone={toneOf(MODULE_STATUS, s.status)} />
              <span>{s.newWorkRuns ? "Novos cálculos e decisões rodam." : "Novos cálculos e decisões NÃO rodam."}</span>
            </div>
            <p>{s.summary}</p>
            <dl className="grid gap-2 sm:grid-cols-3">
              <div><dt className="text-xs text-muted-foreground">Configuração: ligado</dt><dd>{s.configEnabled ? "Sim" : "Não"}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Configuração: modo</dt><dd>{MODE_LABEL[s.configMode]}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Ativado na plataforma</dt><dd>{s.activatedMode ? MODE_LABEL[s.activatedMode] : "—"}</dd></div>
            </dl>
            {s.configurationProblems.length > 0 && (
              <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/5 p-3">
                <p className="font-medium text-destructive">Configuração inválida — o módulo recusa trabalho novo (fail-closed)</p>
                <ul className="list-disc pl-5">{s.configurationProblems.map((p) => <li key={p}>{p}</li>)}</ul>
              </div>
            )}
            <div className="flex flex-wrap gap-2">
              {s.configMode !== "Disabled" && s.status !== "Active" && (
                <Button type="button" onClick={() => setWizard(s.configMode)}>Ativar {MODE_LABEL[s.configMode].toLowerCase()}…</Button>
              )}
              {s.status === "Active" && <Button type="button" variant="destructive" onClick={() => setStop("suspend")}>Suspender…</Button>}
              {s.status !== "Inactive" && <Button type="button" variant="outline" onClick={() => setStop("deactivate")}>Desativar…</Button>}
            </div>
            {s.configMode === "Disabled" && <p className="text-xs text-muted-foreground">A configuração da instalação está em “Desligado”: não há modo a ativar.</p>}
            {s.history.length > 0 && (
              <details>
                <summary className="cursor-pointer font-medium">Histórico de ativações ({s.history.length})</summary>
                <ol className="mt-2 space-y-2">
                  {s.history.map((h) => (
                    <li key={h.id} className="text-xs">
                      <span className="font-medium">{labelOf(MODULE_STATUS, h.status)}</span> · {MODE_LABEL[h.mode]} · {h.decisionReference} — {h.reason}
                      <span className="block text-muted-foreground">{h.actorName} ({h.actorRole}) em {formatDateTime(h.at)}</span>
                    </li>
                  ))}
                </ol>
              </details>
            )}
          </div>
        )}
      </SectionCard>

      <SectionCard title="Prontidão" description="Diagnóstico administrativo: configuração, armazenamento, regras, catálogos e auditoria.">
        {readiness.isLoading && <Skeleton className="h-32 w-full" />}
        {readiness.isError && <TaxQueryError error={readiness.error} what="a prontidão" onRetry={() => void readiness.refetch()} />}
        {readiness.data && (
          <div className="space-y-3">
            <p className="text-xs text-muted-foreground">
              Motor {readiness.data.engineVersion} · canônico {readiness.data.canonicalVersion} · política de cobertura {readiness.data.coveragePolicy} · gerado em {formatDateTime(readiness.data.generatedAt)}
            </p>
            <ReadinessList items={readiness.data.items} />
            <dl className="grid gap-2 text-sm sm:grid-cols-3">
              <div><dt className="text-xs text-muted-foreground">Regras elegíveis</dt><dd>{readiness.data.rulesEligible.join(", ") || "—"}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Regras que bloqueiam</dt><dd>{readiness.data.rulesBlocking.join(", ") || "—"}</dd></div>
              <div><dt className="text-xs text-muted-foreground">Aguardando regulamentação</dt><dd>{readiness.data.pendingRegulation.join(", ") || "—"}</dd></div>
            </dl>
          </div>
        )}
      </SectionCard>

      {wizard && <ActivationWizard mode={wizard} onClose={() => setWizard(null)} />}
      {stop && <StopDialog kind={stop} onClose={() => setStop(null)} />}
    </div>
  );
}

const PILOT_STEPS = [
  "Modo", "Pré-condições", "Regras sem aprovação", "Ressalvas", "Modo sombra", "Referência da decisão", "Motivo", "Frase de confirmação", "Revisão final",
] as const;
const FINAL_STEPS = ["Modo", "Pré-condições", "Regras sem aprovação", "Ressalvas", "Referência da decisão", "Motivo", "Frase de confirmação", "Revisão final"] as const;

/**
 * Activation of the configured mode, step by step. Nothing is pre-filled: the operator reads each precondition, acknowledges
 * the shadow mode (pilot), states the decision reference and reason, and types the exact phrase. The definitive mode stays
 * blocked while any required rule lacks a valid professional review — the server decides, the wizard only shows why.
 */
function ActivationWizard({ mode, onClose }: Readonly<{ mode: TaxApprovalMode; onClose: () => void }>) {
  const qc = useQueryClient();
  const steps = mode === "ControlledPilot" ? PILOT_STEPS : FINAL_STEPS;
  const [step, setStep] = useState(0);
  const [shadow, setShadow] = useState(false);
  const [reference, setReference] = useState("");
  const [reason, setReason] = useState("");
  const [phrase, setPhrase] = useState("");
  const checklist = useQuery({ queryKey: PLATFORM_TAX_KEYS.checklist(mode), queryFn: () => getActivationChecklist(mode) });
  const c = checklist.data;
  const activate = useMutation({
    mutationFn: () => activateModule({ mode, decisionReference: reference.trim(), reason: reason.trim(), confirmation: phrase, acknowledgeShadowMode: shadow }),
    onSuccess: () => { toast.success("Módulo ativado. Ato registrado na auditoria."); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); onClose(); },
    onError: toastError,
  });
  const name = steps[step];
  const blocked = !!c && !c.canActivate;
  const canNext = (() => {
    if (!c) return false;
    switch (name) {
      case "Pré-condições":
      case "Regras sem aprovação": return c.canActivate;
      case "Modo sombra": return shadow;
      case "Referência da decisão": return reference.trim().length > 0;
      case "Motivo": return reason.trim().length > 0;
      case "Frase de confirmação": return phrase === c.confirmationPhrase;
      default: return true;
    }
  })();

  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-h-[90vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Ativar: {MODE_LABEL[mode]}</DialogTitle>
          <DialogDescription>Passo {step + 1} de {steps.length} — {name}</DialogDescription>
        </DialogHeader>
        <ol className="flex flex-wrap gap-1" aria-label="Passos">
          {steps.map((s, i) => (
            <li key={s} aria-current={i === step ? "step" : undefined}
              className={cn("rounded px-2 py-0.5 text-[11px]", i === step ? "bg-primary text-primary-foreground" : i < step ? "bg-muted" : "text-muted-foreground")}>
              {i + 1}. {s}
            </li>
          ))}
        </ol>

        {checklist.isLoading && <Skeleton className="h-24 w-full" />}
        {checklist.isError && <TaxQueryError error={checklist.error} what="as pré-condições" onRetry={() => void checklist.refetch()} />}
        {c && (
          <div className="space-y-3 text-sm">
            {name === "Modo" && (
              <div className="space-y-2">
                <p>Modo configurado na instalação: <strong>{MODE_LABEL[mode]}</strong>. Esta tela não muda a configuração — só registra a ativação do modo já configurado.</p>
                {mode === "ControlledPilot" && <p>No piloto controlado, só as empresas do escopo do piloto trabalham, em modo sombra, com regras aprovadas internamente — aprovação interna não é revisão profissional.</p>}
                {mode === "ProfessionalFinal" && <p>No modo definitivo, cada regra exigida precisa de revisão profissional válida para a data e o regime. Sem isso, a ativação é recusada.</p>}
              </div>
            )}
            {name === "Pré-condições" && (
              <>
                <ReadinessList items={c.items} />
                {blocked && <TaxRefusal error={new Error("Há pré-condições bloqueando a ativação. Resolva-as e volte ao assistente.")} />}
                {blocked && c.rulesMissingApproval.length > 0 && (
                  <div className="space-y-1">
                    <p className="font-medium">{mode === "ProfessionalFinal" ? "Regras sem revisão profissional válida" : "Regras sem aprovação interna do piloto"}</p>
                    <ul className="list-disc pl-5">{c.rulesMissingApproval.map((r) => <li key={r} className="font-mono text-xs">{r}</li>)}</ul>
                  </div>
                )}
              </>
            )}
            {name === "Regras sem aprovação" && (
              c.rulesMissingApproval.length === 0
                ? <p className="flex items-center gap-2 text-success"><CheckCircle2 className="h-4 w-4" /> Todas as regras exigidas têm a aprovação que este modo pede.</p>
                : (
                  <div role="alert" className="space-y-1 rounded-md border border-destructive/40 bg-destructive/5 p-3">
                    <p className="flex items-center gap-2 font-medium text-destructive"><CircleAlert className="h-4 w-4" />
                      {mode === "ProfessionalFinal" ? "Bloqueado: regras sem revisão profissional válida" : "Bloqueado: regras sem aprovação interna do piloto"}</p>
                    <ul className="list-disc pl-5">{c.rulesMissingApproval.map((r) => <li key={r} className="font-mono text-xs">{r}</li>)}</ul>
                  </div>
                )
            )}
            {name === "Ressalvas" && (
              c.caveats.length === 0 ? <p>Sem ressalvas registradas.</p> : <ul className="list-disc space-y-1 pl-5">{c.caveats.map((x) => <li key={x}>{x}</li>)}</ul>
            )}
            {name === "Modo sombra" && (
              <label htmlFor="wz-shadow" className="flex items-start gap-2 rounded-md border border-warning/40 p-3">
                <Checkbox id="wz-shadow" checked={shadow} onCheckedChange={(v) => setShadow(v === true)} className="mt-0.5" />
                <span>Entendo que o piloto roda em modo sombra: nenhum documento fiscal, preço, cadastro ou recolhimento é alterado automaticamente, e os resultados não são parecer profissional.</span>
              </label>
            )}
            {name === "Referência da decisão" && (
              <Field id="wz-ref" label="Referência da decisão (ata, chamado, documento)">
                <Input id="wz-ref" maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} />
              </Field>
            )}
            {name === "Motivo" && (
              <Field id="wz-reason" label="Motivo"><Textarea id="wz-reason" maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
            )}
            {name === "Frase de confirmação" && (
              <Field id="wz-phrase" label={`Digite exatamente: ${c.confirmationPhrase}`}>
                <Input id="wz-phrase" autoComplete="off" value={phrase} onChange={(e) => setPhrase(e.target.value)} />
              </Field>
            )}
            {name === "Revisão final" && (
              <dl className="grid gap-2 sm:grid-cols-2">
                <div><dt className="text-xs text-muted-foreground">Modo</dt><dd>{MODE_LABEL[mode]}</dd></div>
                <div><dt className="text-xs text-muted-foreground">Referência</dt><dd>{reference}</dd></div>
                <div className="sm:col-span-2"><dt className="text-xs text-muted-foreground">Motivo</dt><dd className="whitespace-pre-wrap">{reason}</dd></div>
                {mode === "ControlledPilot" && <div className="sm:col-span-2"><dt className="text-xs text-muted-foreground">Modo sombra</dt><dd>{shadow ? "Reconhecido" : "Não reconhecido"}</dd></div>}
              </dl>
            )}
            {activate.isError && <TaxRefusal error={activate.error} />}
          </div>
        )}

        <DialogFooter className="gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          {step > 0 && <Button type="button" variant="outline" onClick={() => setStep(step - 1)}>Voltar</Button>}
          {step < steps.length - 1
            ? <Button type="button" disabled={!canNext} onClick={() => setStep(step + 1)}>Avançar</Button>
            : <Button type="button" disabled={activate.isPending || blocked} onClick={() => activate.mutate()}>{activate.isPending ? "Ativando…" : "Ativar"}</Button>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Suspension (any platform operator — an emergency brake) or deactivation (super admin), each with its typed phrase. */
function StopDialog({ kind, onClose }: Readonly<{ kind: "suspend" | "deactivate"; onClose: () => void }>) {
  const qc = useQueryClient();
  const phraseRequired = kind === "suspend" ? SUSPEND_PHRASE : DEACTIVATE_PHRASE;
  const [reason, setReason] = useState("");
  const [phrase, setPhrase] = useState("");
  const act = useMutation({
    mutationFn: () => (kind === "suspend" ? suspendModule(reason.trim(), phrase) : deactivateModule(reason.trim(), phrase)),
    onSuccess: () => {
      toast.success(kind === "suspend" ? "Módulo suspenso. Nenhum trabalho novo roda." : "Módulo desativado.");
      void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY });
      onClose();
    },
    onError: toastError,
  });
  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{kind === "suspend" ? "Suspender o módulo" : "Desativar o módulo"}</DialogTitle>
          <DialogDescription>
            {kind === "suspend"
              ? "Freio de emergência: trabalho novo é recusado imediatamente. O histórico continua disponível para consulta e reprodução."
              : "Volta o módulo ao estado inativo. Reativar exige o assistente completo de novo."}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Field id="stop-reason" label="Motivo"><Textarea id="stop-reason" maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
          <Field id="stop-phrase" label={`Digite exatamente: ${phraseRequired}`}><Input id="stop-phrase" autoComplete="off" value={phrase} onChange={(e) => setPhrase(e.target.value)} /></Field>
          {act.isError && <TaxRefusal error={act.error} />}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="button" variant="destructive" disabled={act.isPending || !reason.trim() || phrase !== phraseRequired} onClick={() => act.mutate()}>
            {kind === "suspend" ? "Suspender" : "Desativar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
