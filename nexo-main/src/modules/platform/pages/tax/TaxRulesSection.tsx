import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { formatDateTime } from "@/lib/formatters";
import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import type { ProfessionalReviewStatus, TaxRegime, TaxRuleVersionDto } from "@/modules/tax/api/tax.api";
import { INTERNAL_APPROVAL, INTERNAL_APPROVAL_NOTICE, PROFESSIONAL_REVIEW, RULE_STATUS, labelOf, toneOf } from "@/modules/tax/lib/labels";
import { brazilTodayIso, formatIsoDate } from "@/modules/tax/lib/forms";
import { ToneBadge } from "@/modules/tax/components/TaxBadges";
import { Field, ProblemList } from "@/modules/tax/components/TaxShell";
import { DownloadButton, FilePicker, TaxQueryError, TaxRefusal, toastError, useReasonDialog } from "@/modules/tax/components/TaxCommon";
import {
  PLATFORM_TAX_KEY, PLATFORM_TAX_KEYS, approveForPilot, changeRuleVersionStatus, documentUrl, listProfessionalReviews, listRules,
  recordProfessionalReview, uploadDocument, withdrawPilotApproval,
} from "../../services/taxAdminApi";

/** The statuses an operator can record — "Pending" is only the initial state. */
const REVIEW_STATUSES: Exclude<ProfessionalReviewStatus, "Pending">[] = ["ReviewReceived", "Approved", "ApprovedWithCaveats", "ChangesRequired", "Rejected"];

/** The rule catalogue with both approval dimensions per version: internal pilot approval and professional review — never confused. */
export function TaxRulesSection() {
  const rules = useQuery({ queryKey: PLATFORM_TAX_KEYS.rules, queryFn: listRules });
  const [dialog, setDialog] = useState<{ kind: "pilot" | "review"; v: TaxRuleVersionDto; key: string } | null>(null);
  const [reasonDialog, askReason] = useReasonDialog();
  const [confirm, setConfirm] = useState<{ id: string; to: "Active" | "Revoked"; label: string } | null>(null);
  const qc = useQueryClient();
  const refresh = () => void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY });
  const status = useMutation({
    mutationFn: ({ id, to }: { id: string; to: "Active" | "Revoked" }) => changeRuleVersionStatus(id, to),
    onSuccess: () => { toast.success("Situação da versão alterada."); refresh(); },
    onError: toastError,
  });
  const withdraw = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => withdrawPilotApproval(id, reason),
    onSuccess: () => { toast.success("Aprovação interna retirada."); refresh(); },
    onError: toastError,
  });

  return (
    <SectionCard title="Regras" description={`Aprovação interna do piloto e revisão profissional são registros separados. ${INTERNAL_APPROVAL_NOTICE}`}>
      {reasonDialog}
      {rules.isLoading && <Skeleton className="h-40 w-full" />}
      {rules.isError && <TaxQueryError error={rules.error} what="as regras" onRetry={() => void rules.refetch()} />}
      {rules.data?.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma regra no catálogo.</p>}
      <ul className="space-y-4">
        {rules.data?.map((r) => (
          <li key={r.id} className="rounded-md border border-border p-3">
            <p className="font-medium">{r.name} <span className="font-mono text-xs text-muted-foreground">{r.key}</span></p>
            <p className="text-sm text-muted-foreground">{r.description}</p>
            <ul className="mt-2 space-y-3">
              {r.versions.map((v) => (
                <li key={v.id} className="space-y-1 border-t border-border pt-2 text-sm">
                  <p className="flex flex-wrap items-center gap-2">
                    <span className="font-medium">v{v.version}</span>
                    <ToneBadge label={labelOf(RULE_STATUS, v.status)} tone={toneOf(RULE_STATUS, v.status)} />
                    <ToneBadge label={labelOf(INTERNAL_APPROVAL, v.internalApproval.status)} tone={toneOf(INTERNAL_APPROVAL, v.internalApproval.status)} />
                    <ToneBadge label={labelOf(PROFESSIONAL_REVIEW, v.professionalReview)} tone={toneOf(PROFESSIONAL_REVIEW, v.professionalReview)} />
                    <span className="text-xs text-muted-foreground">{formatIsoDate(v.validFrom)}{v.validTo ? ` a ${formatIsoDate(v.validTo)}` : " em diante"} · fontes {v.legalSourceCodes.join(", ") || "—"}</span>
                  </p>
                  {v.internalApproval.approvedBy && (
                    <p className="text-xs text-muted-foreground">
                      Aprovação interna por {v.internalApproval.approvedBy} ({v.internalApproval.approvedByRole}) em {formatDateTime(v.internalApproval.approvedAt ?? "")} · {v.internalApproval.decisionReference}
                      {v.internalApproval.withdrawalReason ? ` · retirada: ${v.internalApproval.withdrawalReason}` : ""}
                    </p>
                  )}
                  {v.professionalReviewedBy && (
                    <p className="text-xs text-muted-foreground">
                      Revisão: {v.professionalReviewedBy}{v.professionalReference ? ` (${v.professionalReference})` : ""} · registrada por {v.professionalReviewRecordedBy ?? "—"}
                      {v.professionalReviewRegimes?.length ? ` · regimes ${v.professionalReviewRegimes.join(", ")}` : ""}
                    </p>
                  )}
                  <div className="flex flex-wrap gap-1">
                    {(v.status === "Unverified" || v.status === "Draft") && (
                      <Button type="button" size="sm" variant="outline" disabled={status.isPending}
                        onClick={() => setConfirm({ id: v.id, to: "Active", label: `${r.key} v${v.version}` })}>Ativar versão</Button>
                    )}
                    {v.status === "Active" && (
                      <Button type="button" size="sm" variant="ghost" className="text-destructive" disabled={status.isPending}
                        onClick={() => setConfirm({ id: v.id, to: "Revoked", label: `${r.key} v${v.version}` })}>Revogar versão</Button>
                    )}
                    {v.status === "Active" && v.internalApproval.status !== "ApprovedForControlledPilot" && (
                      <Button type="button" size="sm" variant="outline" onClick={() => setDialog({ kind: "pilot", v, key: r.key })}>Aprovar para o piloto…</Button>
                    )}
                    {v.internalApproval.status === "ApprovedForControlledPilot" && (
                      <Button type="button" size="sm" variant="ghost" onClick={() => askReason({
                        title: "Retirar aprovação interna", description: "A regra deixa de ser aceita no piloto controlado a partir de agora.", confirmLabel: "Retirar", danger: true,
                        onConfirm: (reason) => withdraw.mutate({ id: v.id, reason }),
                      })}>Retirar aprovação interna</Button>
                    )}
                    <Button type="button" size="sm" variant="outline" onClick={() => setDialog({ kind: "review", v, key: r.key })}>Registrar revisão profissional recebida…</Button>
                  </div>
                  <ReviewHistory versionId={v.id} />
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
      <AlertDialog open={confirm !== null} onOpenChange={(o) => { if (!o) setConfirm(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{confirm?.to === "Active" ? `Ativar ${confirm?.label}?` : `Revogar ${confirm?.label}?`}</AlertDialogTitle>
            <AlertDialogDescription>
              {confirm?.to === "Active"
                ? "Só ativa se todas as fontes jurídicas estiverem conferidas. Ativar não aprova a regra para o piloto nem registra revisão profissional."
                : "A versão deixa de valer para trabalho novo; cálculos já feitos continuam reproduzíveis com ela."}
              {" "}O ato fica na auditoria com a sua sessão.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={() => { if (confirm) status.mutate({ id: confirm.id, to: confirm.to }); }}>
              {confirm?.to === "Active" ? "Ativar versão" : "Revogar versão"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      {dialog?.kind === "pilot" && <PilotApprovalDialog v={dialog.v} ruleKey={dialog.key} onClose={() => setDialog(null)} />}
      {dialog?.kind === "review" && <ProfessionalReviewDialog v={dialog.v} ruleKey={dialog.key} onClose={() => setDialog(null)} />}
    </SectionCard>
  );
}

function ReviewHistory({ versionId }: Readonly<{ versionId: string }>) {
  const [open, setOpen] = useState(false);
  const reviews = useQuery({ queryKey: PLATFORM_TAX_KEYS.reviews(versionId), queryFn: () => listProfessionalReviews(versionId), enabled: open });
  return (
    <details onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)}>
      <summary className="cursor-pointer text-xs font-medium">Histórico de revisões profissionais</summary>
      <div className="mt-2 space-y-2">
        {reviews.isLoading && <Skeleton className="h-10 w-full" />}
        {reviews.isError && <TaxQueryError error={reviews.error} what="o histórico" />}
        {reviews.data?.length === 0 && <p className="text-xs text-muted-foreground">Nenhuma revisão registrada — a versão está pendente.</p>}
        {reviews.data?.map((r) => (
          <div key={r.id} className="rounded border border-border p-2 text-xs">
            <p className="flex flex-wrap items-center gap-2">
              <ToneBadge label={labelOf(PROFESSIONAL_REVIEW, r.status)} tone={toneOf(PROFESSIONAL_REVIEW, r.status)} />
              <span>antes: {labelOf(PROFESSIONAL_REVIEW, r.previousStatus)}</span>
            </p>
            <p>Profissional informado: {r.reviewer}{r.professionalReference ? ` (${r.professionalReference})` : ""}{r.reviewedOn ? ` · revisão de ${formatIsoDate(r.reviewedOn)}` : ""}</p>
            <p className="text-muted-foreground">Registrado por {r.recordedByName} ({r.recordedByRole}) em {formatDateTime(r.recordedAt)} — horário do servidor.</p>
            {r.scope && <p>Escopo: {r.scope}</p>}
            {r.coveredRegimes.length > 0 && <p>Regimes: {r.coveredRegimes.join(", ")}</p>}
            {r.validFrom && <p>Vale de {formatIsoDate(r.validFrom)}{r.validTo ? ` a ${formatIsoDate(r.validTo)}` : " em diante"}</p>}
            {r.assumptions && <p>Premissas: {r.assumptions}</p>}
            {r.caveats && <p className="text-warning">Ressalvas: {r.caveats}</p>}
            {r.notes && <p>Notas: {r.notes}</p>}
            {r.supportingDocument && (
              <p className="flex flex-wrap items-center gap-2">
                Documento: {r.supportingDocument.originalFileName} <span className="break-all font-mono">SHA-256 {r.supportingDocument.sha256}</span>
                <DownloadButton path={documentUrl(r.supportingDocument.id)} fileName={r.supportingDocument.originalFileName} variant="ghost" />
              </p>
            )}
          </div>
        ))}
      </div>
    </details>
  );
}

function PilotApprovalDialog({ v, ruleKey, onClose }: Readonly<{ v: TaxRuleVersionDto; ruleKey: string; onClose: () => void }>) {
  const qc = useQueryClient();
  const [scope, setScope] = useState("");
  const [from, setFrom] = useState(brazilTodayIso());
  const [to, setTo] = useState("");
  const [caveats, setCaveats] = useState("");
  const [reference, setReference] = useState("");
  const approve = useMutation({
    mutationFn: () => approveForPilot(v.id, { scope: scope.trim(), validFrom: from, validTo: to || null, caveats: caveats.trim(), decisionReference: reference.trim() }),
    onSuccess: () => { toast.success("Aprovação interna registrada."); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); onClose(); },
    onError: toastError,
  });
  const ready = scope.trim() && caveats.trim() && reference.trim() && from;
  return (
    <Dialog open onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Aprovar {ruleKey} v{v.version} para o piloto</DialogTitle>
          <DialogDescription>{INTERNAL_APPROVAL_NOTICE} Quem aprova é a sua sessão, registrada com o seu papel.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field id="pa-scope" label="Escopo" className="sm:col-span-2"><Textarea id="pa-scope" maxLength={2000} value={scope} onChange={(e) => setScope(e.target.value)} /></Field>
          <Field id="pa-from" label="Vale de"><Input id="pa-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field id="pa-to" label="até (opcional)"><Input id="pa-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field id="pa-caveats" label="Ressalvas aceitas" className="sm:col-span-2"><Textarea id="pa-caveats" maxLength={4000} value={caveats} onChange={(e) => setCaveats(e.target.value)} /></Field>
          <Field id="pa-ref" label="Referência da decisão" className="sm:col-span-2"><Input id="pa-ref" maxLength={300} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
          {approve.isError && <div className="sm:col-span-2"><TaxRefusal error={approve.error} /></div>}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={!ready || approve.isPending} onClick={() => approve.mutate()}>Registrar aprovação interna</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Records a professional review that was actually received. Nothing is pre-filled or inferred: the professional is typed as
 * informed (the platform does not verify it), and the authenticated recorder and server time are stored apart from it.
 */
function ProfessionalReviewDialog({ v, ruleKey, onClose }: Readonly<{ v: TaxRuleVersionDto; ruleKey: string; onClose: () => void }>) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Exclude<ProfessionalReviewStatus, "Pending">>("ReviewReceived");
  const [reviewer, setReviewer] = useState("");
  const [reference, setReference] = useState("");
  const [reviewedOn, setReviewedOn] = useState("");
  const [scope, setScope] = useState("");
  const [regimes, setRegimes] = useState<TaxRegime[]>([]);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [assumptions, setAssumptions] = useState("");
  const [caveats, setCaveats] = useState("");
  const [notes, setNotes] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [touched, setTouched] = useState(false);
  const approving = status === "Approved" || status === "ApprovedWithCaveats";
  const problems = [
    ...(reviewer.trim() ? [] : ["Informe o profissional, como consta na revisão recebida."]),
    ...(approving && !scope.trim() ? ["Uma aprovação diz o escopo."] : []),
    ...(approving && !from ? ["Uma aprovação diz desde quando vale."] : []),
    ...(approving && regimes.length === 0 ? ["Uma aprovação diz a quais regimes se aplica."] : []),
    ...(status === "ApprovedWithCaveats" && !caveats.trim() ? ["Aprovação com ressalvas precisa das ressalvas."] : []),
    ...((status === "ChangesRequired" || status === "Rejected") && !notes.trim() ? ["Diga o que foi pedido ou por que foi rejeitada."] : []),
    ...(confirmed ? [] : ["Confirme que esta revisão foi de fato recebida."]),
  ];
  const save = useMutation({
    mutationFn: async () => {
      const doc = file ? await uploadDocument("professional-review", file, `Revisão de ${ruleKey} v${v.version}`) : null;
      const opt = (s: string) => s.trim() || null;
      return recordProfessionalReview(v.id, {
        status, reviewer: reviewer.trim(), notes: opt(notes), professionalReference: opt(reference), scope: opt(scope),
        validFrom: from || null, validTo: to || null, assumptions: opt(assumptions), caveats: opt(caveats), coveredRegimes: regimes,
        reviewedOn: reviewedOn || null, supportingDocumentId: doc?.id ?? null,
      });
    },
    onSuccess: () => { toast.success("Revisão profissional registrada no histórico."); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); onClose(); },
    onError: toastError,
  });
  const toggleRegime = (r: TaxRegime, on: boolean) => setRegimes((cur) => (on ? [...cur, r] : cur.filter((x) => x !== r)));

  return (
    <Dialog open onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent className="max-h-[90vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Revisão profissional de {ruleKey} v{v.version}</DialogTitle>
          <DialogDescription>
            Registre apenas uma revisão que você recebeu de um profissional. O sistema não verifica o profissional; você, como quem registra, e o horário do servidor ficam gravados à parte.
          </DialogDescription>
        </DialogHeader>
        <form id="review-form" className="grid gap-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
          <Field id="pr-status" label="Resultado da revisão">
            <Select value={status} onValueChange={(s) => setStatus(s as typeof status)}>
              <SelectTrigger id="pr-status"><SelectValue /></SelectTrigger>
              <SelectContent>{REVIEW_STATUSES.map((s) => <SelectItem key={s} value={s}>{PROFESSIONAL_REVIEW[s].label}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field id="pr-on" label="Data da revisão"><Input id="pr-on" type="date" value={reviewedOn} onChange={(e) => setReviewedOn(e.target.value)} /></Field>
          <Field id="pr-reviewer" label="Profissional (como informado na revisão)"><Input id="pr-reviewer" maxLength={200} autoComplete="off" value={reviewer} onChange={(e) => setReviewer(e.target.value)} /></Field>
          <Field id="pr-ref" label="Registro profissional informado (opcional)"><Input id="pr-ref" maxLength={100} autoComplete="off" value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
          <Field id="pr-scope" label="Escopo" className="sm:col-span-2"><Textarea id="pr-scope" maxLength={2000} value={scope} onChange={(e) => setScope(e.target.value)} /></Field>
          <fieldset className="sm:col-span-2">
            <legend className="text-sm font-medium">Regimes cobertos</legend>
            <div className="mt-1 flex flex-wrap gap-3">
              {TAX_REGIMES.map((r) => (
                <label key={r.value} htmlFor={`pr-reg-${r.value}`} className="flex items-center gap-1.5 text-sm">
                  <Checkbox id={`pr-reg-${r.value}`} checked={regimes.includes(r.value)} onCheckedChange={(c) => toggleRegime(r.value, c === true)} /> {r.label}
                </label>
              ))}
            </div>
          </fieldset>
          <Field id="pr-from" label="Vale de"><Input id="pr-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field id="pr-to" label="até (opcional)"><Input id="pr-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field id="pr-assumptions" label="Premissas" className="sm:col-span-2"><Textarea id="pr-assumptions" maxLength={4000} value={assumptions} onChange={(e) => setAssumptions(e.target.value)} /></Field>
          <Field id="pr-caveats" label="Ressalvas" className="sm:col-span-2"><Textarea id="pr-caveats" maxLength={4000} value={caveats} onChange={(e) => setCaveats(e.target.value)} /></Field>
          <Field id="pr-notes" label="Notas" className="sm:col-span-2"><Textarea id="pr-notes" maxLength={4000} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
          <div className="sm:col-span-2">
            <p className="mb-1 text-sm font-medium">Documento da revisão (opcional)</p>
            <FilePicker id="pr-file" file={file} onChange={setFile} disabled={save.isPending} />
          </div>
          <label htmlFor="pr-confirm" className="flex items-start gap-2 rounded-md border border-warning/40 p-2 text-sm sm:col-span-2">
            <Checkbox id="pr-confirm" checked={confirmed} onCheckedChange={(c) => setConfirmed(c === true)} className="mt-0.5" />
            <span>Confirmo que esta revisão foi de fato recebida do profissional informado e que os dados acima reproduzem o que ela diz.</span>
          </label>
          <div className="sm:col-span-2">{touched && <ProblemList problems={problems} />}</div>
          {save.isError && <div className="sm:col-span-2"><TaxRefusal error={save.error} /></div>}
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="submit" form="review-form" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar revisão"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
