import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Ban, CheckCircle2, CircleHelp, FileCheck2, Gavel, History, UserRound, XCircle } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import { listUsers } from "@/modules/users/api/users.api";
import {
  TAX_KEY, TAX_KEYS, addOpportunityEvidence, answerRequirement, approveOpportunity, assignOpportunity, blockOpportunity, decisionExportUrl,
  getEvidenceStorage, getOpportunityTimeline, getTaxOpportunity, rejectOpportunity, replaceOpportunityEvidence, reviewOpportunityEvidence,
  revokeOpportunityEvidence, startOpportunityReview, uploadOpportunityEvidence, verifyOpportunityExecution, type EvidenceType,
  type ProhibitedStructure, type RequirementPhase, type TaxEvidenceDto, type TaxOpportunityDto, type TaxRequirementDto,
} from "../api/tax.api";
import {
  APPROVAL_REQUIREMENTS, EVIDENCE_STATUS, EVIDENCE_TYPES, IMPACT_NATURES, OPPORTUNITY_TYPES, PROHIBITED_STRUCTURES, SCENARIO_APPROVED_COPY,
  labelOf, toneOf,
} from "../lib/labels";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge, ToneBadge } from "../components/TaxBadges";
import { ApprovalGovernance } from "../components/ApprovalGovernance";
import { SourceLinks } from "../components/EngineResult";
import { ScenarioCompare } from "../components/ScenarioCompare";
import { Field, ProblemList, TaxSubNav } from "../components/TaxShell";
import { DownloadButton, FilePicker, TaxQueryError, toastError, useReasonDialog } from "../components/TaxCommon";
import { StoredFileLine } from "../components/EvidenceFiles";

type DialogKind = "approve" | "reject" | "block" | null;

/**
 * Uma oportunidade respondida em linguagem de negócio: o que aconteceu, quanto muda, qual o risco,
 * qual a fonte, o que é preciso cumprir e quem aprova. Bloqueada = estrutura proibida, sem economia.
 */
export default function TaxOpportunityDetailPage() {
  const { id = "" } = useParams();
  const qc = useQueryClient();
  const { session } = useAuth();
  const isDirector = session?.role === "diretoria";
  const [dialog, setDialog] = useState<DialogKind>(null);
  const query = useQuery({ queryKey: TAX_KEYS.opportunity(id), queryFn: () => getTaxOpportunity(id), enabled: !!id });
  const o = query.data;

  const updated = (next: TaxOpportunityDto) => {
    qc.setQueryData(TAX_KEYS.opportunity(id), next);
    void qc.invalidateQueries({ queryKey: TAX_KEY, predicate: (q) => q.queryKey[1] !== "opportunity" });
  };
  const review = useMutation({ mutationFn: () => startOpportunityReview(id), onSuccess: (n) => { updated(n); toast.success("Revisão iniciada."); }, onError: toastError });

  const open = o && (o.status === "Identified" || o.status === "InReview");
  const executing = o?.status === "Approved";

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária · oportunidade"
        title={o?.title ?? "Oportunidade"}
        actions={<Button asChild variant="outline"><Link to="/tributos/oportunidades"><ArrowLeft className="mr-1.5 h-4 w-4" /> Oportunidades</Link></Button>}
      />
      <TaxSubNav />

      {query.isLoading && <Skeleton className="h-64 w-full" />}
      {query.isError && <TaxQueryError error={query.error} what="a oportunidade" onRetry={() => void query.refetch()} />}
      {o && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <ImpactBadge nature={o.impactNature} />
            <RiskBadge risk={o.risk} />
            <OpportunityStatusBadge status={o.status} />
            <span className="text-xs text-muted-foreground">{labelOf(OPPORTUNITY_TYPES, o.type)} · confiança {o.confidence}%</span>
          </div>
          <Responsible o={o} canAssign={!!open || executing} onUpdated={updated} />

          {o.status === "Blocked" && (
            <Alert variant="destructive">
              <Ban className="h-4 w-4" />
              <AlertTitle>Estrutura proibida: {labelOf(PROHIBITED_STRUCTURES, o.blockedAs)} — {o.blockReason}</AlertTitle>
              <AlertDescription>Nenhuma economia é mostrada para esta oportunidade e ela não pode ser aprovada.</AlertDescription>
            </Alert>
          )}
          {o.status === "Rejected" && o.rejectionReason && (
            <Alert><XCircle className="h-4 w-4" /><AlertTitle>Recusada</AlertTitle><AlertDescription>{o.rejectionReason}{o.reviewedByName ? ` — ${o.reviewedByName}` : ""}</AlertDescription></Alert>
          )}

          <SectionCard title="O que aconteceu?">
            <p className="whitespace-pre-line text-sm">{o.summary}</p>
            {o.sourceCalculationId && (
              <p className="mt-2 text-sm"><Link to={`/tributos/calculos/${o.sourceCalculationId}`} className="font-medium text-primary hover:underline">Ver o cálculo de origem</Link></p>
            )}
          </SectionCard>

          {o.status !== "Blocked" && (
            <SectionCard title="Quanto muda?" description={IMPACT_NATURES[o.impactNature]?.hint}>
              <div className="mb-4 flex flex-wrap items-baseline gap-2">
                <ImpactBadge nature={o.impactNature} />
                <span className="text-2xl font-semibold tabular-nums">{o.estimatedImpact === null ? "Não estimado" : formatCurrency(o.estimatedImpact)}</span>
              </div>
              {o.current && o.proposed && <ScenarioCompare scenarios={[o.current, o.proposed]} />}
            </SectionCard>
          )}

          <SectionCard title="Qual o risco e qual a fonte?">
            <div className="space-y-2 text-sm">
              <p className="flex flex-wrap items-center gap-2"><RiskBadge risk={o.risk} /> confiança {o.confidence}%</p>
              {o.caveats && <p className="whitespace-pre-line text-muted-foreground">{o.caveats}</p>}
              <p>Fonte: <SourceLinks codes={o.legalSourceCodes} /></p>
              {o.professionalReviewPending && (
                <p className="rounded-md border border-warning/40 bg-warning/10 p-2 text-warning">
                  Revisão profissional pendente: as regras usadas estão em vigor no motor, mas a interpretação ainda não foi aprovada por um profissional tributário.
                </p>
              )}
              <ApprovalGovernance governance={o.governance} />
            </div>
          </SectionCard>

          <SectionCard
            title="O que preciso fazer antes da operação?"
            description="Cenário: cada condição precisa estar cumprida, com a evidência pedida, antes da aprovação. Mudar o rótulo para “incondicional” não basta."
          >
            <RequirementList o={o} phase="ScenarioApproval" editable={!!open} onUpdated={updated} />
          </SectionCard>

          <SectionCard
            title="O que confirmar depois da operação?"
            description="Execução: só depois do cenário aprovado e da operação acontecer — documento fiscal autorizado e valores finais."
          >
            <RequirementList o={o} phase="ExecutionVerification" editable={executing} onUpdated={updated} />
            {!executing && !o.execution && <p className="text-xs text-muted-foreground">Fica disponível depois que o cenário for aprovado.</p>}
          </SectionCard>

          <SectionCard title="Evidências">
            <Evidence o={o} editable={!!open || executing} canReadRevoked={isDirector} onUpdated={updated} />
          </SectionCard>

          <SectionCard title="Quem precisa aprovar?">
            {o.decision ? (
              <div className="space-y-2 text-sm">
                <p className="flex items-center gap-2 font-medium text-success"><CheckCircle2 className="h-4 w-4" /> Cenário aprovado por {o.decision.approvedByName} em {formatDateTime(o.decision.approvedAt)}</p>
                <p className="text-muted-foreground">{SCENARIO_APPROVED_COPY}</p>
                <p className="text-muted-foreground">Estratégia: {o.decision.strategy}{o.decision.highRiskAcknowledged ? " · risco alto assumido" : ""}</p>
                {o.decision.selfApprovalJustification && (
                  <p className="text-muted-foreground">Aprovada por quem fez a análise. Justificativa: {o.decision.selfApprovalJustification}</p>
                )}
                <p className="break-all font-mono text-xs text-muted-foreground">dossiê {o.decision.dossierHash} · motor {o.decision.engineVersion}</p>
                <details className="rounded-md border border-border">
                  <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Dossiê da decisão</summary>
                  <pre className="max-h-96 overflow-auto border-t border-border p-3 text-xs">{JSON.stringify(o.decision.dossier, null, 2)}</pre>
                </details>
                <p className="text-xs text-muted-foreground">O hash e a versão demonstram a integridade do registro, não a veracidade material dos fatos.</p>
                {isDirector && (
                  <div className="flex flex-wrap gap-2">
                    <DownloadButton path={decisionExportUrl(o.decision.id, false)} fileName={`dossie-${o.decision.id}.zip`} label="Exportar dossiê (ZIP)" />
                    <DownloadButton path={decisionExportUrl(o.decision.id, true)} fileName={`dossie-${o.decision.id}-com-arquivos.zip`} label="Exportar com os arquivos" />
                  </div>
                )}
                {isDirector && <p className="text-xs text-muted-foreground">O ZIP traz o dossiê exatamente como foi congelado, as fontes, o índice das evidências e um manifesto SHA-256 de cada arquivo.</p>}
                <ExecutionPanel o={o} canVerify={isDirector && executing} onUpdated={updated} />
              </div>
            ) : (
              <div className="space-y-3 text-sm">
                <p>A diretoria aprova o cenário, depois que as condições estiverem cumpridas.{o.risk === "High" ? " Risco alto exige que a decisão seja assumida expressamente." : ""}</p>
                <p className="text-xs text-muted-foreground">Quem pode aprovar: {APPROVAL_REQUIREMENTS[o.requiredApproval]}.</p>
                <p className="text-xs text-muted-foreground">
                  {o.materiality
                    ? `Materialidade em vigor: ${formatCurrency(o.materiality.threshold)} (versão ${o.materiality.version}, ${o.materiality.decisionReference}).`
                    : "Materialidade: sem versão própria da empresa — vale o padrão da instalação."}
                </p>
                {open && (
                  <div className="flex flex-wrap gap-2">
                    {o.status === "Identified" && (
                      <Button type="button" variant="outline" disabled={review.isPending} onClick={() => review.mutate()}>
                        {review.isPending ? "Iniciando…" : "Iniciar revisão"}
                      </Button>
                    )}
                    {isDirector && <Button type="button" onClick={() => setDialog("approve")}><Gavel className="mr-1.5 h-4 w-4" /> Aprovar</Button>}
                    <Button type="button" variant="outline" onClick={() => setDialog("reject")}>Recusar</Button>
                    <Button type="button" variant="outline" className="text-destructive" onClick={() => setDialog("block")}><Ban className="mr-1.5 h-4 w-4" /> Bloquear como estrutura proibida</Button>
                  </div>
                )}
                {open && !isDirector && <p className="text-xs text-muted-foreground">Só a diretoria vê o botão de aprovar.</p>}
              </div>
            )}
          </SectionCard>

          <Timeline id={o.id} />

          <ApproveDialog open={dialog === "approve"} o={o} onClose={() => setDialog(null)} onUpdated={updated} />
          <ReasonDialog kind={dialog === "reject" || dialog === "block" ? dialog : null} id={o.id} onClose={() => setDialog(null)} onUpdated={updated} />
        </>
      )}
    </div>
  );
}


/** How a requirement looks for each answer: met, not met, not answered yet. */
function answerLook(met: boolean | null) {
  if (met === true) return { Icon: CheckCircle2, color: "text-success" };
  if (met === false) return { Icon: XCircle, color: "text-destructive" };
  return { Icon: CircleHelp, color: "text-muted-foreground" };
}

function RequirementList({ o, phase, editable, onUpdated }: Readonly<{
  o: TaxOpportunityDto; phase: RequirementPhase; editable: boolean; onUpdated: (o: TaxOpportunityDto) => void;
}>) {
  const items = o.requirements.filter((r) => r.phase === phase);
  return (
    <ul className="space-y-3">
      {items.map((r) => <Requirement key={r.id} opportunityId={o.id} r={r} editable={editable} onUpdated={onUpdated} />)}
      {items.length === 0 && <li className="text-sm text-muted-foreground">Sem condições nesta fase.</li>}
    </ul>
  );
}

function Requirement({ opportunityId, r, editable, onUpdated }: Readonly<{ opportunityId: string; r: TaxRequirementDto; editable: boolean; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [note, setNote] = useState(r.note ?? "");
  const answer = useMutation({
    mutationFn: (met: boolean) => answerRequirement(opportunityId, r.id, met, note.trim() || null),
    onSuccess: onUpdated,
    onError: toastError,
  });
  const { Icon, color } = answerLook(r.met);

  return (
    <li className="rounded-md border border-border p-3 text-sm">
      <p className="flex items-start gap-2">
        <Icon className={`mt-0.5 h-4 w-4 shrink-0 ${color}`} />
        <span className="min-w-0">
          {r.text}
          {r.legalSourceCode && <span className="ml-2"><SourceLinks codes={[r.legalSourceCode]} /></span>}
        </span>
      </p>
      {r.requiredEvidence && (
        <p className={`mt-1 pl-6 text-xs ${r.hasRequiredEvidence ? "text-success" : "text-warning"}`}>
          {r.hasRequiredEvidence ? "Evidência vinculada" : "Falta evidência"}: {EVIDENCE_TYPES[r.requiredEvidence]}
        </p>
      )}
      {r.answeredByName && (
        <p className="mt-1 pl-6 text-xs text-muted-foreground">
          {r.met ? "Cumprida" : "Não cumprida"} — {r.answeredByName}{r.answeredAt ? ` em ${formatDateTime(r.answeredAt)}` : ""}{r.note ? `: ${r.note}` : ""}
        </p>
      )}
      {editable && (
        <div className="mt-2 flex flex-col gap-2 pl-6 sm:flex-row sm:items-center">
          <Input aria-label="Observação" placeholder="Observação (opcional)" maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} className="sm:max-w-sm" />
          <div className="flex gap-2">
            <Button type="button" size="sm" variant="outline" disabled={answer.isPending} onClick={() => answer.mutate(true)}>Cumprida</Button>
            <Button type="button" size="sm" variant="outline" disabled={answer.isPending} onClick={() => answer.mutate(false)}>Não cumprida</Button>
          </div>
        </div>
      )}
    </li>
  );
}

const NO_REQUIREMENT = "none";
const NOBODY = "__nobody";

/** Who moves the analysis forward — a work assignment, never an approval power. Only management can be made responsible. */
function Responsible({ o, canAssign, onUpdated }: Readonly<{ o: TaxOpportunityDto; canAssign: boolean; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const { session } = useAuth();
  const users = useQuery({ queryKey: ["users", "tax-responsible"], queryFn: listUsers, enabled: canAssign, staleTime: 60_000 });
  const assign = useMutation({
    mutationFn: (userId: string | null) => assignOpportunity(o.id, userId),
    onSuccess: (n) => { onUpdated(n); toast.success(n.assignedToName ? `Responsável: ${n.assignedToName}.` : "Responsável retirado."); },
    onError: toastError,
  });
  const candidates = (users.data ?? []).filter((u) => u.status.toLowerCase() === "active" && ["gerente", "diretoria"].includes(u.role.toLowerCase()));

  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      <UserRound className="h-4 w-4 text-muted-foreground" aria-hidden />
      <span className="text-muted-foreground">Responsável pela análise:</span>
      <span className="font-medium">{o.assignedToName ?? "ninguém"}</span>
      {canAssign && session && o.assignedToId !== session.userId && (
        <Button type="button" size="sm" variant="ghost" disabled={assign.isPending} onClick={() => assign.mutate(session.userId)}>Assumir</Button>
      )}
      {canAssign && candidates.length > 0 && (
        <Select value={o.assignedToId ?? NOBODY} onValueChange={(v) => assign.mutate(v === NOBODY ? null : v)} disabled={assign.isPending}>
          <SelectTrigger aria-label="Escolher o responsável" className="h-8 w-56"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={NOBODY}>Ninguém</SelectItem>
            {candidates.map((u) => <SelectItem key={u.id} value={u.id}>{u.fullName}</SelectItem>)}
          </SelectContent>
        </Select>
      )}
    </div>
  );
}

/** What happened to the opportunity, from this company's audit trail — loaded only when opened. */
function Timeline({ id }: Readonly<{ id: string }>) {
  const [open, setOpen] = useState(false);
  const events = useQuery({ queryKey: TAX_KEYS.timeline(id), queryFn: () => getOpportunityTimeline(id), enabled: open });
  return (
    <SectionCard title="Histórico">
      <details onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)}>
        <summary className="flex cursor-pointer items-center gap-2 text-sm font-medium"><History className="h-4 w-4" aria-hidden /> Ver o que aconteceu, em ordem</summary>
        <div className="mt-3">
          {events.isLoading && <Skeleton className="h-16 w-full" />}
          {events.isError && <TaxQueryError error={events.error} what="o histórico" onRetry={() => void events.refetch()} />}
          {events.data?.length === 0 && <p className="text-sm text-muted-foreground">Nada registrado ainda.</p>}
          {events.data && events.data.length > 0 && (
            <ol className="space-y-2 border-l border-border pl-4">
              {events.data.map((ev, i) => (
                <li key={`${ev.at}-${i}`} className="text-sm">
                  <p>{ev.description}</p>
                  <p className="text-xs text-muted-foreground">{ev.actorName} · {formatDateTime(ev.at)}</p>
                </li>
              ))}
            </ol>
          )}
        </div>
      </details>
    </SectionCard>
  );
}

const inForce = (e: TaxEvidenceDto) => e.status !== "Superseded" && e.status !== "Revoked";

/**
 * Evidence of the analysis: the versions in force first, superseded and revoked ones kept as history (never deleted).
 * With a file, the hash is the SHA-256 of its bytes; without one, it only fingerprints the reference.
 */
function Evidence({ o, editable, canReadRevoked, onUpdated }: Readonly<{
  o: TaxOpportunityDto; editable: boolean; canReadRevoked: boolean; onUpdated: (o: TaxOpportunityDto) => void;
}>) {
  const [reasonDialog, askReason] = useReasonDialog();
  const [replacing, setReplacing] = useState<TaxEvidenceDto | null>(null);
  const storage = useQuery({ queryKey: TAX_KEYS.storage, queryFn: getEvidenceStorage, staleTime: 60_000, enabled: editable });
  const review = useMutation({
    mutationFn: ({ id, accepted, notes }: { id: string; accepted: boolean; notes: string | null }) => reviewOpportunityEvidence(o.id, id, accepted, notes),
    onSuccess: (n) => { onUpdated(n); toast.success("Revisão da evidência registrada."); },
    onError: toastError,
  });
  const revoke = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => revokeOpportunityEvidence(o.id, id, reason),
    onSuccess: (n) => { onUpdated(n); toast.success("Evidência revogada — continua no histórico."); },
    onError: toastError,
  });
  const requirementText = (id: string | null) => o.requirements.find((r) => r.id === id)?.text;
  const current = o.evidence.filter(inForce);
  const history = o.evidence.filter((e) => !inForce(e));
  const filesAllowed = !!storage.data?.available;

  const item = (e: TaxEvidenceDto, isCurrent: boolean) => (
    <li key={e.id} className="flex gap-2 text-sm">
      <FileCheck2 className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
      <div className="min-w-0 flex-1 space-y-1">
        <p className="flex flex-wrap items-center gap-2">
          <span className="break-words">{e.description}{e.documentReference ? ` · ${e.documentReference}` : ""}</span>
          <ToneBadge label={labelOf(EVIDENCE_STATUS, e.status)} tone={toneOf(EVIDENCE_STATUS, e.status)} />
          {e.version > 1 && <span className="text-xs text-muted-foreground">versão {e.version}</span>}
        </p>
        <p className="text-xs text-muted-foreground">{EVIDENCE_TYPES[e.type]}{e.requirementId ? ` · para: ${requirementText(e.requirementId)}` : ""}</p>
        <p className="text-xs text-muted-foreground">{e.addedByName} em {formatDateTime(e.createdAt)}</p>
        {e.reviewNotes && <p className="text-xs text-muted-foreground">Revisão: {e.reviewNotes}{e.reviewedByName ? ` — ${e.reviewedByName}` : ""}</p>}
        {e.revokedAt && <p className="text-xs text-destructive">Revogada por {e.revokedByName} em {formatDateTime(e.revokedAt)}: {e.revocationReason}</p>}
        {e.file
          ? <StoredFileLine file={e.file} canReadRevoked={canReadRevoked} />
          : <p className="break-all font-mono text-xs text-muted-foreground">sem arquivo · impressão do registro {e.contentHash}</p>}
        {isCurrent && editable && e.status === "Submitted" && (
          <EvidenceReview e={e} pending={review.isPending} onReview={(accepted, notes) => review.mutate({ id: e.id, accepted, notes })} />
        )}
        {isCurrent && editable && (
          <div className="flex flex-wrap gap-1">
            <Button type="button" size="sm" variant="ghost" onClick={() => setReplacing(e)}>Substituir por versão nova</Button>
            <Button type="button" size="sm" variant="ghost" className="text-destructive" disabled={revoke.isPending} onClick={() => askReason({
              title: "Revogar evidência", description: "Ela deixa de sustentar o requisito, mas continua no histórico e na auditoria.",
              confirmLabel: "Revogar", danger: true, onConfirm: (reason) => revoke.mutate({ id: e.id, reason }),
            })}>Revogar</Button>
          </div>
        )}
      </div>
    </li>
  );

  return (
    <div className="space-y-3">
      {reasonDialog}
      {current.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma evidência em vigor.</p>}
      <ul className="space-y-3">{current.map((e) => item(e, true))}</ul>
      {history.length > 0 && (
        <details className="rounded-md border border-border">
          <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Versões anteriores e revogadas ({history.length})</summary>
          <ul className="space-y-3 border-t border-border p-3">{history.map((e) => item(e, false))}</ul>
        </details>
      )}
      {editable && <AddEvidence o={o} filesAllowed={filesAllowed} storageMessage={storage.data && !storage.data.available ? storage.data.message : null} onUpdated={onUpdated} />}
      {replacing && <ReplaceEvidenceDialog o={o} e={replacing} filesAllowed={filesAllowed} onClose={() => setReplacing(null)} onUpdated={onUpdated} />}
    </div>
  );
}

function AddEvidence({ o, filesAllowed, storageMessage, onUpdated }: Readonly<{
  o: TaxOpportunityDto; filesAllowed: boolean; storageMessage: string | null; onUpdated: (o: TaxOpportunityDto) => void;
}>) {
  const executing = o.status === "Approved";
  const linkable = o.requirements.filter((r) => r.phase === (executing ? "ExecutionVerification" : "ScenarioApproval"));
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [type, setType] = useState<EvidenceType>(executing ? "FiscalDocument" : "CommercialInstrument");
  const [requirementId, setRequirementId] = useState<string>(NO_REQUIREMENT);
  const add = useMutation({
    mutationFn: () => {
      const body = {
        description: description.trim(), documentReference: reference.trim() || null, type,
        requirementId: requirementId === NO_REQUIREMENT ? null : requirementId, notes: null,
      };
      return file ? uploadOpportunityEvidence(o.id, file, body) : addOpportunityEvidence(o.id, body);
    },
    onSuccess: (n) => { onUpdated(n); setDescription(""); setReference(""); setFile(null); toast.success(file ? "Evidência e arquivo guardados." : "Evidência registrada."); },
    onError: toastError,
  });

  return (
    <form className="grid gap-2 rounded-md border border-dashed border-border p-3 sm:grid-cols-2 lg:grid-cols-4 lg:items-end"
      onSubmit={(e) => { e.preventDefault(); if (description.trim()) add.mutate(); else toast.error("Descreva a evidência."); }}>
      <p className="text-sm font-medium sm:col-span-2 lg:col-span-4">Adicionar evidência</p>
      <Field id="ev-type" label="Tipo">
        <Select value={type} onValueChange={(v) => setType(v as EvidenceType)}>
          <SelectTrigger id="ev-type"><SelectValue /></SelectTrigger>
          <SelectContent>{(Object.keys(EVIDENCE_TYPES) as EvidenceType[]).map((k) => <SelectItem key={k} value={k}>{EVIDENCE_TYPES[k]}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
      <Field id="ev-req" label="Para o requisito">
        <Select value={requirementId} onValueChange={setRequirementId}>
          <SelectTrigger id="ev-req"><SelectValue /></SelectTrigger>
          <SelectContent>
            {!executing && <SelectItem value={NO_REQUIREMENT}>Nenhum em especial</SelectItem>}
            {linkable.map((r) => <SelectItem key={r.id} value={r.id}>{r.text}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      <Field id="ev-desc" label="Evidência"><Input id="ev-desc" maxLength={1000} placeholder="Ex.: pedido com o preço já reduzido" value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
      <Field id="ev-ref" label="Documento (opcional)"><Input id="ev-ref" maxLength={200} placeholder="Nº, chave ou referência" value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
      <div className="sm:col-span-2 lg:col-span-3">
        {filesAllowed ? <FilePicker id="ev-file" file={file} onChange={setFile} disabled={add.isPending} /> : <p className="text-xs text-warning">{storageMessage ?? "Envio de arquivos indisponível."}</p>}
        <p className="mt-1 text-xs text-muted-foreground">
          Com arquivo, o hash é o SHA-256 dos bytes e o arquivo fica privado. Sem arquivo, registre onde o documento está — o hash identifica só o registro.
        </p>
      </div>
      <Button type="submit" variant="outline" disabled={add.isPending}>{add.isPending ? "Registrando…" : "Registrar evidência"}</Button>
    </form>
  );
}

/** A newer version of a piece of evidence; the old one stays on record as superseded and the dossier points at the exact version. */
function ReplaceEvidenceDialog({ o, e, filesAllowed, onClose, onUpdated }: Readonly<{
  o: TaxOpportunityDto; e: TaxEvidenceDto; filesAllowed: boolean; onClose: () => void; onUpdated: (o: TaxOpportunityDto) => void;
}>) {
  const [description, setDescription] = useState(e.description);
  const [reference, setReference] = useState(e.documentReference ?? "");
  const [file, setFile] = useState<File | null>(null);
  const replace = useMutation({
    mutationFn: () => replaceOpportunityEvidence(o.id, e.id, description.trim(), reference.trim() || null, file),
    onSuccess: (n) => { onUpdated(n); toast.success("Versão nova registrada; a anterior ficou no histórico."); onClose(); },
    onError: toastError,
  });
  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Substituir evidência</DialogTitle>
          <DialogDescription>A versão {e.version} fica registrada como substituída; a nova volta para revisão.</DialogDescription>
        </DialogHeader>
        <form id="replace-form" className="space-y-3" onSubmit={(ev) => { ev.preventDefault(); if (description.trim()) replace.mutate(); }}>
          <Field id="rep-desc" label="Evidência"><Input id="rep-desc" maxLength={1000} value={description} onChange={(ev) => setDescription(ev.target.value)} /></Field>
          <Field id="rep-ref" label="Documento (opcional)"><Input id="rep-ref" maxLength={200} value={reference} onChange={(ev) => setReference(ev.target.value)} /></Field>
          {filesAllowed && <FilePicker id="rep-file" file={file} onChange={setFile} disabled={replace.isPending} />}
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="submit" form="replace-form" disabled={replace.isPending || !description.trim()}>{replace.isPending ? "Enviando…" : "Registrar versão nova"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Accepts, or rejects with a reason — a rejected evidence supports no requirement. */
function EvidenceReview({ e, pending, onReview }: Readonly<{
  e: TaxEvidenceDto; pending: boolean; onReview: (accepted: boolean, notes: string | null) => void;
}>) {
  const [reason, setReason] = useState("");
  return (
    <div className="flex flex-col gap-2 pt-1 sm:flex-row sm:items-center">
      <Input aria-label={`Motivo da recusa de ${e.description}`} placeholder="Motivo, se recusar" maxLength={1000} value={reason}
        onChange={(ev) => setReason(ev.target.value)} className="sm:max-w-xs" />
      <div className="flex gap-2">
        <Button type="button" size="sm" variant="outline" disabled={pending} onClick={() => onReview(true, null)}>Aceitar evidência</Button>
        <Button type="button" size="sm" variant="outline" disabled={pending || !reason.trim()} onClick={() => onReview(false, reason.trim())}>Recusar evidência</Button>
      </div>
    </div>
  );
}

/** After the operation: whether it happened as the scenario was approved. Never inferred from the approval. */
function ExecutionPanel({ o, canVerify, onUpdated }: Readonly<{ o: TaxOpportunityDto; canVerify: boolean; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [outcome, setOutcome] = useState<"Verified" | "Diverged">("Verified");
  const [documentRef, setDocumentRef] = useState("");
  const [protocol, setProtocol] = useState("");
  const [notes, setNotes] = useState("");
  const verify = useMutation({
    mutationFn: () => verifyOpportunityExecution(o.id, {
      outcome, fiscalDocumentReference: documentRef.trim() || null, authorizationProtocol: protocol.trim() || null, notes: notes.trim() || null,
    }),
    onSuccess: (n) => { onUpdated(n); toast.success("Verificação da execução registrada."); },
    onError: toastError,
  });

  if (o.execution) {
    const x = o.execution;
    return (
      <div className="space-y-1 rounded-md border border-border p-3">
        <p className="font-medium">{x.outcome === "Verified" ? "Execução verificada conforme o cenário aprovado" : "Execução divergiu do cenário aprovado"}</p>
        <p className="text-muted-foreground">{x.verifiedByName} em {formatDateTime(x.verifiedAt)}{x.fiscalDocumentReference ? ` · documento ${x.fiscalDocumentReference}` : ""}</p>
        {x.notes && <p className="text-muted-foreground">{x.notes}</p>}
        <p className="break-all font-mono text-xs text-muted-foreground">dossiê da execução {x.dossierHash}</p>
      </div>
    );
  }
  if (!canVerify) return <p className="text-xs text-muted-foreground">Verificação da execução pendente.</p>;
  return (
    <form className="grid gap-2 rounded-md border border-border p-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); verify.mutate(); }}>
      <p className="font-medium sm:col-span-2">Verificar a execução</p>
      <Field id="ex-outcome" label="Resultado">
        <Select value={outcome} onValueChange={(v) => setOutcome(v as "Verified" | "Diverged")}>
          <SelectTrigger id="ex-outcome"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="Verified">Aconteceu como aprovado</SelectItem>
            <SelectItem value="Diverged">Divergiu do aprovado</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field id="ex-doc" label="Documento fiscal (chave)"><Input id="ex-doc" maxLength={200} value={documentRef} onChange={(e) => setDocumentRef(e.target.value)} /></Field>
      <Field id="ex-prot" label="Protocolo de autorização"><Input id="ex-prot" maxLength={100} value={protocol} onChange={(e) => setProtocol(e.target.value)} /></Field>
      <Field id="ex-notes" label={outcome === "Diverged" ? "O que divergiu" : "Notas (opcional)"}><Input id="ex-notes" maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
      <div className="sm:col-span-2"><Button type="submit" disabled={verify.isPending}>{verify.isPending ? "Registrando…" : "Registrar verificação"}</Button></div>
    </form>
  );
}

/** Server code when whoever analysed alone tries to approve without a justification (segregation of duties). */
const SELF_APPROVAL_CODE = "tax_self_approval_requires_justification";
/** Where the policy requires a reviewer or an independent approver, no justification bypasses it. */
const INDEPENDENCE_CODES = new Set(["tax_approval_requires_independent_approver", "tax_approval_requires_reviewer"]);

function ApproveDialog({ open, o, onClose, onUpdated }: Readonly<{ open: boolean; o: TaxOpportunityDto; onClose: () => void; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [ack, setAck] = useState(false);
  const [needsJustification, setNeedsJustification] = useState(false);
  const [justification, setJustification] = useState("");
  const high = o.risk === "High";
  const approve = useMutation({
    mutationFn: () => approveOpportunity(o.id, high ? ack : false, needsJustification ? justification : null),
    onSuccess: (n) => { onUpdated(n); toast.success(SCENARIO_APPROVED_COPY); onClose(); },
    onError: (e) => {
      if (e instanceof ApiError && e.code === SELF_APPROVAL_CODE) setNeedsJustification(true);
      if (e instanceof ApiError && INDEPENDENCE_CODES.has(e.code ?? "")) setNeedsJustification(false);
      toastError(e);
    },
  });
  const pending = o.requirements.filter((r) => r.phase === "ScenarioApproval" && r.met !== true).length;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) { setAck(false); onClose(); } }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Aprovar o cenário</DialogTitle>
          <DialogDescription>
            A aprovação do cenário guarda um dossiê com o cálculo, as condições, as evidências e as fontes. {SCENARIO_APPROVED_COPY}
            A execução é verificada depois, com o documento fiscal da operação.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3 text-sm">
          <p className="flex flex-wrap items-center gap-2"><ImpactBadge nature={o.impactNature} /><RiskBadge risk={o.risk} /></p>
          <p className="text-xs text-muted-foreground">Quem pode aprovar: {APPROVAL_REQUIREMENTS[o.requiredApproval]}.</p>
          {pending > 0 && <p className="text-warning">{pending} {pending === 1 ? "condição ainda não está cumprida" : "condições ainda não estão cumpridas"}: o servidor vai recusar.</p>}
          {o.professionalReviewPending && (
            <p className="text-warning">
              {o.governance.mode === "ControlledPilot"
                ? "Revisão profissional pendente. No piloto controlado, a aprovação se apoia na aprovação interna das regras — decisão de governança, não parecer profissional — e as ressalvas vão para o dossiê."
                : "Revisão profissional pendente: neste modo, a aprovação só é aceita depois que as regras forem revisadas por um profissional."}
            </p>
          )}
          {needsJustification && (
            <Field id="self-approval" label="Por que você aprova a própria análise?" hint="Fica registrado no dossiê e na auditoria. O ideal é outra pessoa revisar.">
              <Textarea id="self-approval" rows={2} maxLength={1000} value={justification} onChange={(e) => setJustification(e.target.value)} />
            </Field>
          )}
          {high && (
            <label htmlFor="ack-high" className="flex items-start gap-2 rounded-md border border-destructive/40 p-2">
              <Checkbox id="ack-high" checked={ack} onCheckedChange={(c) => setAck(c === true)} className="mt-0.5" />
              <span>Entendo o risco alto e assumo a decisão</span>
            </label>
          )}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={approve.isPending || (high && !ack) || (needsJustification && !justification.trim())} onClick={() => approve.mutate()}>{approve.isPending ? "Aprovando…" : "Aprovar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ReasonDialog({ kind, id, onClose, onUpdated }: Readonly<{ kind: "reject" | "block" | null; id: string; onClose: () => void; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [reason, setReason] = useState("");
  const [structure, setStructure] = useState<ProhibitedStructure | "">("");
  const [touched, setTouched] = useState(false);
  const problems = [
    ...(!reason.trim() ? ["Diga o motivo."] : []),
    ...(kind === "block" && !structure ? ["Escolha a estrutura proibida."] : []),
  ];
  const close = () => { setReason(""); setStructure(""); setTouched(false); onClose(); };
  const act = useMutation({
    mutationFn: () => (kind === "block" ? blockOpportunity(id, structure as ProhibitedStructure, reason.trim()) : rejectOpportunity(id, reason.trim())),
    onSuccess: (n) => { onUpdated(n); toast.success(kind === "block" ? "Oportunidade bloqueada." : "Oportunidade recusada."); close(); },
    onError: toastError,
  });

  return (
    <Dialog open={kind !== null} onOpenChange={(v) => { if (!v) close(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{kind === "block" ? "Bloquear como estrutura proibida" : "Recusar oportunidade"}</DialogTitle>
          <DialogDescription>
            {kind === "block" ? "Bloqueada, a oportunidade deixa de mostrar economia e não pode mais ser aprovada." : "A recusa fica registrada com o motivo."}
          </DialogDescription>
        </DialogHeader>
        <form id="reason-form" className="space-y-3" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) act.mutate(); }}>
          {kind === "block" && (
            <Field id="block-structure" label="Estrutura">
              <Select value={structure || undefined} onValueChange={(v) => setStructure(v as ProhibitedStructure)}>
                <SelectTrigger id="block-structure"><SelectValue placeholder="Escolha" /></SelectTrigger>
                <SelectContent>{(Object.keys(PROHIBITED_STRUCTURES) as ProhibitedStructure[]).map((s) => <SelectItem key={s} value={s}>{PROHIBITED_STRUCTURES[s]}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          )}
          <Field id="reason-text" label="Motivo"><Textarea id="reason-text" rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
          {touched && <ProblemList problems={problems} />}
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={close}>Cancelar</Button>
          <Button type="submit" form="reason-form" variant={kind === "block" ? "destructive" : "default"} disabled={act.isPending}>
            {kind === "block" ? "Bloquear" : "Recusar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
