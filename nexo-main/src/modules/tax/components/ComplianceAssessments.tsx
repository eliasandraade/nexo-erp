import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TAX_KEY, recordComplianceAssessment, type ComplianceAssessmentStatus, type TaxComplianceAssessmentDto } from "../api/tax.api";
import { COMPLIANCE_STATUS, labelOf, toneOf } from "../lib/labels";
import { complianceProblems, emptyCompliance, errorMessage, formatIsoDate, toComplianceRequest, type ComplianceDraft } from "../lib/forms";
import { ToneBadge } from "./TaxBadges";
import { Field, ProblemList, TaxNote } from "./TaxShell";

const STATUS_OPTIONS = Object.entries(COMPLIANCE_STATUS) as [ComplianceAssessmentStatus, { label: string }][];

/**
 * Avaliações das obrigações acessórias do IBS/CBS, por período e escopo. Nunca diz "empresa conforme":
 * só "verificadas no período e no escopo indicados" — e só "Verificado" conta para as regras.
 */
export function ComplianceAssessments({ items, canRecord }: Readonly<{ items: TaxComplianceAssessmentDto[]; canRecord: boolean }>) {
  return (
    <SectionCard
      title="Obrigações acessórias do IBS/CBS"
      description="Cada avaliação vale para um período e um escopo. Declarar, regularizar ou participar do PNCT não é o mesmo que verificar."
    >
      {items.length === 0
        ? <p className="text-sm text-muted-foreground">Nenhuma avaliação registrada: nenhuma conclusão sobre as obrigações acessórias é aplicada.</p>
        : <ul className="space-y-3">{items.map((a) => <Assessment key={a.id} a={a} />)}</ul>}
      {canRecord ? <AssessmentForm /> : <TaxNote>Só a diretoria registra uma avaliação.</TaxNote>}
    </SectionCard>
  );
}

function Assessment({ a }: Readonly<{ a: TaxComplianceAssessmentDto }>) {
  const s = COMPLIANCE_STATUS[a.status];
  return (
    <li className="rounded-md border border-border p-3 text-sm">
      <p className="flex flex-wrap items-center gap-2">
        <ToneBadge label={labelOf(COMPLIANCE_STATUS, a.status)} tone={toneOf(COMPLIANCE_STATUS, a.status)} title={s.hint} />
        <span>{formatIsoDate(a.periodFrom)} a {formatIsoDate(a.periodTo)}</span>
        {a.pnctParticipation && <ToneBadge label="Participa do PNCT — não é regularidade integral" tone="warning" />}
      </p>
      {a.status === "Verified" && <p className="mt-1">Obrigações verificadas no período e no escopo indicados.</p>}
      <p className="mt-1 text-muted-foreground">Escopo: {a.scope || "—"} · avaliado por {a.assessedBy}</p>
      {a.obligationsChecked.length > 0 && <p className="text-muted-foreground">Conferido: {a.obligationsChecked.join("; ")}</p>}
      {a.evidence && <p className="text-muted-foreground">Evidência: {a.evidence}</p>}
      {a.limitations && <p className="text-muted-foreground">Limitações: {a.limitations}</p>}
      {a.notes && <p className="text-muted-foreground">Notas: {a.notes}</p>}
    </li>
  );
}

function AssessmentForm() {
  const qc = useQueryClient();
  const [draft, setDraft] = useState<ComplianceDraft>(emptyCompliance);
  const [touched, setTouched] = useState(false);
  const problems = complianceProblems(draft);
  const save = useMutation({
    mutationFn: () => recordComplianceAssessment(toComplianceRequest(draft)),
    onSuccess: () => {
      toast.success("Avaliação registrada. As anteriores ficam guardadas.");
      setDraft(emptyCompliance());
      setTouched(false);
      void qc.invalidateQueries({ queryKey: TAX_KEY });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const text = (k: keyof ComplianceDraft) => ({
    value: draft[k] as string,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft({ ...draft, [k]: e.target.value }),
  });

  return (
    <form
      className="mt-4 grid gap-3 border-t border-border pt-4 sm:grid-cols-2 lg:grid-cols-4"
      onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}
      aria-label="Registrar avaliação das obrigações acessórias"
    >
      <Field id="ca-status" label="Situação" hint={COMPLIANCE_STATUS[draft.status].hint} className="sm:col-span-2">
        <Select value={draft.status} onValueChange={(v) => setDraft({ ...draft, status: v as ComplianceAssessmentStatus })}>
          <SelectTrigger id="ca-status"><SelectValue /></SelectTrigger>
          <SelectContent>{STATUS_OPTIONS.map(([value, l]) => <SelectItem key={value} value={value}>{l.label}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
      <Field id="ca-from" label="Período: de"><Input id="ca-from" type="date" {...text("periodFrom")} /></Field>
      <Field id="ca-to" label="até"><Input id="ca-to" type="date" {...text("periodTo")} /></Field>
      <Field id="ca-scope" label="Escopo" hint="O que a avaliação cobre (estabelecimento, documentos…)." className="sm:col-span-2">
        <Input id="ca-scope" maxLength={500} {...text("scope")} />
      </Field>
      <Field id="ca-by" label="Quem avaliou" className="sm:col-span-2"><Input id="ca-by" maxLength={200} {...text("assessedBy")} /></Field>
      <Field id="ca-checked" label="Obrigações conferidas" hint="Uma por linha." className="sm:col-span-2">
        <Textarea id="ca-checked" rows={2} {...text("obligationsChecked")} />
      </Field>
      <Field id="ca-evidence" label="Evidência" hint="O que sustenta a avaliação. Quais documentos bastam depende de revisão profissional." className="sm:col-span-2">
        <Textarea id="ca-evidence" rows={2} maxLength={2000} {...text("evidence")} />
      </Field>
      <Field id="ca-limits" label="Limitações" className="sm:col-span-2"><Textarea id="ca-limits" rows={2} maxLength={2000} {...text("limitations")} /></Field>
      <Field id="ca-notes" label="Notas" className="sm:col-span-2"><Textarea id="ca-notes" rows={2} maxLength={2000} {...text("notes")} /></Field>
      <label htmlFor="ca-pnct" className="flex items-start gap-2 text-sm sm:col-span-2 lg:col-span-4">
        <Checkbox id="ca-pnct" checked={draft.pnctParticipation} onCheckedChange={(c) => setDraft({ ...draft, pnctParticipation: c === true })} className="mt-0.5" />
        <span>Participa do Programa Nacional de Conformidade Tributária (registro apenas — não significa regularidade integral)</span>
      </label>
      <div className="sm:col-span-2 lg:col-span-4">{touched && <ProblemList problems={problems} />}</div>
      <div className="sm:col-span-2 lg:col-span-4">
        <Button type="submit" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar avaliação"}</Button>
      </div>
    </form>
  );
}
