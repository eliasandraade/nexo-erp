import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Ban, CheckCircle2, CircleHelp, FileCheck2, Gavel, XCircle } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
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
import {
  TAX_KEY, TAX_KEYS, addOpportunityEvidence, answerRequirement, approveOpportunity, blockOpportunity, getTaxOpportunity,
  rejectOpportunity, startOpportunityReview, type ProhibitedStructure, type TaxOpportunityDto, type TaxRequirementDto,
} from "../api/tax.api";
import { IMPACT_NATURES, OPPORTUNITY_TYPES, PROHIBITED_STRUCTURES, labelOf } from "../lib/labels";
import { errorMessage } from "../lib/forms";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge } from "../components/TaxBadges";
import { SourceLinks } from "../components/EngineResult";
import { ScenarioCompare } from "../components/ScenarioCompare";
import { Field, ProblemList, TaxSubNav } from "../components/TaxShell";

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
  const failed = (e: unknown) => toast.error(errorMessage(e));
  const review = useMutation({ mutationFn: () => startOpportunityReview(id), onSuccess: (n) => { updated(n); toast.success("Revisão iniciada."); }, onError: failed });

  const open = o && (o.status === "Identified" || o.status === "InReview");

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária · oportunidade"
        title={o?.title ?? "Oportunidade"}
        actions={<Button asChild variant="outline"><Link to="/tributos/oportunidades"><ArrowLeft className="mr-1.5 h-4 w-4" /> Oportunidades</Link></Button>}
      />
      <TaxSubNav />

      {query.isLoading && <Skeleton className="h-64 w-full" />}
      {query.isError && <ErrorState title="Não foi possível abrir a oportunidade" onRetry={() => void query.refetch()} />}
      {o && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <ImpactBadge nature={o.impactNature} />
            <RiskBadge risk={o.risk} />
            <OpportunityStatusBadge status={o.status} />
            <span className="text-xs text-muted-foreground">{labelOf(OPPORTUNITY_TYPES, o.type)} · confiança {o.confidence}%</span>
          </div>

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
            </div>
          </SectionCard>

          <SectionCard title="O que preciso fazer?" description="Cada condição precisa estar cumprida, com evidência, antes da aprovação.">
            <ul className="space-y-3">
              {o.requirements.map((r) => <Requirement key={r.id} opportunityId={o.id} r={r} editable={!!open} onUpdated={updated} />)}
              {o.requirements.length === 0 && <li className="text-sm text-muted-foreground">Sem condições cadastradas.</li>}
            </ul>
            <Evidence o={o} editable={!!open} onUpdated={updated} />
          </SectionCard>

          <SectionCard title="Quem precisa aprovar?">
            {o.decision ? (
              <div className="space-y-2 text-sm">
                <p className="flex items-center gap-2 font-medium text-success"><CheckCircle2 className="h-4 w-4" /> Aprovada por {o.decision.approvedByName} em {formatDateTime(o.decision.approvedAt)}</p>
                <p className="text-muted-foreground">Estratégia: {o.decision.strategy}{o.decision.highRiskAcknowledged ? " · risco alto assumido" : ""}</p>
                <p className="break-all font-mono text-xs text-muted-foreground">dossiê {o.decision.dossierHash} · motor {o.decision.engineVersion}</p>
                <details className="rounded-md border border-border">
                  <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Dossiê da decisão</summary>
                  <pre className="max-h-96 overflow-auto border-t border-border p-3 text-xs">{JSON.stringify(o.decision.dossier, null, 2)}</pre>
                </details>
              </div>
            ) : (
              <div className="space-y-3 text-sm">
                <p>A diretoria aprova, depois que as condições estiverem cumpridas.{o.risk === "High" ? " Risco alto exige que a decisão seja assumida expressamente." : ""}</p>
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

          <ApproveDialog open={dialog === "approve"} o={o} onClose={() => setDialog(null)} onUpdated={updated} />
          <ReasonDialog kind={dialog === "reject" || dialog === "block" ? dialog : null} id={o.id} onClose={() => setDialog(null)} onUpdated={updated} />
        </>
      )}
    </div>
  );
}

function Requirement({ opportunityId, r, editable, onUpdated }: Readonly<{ opportunityId: string; r: TaxRequirementDto; editable: boolean; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [note, setNote] = useState(r.note ?? "");
  const answer = useMutation({
    mutationFn: (met: boolean) => answerRequirement(opportunityId, r.id, met, note.trim() || null),
    onSuccess: onUpdated,
    onError: (e) => toast.error(errorMessage(e)),
  });
  const Icon = r.met === true ? CheckCircle2 : r.met === false ? XCircle : CircleHelp;
  const color = r.met === true ? "text-success" : r.met === false ? "text-destructive" : "text-muted-foreground";

  return (
    <li className="rounded-md border border-border p-3 text-sm">
      <p className="flex items-start gap-2">
        <Icon className={`mt-0.5 h-4 w-4 shrink-0 ${color}`} />
        <span className="min-w-0">
          {r.text}
          {r.legalSourceCode && <span className="ml-2"><SourceLinks codes={[r.legalSourceCode]} /></span>}
        </span>
      </p>
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

function Evidence({ o, editable, onUpdated }: Readonly<{ o: TaxOpportunityDto; editable: boolean; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const add = useMutation({
    mutationFn: () => addOpportunityEvidence(o.id, description.trim(), reference.trim() || null),
    onSuccess: (n) => { onUpdated(n); setDescription(""); setReference(""); toast.success("Evidência registrada."); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <div className="mt-5 space-y-3">
      <h4 className="text-sm font-semibold">Evidências</h4>
      {o.evidence.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma evidência registrada.</p>}
      <ul className="space-y-2">
        {o.evidence.map((e) => (
          <li key={e.id} className="flex gap-2 text-sm">
            <FileCheck2 className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
            <div className="min-w-0">
              <p className="break-words">{e.description}{e.documentReference ? ` · ${e.documentReference}` : ""}</p>
              <p className="text-xs text-muted-foreground">{e.addedByName} em {formatDateTime(e.createdAt)} · <span className="break-all font-mono">{e.contentHash}</span></p>
            </div>
          </li>
        ))}
      </ul>
      {editable && (
        <form className="grid gap-2 sm:grid-cols-[1fr_14rem_auto] sm:items-end" onSubmit={(e) => { e.preventDefault(); if (description.trim()) add.mutate(); else toast.error("Descreva a evidência."); }}>
          <Field id="ev-desc" label="Evidência"><Input id="ev-desc" maxLength={1000} placeholder="Ex.: contrato com cláusula de desconto na nota" value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
          <Field id="ev-ref" label="Documento (opcional)"><Input id="ev-ref" maxLength={200} placeholder="Nº, chave ou arquivo" value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
          <Button type="submit" variant="outline" disabled={add.isPending}>{add.isPending ? "Registrando…" : "Registrar evidência"}</Button>
        </form>
      )}
    </div>
  );
}

function ApproveDialog({ open, o, onClose, onUpdated }: Readonly<{ open: boolean; o: TaxOpportunityDto; onClose: () => void; onUpdated: (o: TaxOpportunityDto) => void }>) {
  const [ack, setAck] = useState(false);
  const high = o.risk === "High";
  const approve = useMutation({
    mutationFn: () => approveOpportunity(o.id, high ? ack : false),
    onSuccess: (n) => { onUpdated(n); toast.success("Oportunidade aprovada. Dossiê da decisão guardado."); onClose(); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const pending = o.requirements.filter((r) => r.met !== true).length;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) { setAck(false); onClose(); } }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Aprovar oportunidade</DialogTitle>
          <DialogDescription>
            A aprovação guarda um dossiê com o cálculo, as condições, as evidências e as fontes. Ela não altera vendas nem obrigações sozinha.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3 text-sm">
          <p className="flex flex-wrap items-center gap-2"><ImpactBadge nature={o.impactNature} /><RiskBadge risk={o.risk} /></p>
          {pending > 0 && <p className="text-warning">{pending} {pending === 1 ? "condição ainda não está cumprida" : "condições ainda não estão cumpridas"}: o servidor vai recusar.</p>}
          {high && (
            <label htmlFor="ack-high" className="flex items-start gap-2 rounded-md border border-destructive/40 p-2">
              <Checkbox id="ack-high" checked={ack} onCheckedChange={(c) => setAck(c === true)} className="mt-0.5" />
              <span>Entendo o risco alto e assumo a decisão</span>
            </label>
          )}
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="button" disabled={approve.isPending || (high && !ack)} onClick={() => approve.mutate()}>{approve.isPending ? "Aprovando…" : "Aprovar"}</Button>
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
    onError: (e) => toast.error(errorMessage(e)),
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
