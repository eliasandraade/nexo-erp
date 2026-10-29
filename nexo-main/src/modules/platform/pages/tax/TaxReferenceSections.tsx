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
import { formatDateTime } from "@/lib/formatters";
import type { ClassificationStatus, EvidenceType, ProfessionalReviewStatus, RetentionStart } from "@/modules/tax/api/tax.api";
import {
  CLASSIFICATION_STATUS, EVIDENCE_TYPES, FILE_STATUS, PROFESSIONAL_REVIEW, RETENTION_START, TEXT_VERIFICATION, labelOf, refusalTitle, toneOf,
} from "@/modules/tax/lib/labels";
import { formatIsoDate } from "@/modules/tax/lib/forms";
import { ToneBadge } from "@/modules/tax/components/TaxBadges";
import { Field, ProblemList } from "@/modules/tax/components/TaxShell";
import { DownloadButton, FilePicker, TaxPager, TaxQueryError, toastError, useReasonDialog } from "@/modules/tax/components/TaxCommon";
import {
  PLATFORM_TAX_KEY, PLATFORM_TAX_KEYS, changeClassificationStatus, configurePlatformRetention, createLegalSource, defineObligation, documentUrl,
  getRefusals, getTaxAudit, importClassifications, listDocuments, listLegalSources, listPlatformClassifications, listPlatformObligations,
  listPlatformRetention, recordClassificationReview, revokeDocument, setObligationActive, uploadDocument, verifyLegalSource,
  parseClassificationLines, type ClassificationFilter, type DocumentPurpose,
} from "../../services/taxAdminApi";

const useRefresh = () => {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY });
};

// ── Legal sources ────────────────────────────────────────────────────────────

const SOURCE_KINDS: Record<string, string> = {
  Constitution: "Constituição", ConstitutionalAmendment: "Emenda constitucional", ComplementaryLaw: "Lei complementar", OrdinaryLaw: "Lei ordinária",
  Decree: "Decreto", FederalRevenueNormative: "Norma da Receita Federal", IbsManagementCommittee: "Comitê Gestor do IBS",
  StateLegislation: "Legislação estadual", MunicipalLegislation: "Legislação municipal", Other: "Outra",
};

/** Official sources: the excerpt is compared with the official text by a person — the verification says the words match, nothing more. */
export function TaxSourcesSection() {
  const sources = useQuery({ queryKey: PLATFORM_TAX_KEYS.sources, queryFn: listLegalSources });
  const refresh = useRefresh();
  const [verifying, setVerifying] = useState<string | null>(null);
  const [verifiedBy, setVerifiedBy] = useState("");
  const verify = useMutation({
    mutationFn: (code: string) => verifyLegalSource(code, verifiedBy.trim()),
    onSuccess: () => { toast.success("Conferência do texto registrada."); setVerifying(null); setVerifiedBy(""); refresh(); },
    onError: toastError,
  });
  return (
    <div className="space-y-6">
      <SectionCard title="Fontes jurídicas" description="Conferir o texto não é parecer jurídico: diz apenas que o trecho reproduz o texto oficial.">
        {sources.isLoading && <Skeleton className="h-32 w-full" />}
        {sources.isError && <TaxQueryError error={sources.error} what="as fontes" onRetry={() => void sources.refetch()} />}
        <ul className="space-y-3">
          {sources.data?.map((s) => (
            <li key={s.id} className="rounded-md border border-border p-3 text-sm">
              <p className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-xs">{s.code}</span>
                <span className="font-medium">{s.title}</span>
                <ToneBadge label={labelOf(TEXT_VERIFICATION, s.textVerification)} tone={toneOf(TEXT_VERIFICATION, s.textVerification)} />
                <span className="text-xs text-muted-foreground">{SOURCE_KINDS[s.kind] ?? s.kind} · {s.reference} · {s.rulesUsing} regra(s)</span>
              </p>
              <a href={s.url} target="_blank" rel="noopener noreferrer" className="break-all text-xs text-primary hover:underline">{s.url}</a>
              <details className="mt-1"><summary className="cursor-pointer text-xs">Trecho</summary><p className="mt-1 whitespace-pre-wrap text-xs">{s.excerpt}</p></details>
              {s.verifiedBy && (
                <p className="text-xs text-muted-foreground">
                  Conferido por {s.verifiedBy}{s.verifiedAt ? ` em ${formatDateTime(s.verifiedAt)}` : ""}{s.verificationRecordedBy ? ` · registrado por ${s.verificationRecordedBy}` : ""}
                </p>
              )}
              {s.textVerification !== "Verified" && (verifying === s.code ? (
                <form className="mt-2 flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); if (verifiedBy.trim()) verify.mutate(s.code); }}>
                  <Field id={`vs-${s.code}`} label="Quem comparou com o texto oficial"><Input id={`vs-${s.code}`} maxLength={200} value={verifiedBy} onChange={(e) => setVerifiedBy(e.target.value)} /></Field>
                  <Button type="submit" size="sm" disabled={!verifiedBy.trim() || verify.isPending}>Registrar conferência</Button>
                  <Button type="button" size="sm" variant="ghost" onClick={() => setVerifying(null)}>Cancelar</Button>
                </form>
              ) : <Button type="button" size="sm" variant="outline" className="mt-2" onClick={() => setVerifying(s.code)}>Conferir texto…</Button>)}
            </li>
          ))}
        </ul>
      </SectionCard>
      <NewSourceForm />
    </div>
  );
}

function NewSourceForm() {
  const refresh = useRefresh();
  const [d, setD] = useState({ code: "", kind: "ComplementaryLaw", title: "", reference: "", url: "", excerpt: "", publishedOn: "", notes: "" });
  const [touched, setTouched] = useState(false);
  const problems = [
    ...(/^[A-Za-z0-9-]{1,60}$/.test(d.code) ? [] : ["Código: letras, números e hífen."]),
    ...(d.title.trim() ? [] : ["Informe o título."]),
    ...(d.reference.trim() ? [] : ["Informe a referência (artigo, inciso…)."]),
    ...(/^https:\/\//.test(d.url) ? [] : ["Informe o link oficial (https)."]),
    ...(d.excerpt.trim() ? [] : ["Cole o trecho do texto oficial."]),
  ];
  const create = useMutation({
    mutationFn: () => createLegalSource({ ...d, publishedOn: d.publishedOn || null, notes: d.notes.trim() || null }),
    onSuccess: () => { toast.success("Fonte cadastrada — ainda não conferida."); setD({ ...d, code: "", title: "", reference: "", url: "", excerpt: "", notes: "" }); setTouched(false); refresh(); },
    onError: toastError,
  });
  const text = (k: keyof typeof d) => ({ value: d[k], onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setD({ ...d, [k]: e.target.value }) });
  return (
    <SectionCard title="Cadastrar fonte" description="Entra como não conferida. Nenhuma regra com fonte não conferida pode ser ativada.">
      <form className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) create.mutate(); }}>
        <Field id="ns-code" label="Código"><Input id="ns-code" maxLength={60} {...text("code")} /></Field>
        <Field id="ns-kind" label="Tipo">
          <Select value={d.kind} onValueChange={(v) => setD({ ...d, kind: v })}>
            <SelectTrigger id="ns-kind"><SelectValue /></SelectTrigger>
            <SelectContent>{Object.entries(SOURCE_KINDS).map(([k, l]) => <SelectItem key={k} value={k}>{l}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id="ns-ref" label="Referência"><Input id="ns-ref" maxLength={200} {...text("reference")} /></Field>
        <Field id="ns-pub" label="Publicação (opcional)"><Input id="ns-pub" type="date" {...text("publishedOn")} /></Field>
        <Field id="ns-title" label="Título" className="sm:col-span-2"><Input id="ns-title" maxLength={300} {...text("title")} /></Field>
        <Field id="ns-url" label="Link oficial" className="sm:col-span-2"><Input id="ns-url" maxLength={500} {...text("url")} /></Field>
        <Field id="ns-excerpt" label="Trecho do texto oficial" className="sm:col-span-2 lg:col-span-4"><Textarea id="ns-excerpt" rows={4} maxLength={20000} {...text("excerpt")} /></Field>
        <Field id="ns-notes" label="Notas (opcional)" className="sm:col-span-2 lg:col-span-4"><Input id="ns-notes" maxLength={1000} {...text("notes")} /></Field>
        <div className="sm:col-span-2 lg:col-span-4">{touched && <ProblemList problems={problems} />}</div>
        <div className="sm:col-span-2 lg:col-span-4"><Button type="submit" disabled={create.isPending}>Cadastrar fonte</Button></div>
      </form>
    </SectionCard>
  );
}

// ── Classification catalogue ────────────────────────────────────────────────

const ALL = "__all";
const REVIEW_STATUSES: Exclude<ProfessionalReviewStatus, "Pending">[] = ["ReviewReceived", "Approved", "ApprovedWithCaveats", "ChangesRequired", "Rejected"];

/** cClassTrib catalogue: imported from a documented source as Draft, activated explicitly, reviewed apart. Fail-closed for anything not Active. */
export function TaxClassificationsSection() {
  const [filter, setFilter] = useState<ClassificationFilter>({ kind: "CClassTrib", status: "", search: "", skip: 0, take: 50 });
  const list = useQuery({ queryKey: PLATFORM_TAX_KEYS.classifications(filter), queryFn: () => listPlatformClassifications(filter) });
  const refresh = useRefresh();
  const [reasonDialog, askReason] = useReasonDialog();
  const [reviewing, setReviewing] = useState<string | null>(null);
  const [reviewStatus, setReviewStatus] = useState<Exclude<ProfessionalReviewStatus, "Pending">>("ReviewReceived");
  const [reviewNotes, setReviewNotes] = useState("");
  const status = useMutation({
    mutationFn: ({ id, to, reason }: { id: string; to: ClassificationStatus; reason: string }) => changeClassificationStatus(id, to, reason),
    onSuccess: () => { toast.success("Situação do código alterada."); refresh(); },
    onError: toastError,
  });
  const review = useMutation({
    mutationFn: (id: string) => recordClassificationReview(id, reviewStatus, reviewNotes.trim()),
    onSuccess: () => { toast.success("Revisão registrada."); setReviewing(null); setReviewNotes(""); refresh(); },
    onError: toastError,
  });

  return (
    <div className="space-y-6">
      {reasonDialog}
      <SectionCard title="Catálogo de classificação" description="Só códigos ativos na data da operação são aceitos pela matriz de cobertura; no modo definitivo, também precisam de revisão profissional aprovada.">
        <div className="mb-3 flex flex-wrap items-end gap-3">
          <Field id="cc-search" label="Buscar" className="w-full sm:w-64">
            <Input id="cc-search" value={filter.search ?? ""} onChange={(e) => setFilter({ ...filter, search: e.target.value, skip: 0 })} />
          </Field>
          <Field id="cc-status" label="Situação" className="w-full sm:w-44">
            <Select value={filter.status || ALL} onValueChange={(v) => setFilter({ ...filter, status: v === ALL ? "" : (v as ClassificationStatus), skip: 0 })}>
              <SelectTrigger id="cc-status"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Todas</SelectItem>
                {(Object.keys(CLASSIFICATION_STATUS) as ClassificationStatus[]).map((s) => <SelectItem key={s} value={s}>{CLASSIFICATION_STATUS[s].label}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
        </div>
        {list.isLoading && <Skeleton className="h-32 w-full" />}
        {list.isError && <TaxQueryError error={list.error} what="o catálogo" onRetry={() => void list.refetch()} />}
        {list.data?.items.length === 0 && <p className="text-sm text-muted-foreground">Nenhum código com estes filtros.</p>}
        <ul className="space-y-2">
          {list.data?.items.map((c) => (
            <li key={c.id} className="rounded-md border border-border p-2 text-sm">
              <p className="flex flex-wrap items-center gap-2">
                <span className="font-mono">{c.code}</span> <span>{c.description}</span>
                <ToneBadge label={labelOf(CLASSIFICATION_STATUS, c.status)} tone={toneOf(CLASSIFICATION_STATUS, c.status)} />
                <ToneBadge label={labelOf(PROFESSIONAL_REVIEW, c.professionalReview)} tone={toneOf(PROFESSIONAL_REVIEW, c.professionalReview)} />
              </p>
              <p className="text-xs text-muted-foreground">
                {c.source}{c.sourceVersion ? ` ${c.sourceVersion}` : ""} · {formatIsoDate(c.validFrom)}{c.validTo ? ` a ${formatIsoDate(c.validTo)}` : ""} · importado por {c.createdByName}
                {c.statusChangedByName ? ` · situação por ${c.statusChangedByName} em ${formatDateTime(c.statusChangedAt ?? "")}` : ""}
              </p>
              {c.professionalReviewNotes && <p className="text-xs text-muted-foreground">Revisão: {c.professionalReviewNotes}</p>}
              <div className="mt-1 flex flex-wrap gap-1">
                {c.status !== "Active" && (
                  <Button type="button" size="sm" variant="outline" onClick={() => askReason({
                    title: `Ativar ${c.code}`, description: "Empresas passam a poder usar o código a partir de agora.", confirmLabel: "Ativar",
                    onConfirm: (reason) => status.mutate({ id: c.id, to: "Active", reason }),
                  })}>Ativar</Button>
                )}
                {c.status === "Active" && (
                  <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={() => askReason({
                    title: `Inativar ${c.code}`, description: "Operações com este código ficam fora do recorte a partir de agora.", confirmLabel: "Inativar", danger: true,
                    onConfirm: (reason) => status.mutate({ id: c.id, to: "Inactive", reason }),
                  })}>Inativar</Button>
                )}
                <Button type="button" size="sm" variant="ghost" onClick={() => setReviewing(reviewing === c.id ? null : c.id)}>Registrar revisão recebida…</Button>
              </div>
              {reviewing === c.id && (
                <form className="mt-2 grid gap-2 sm:grid-cols-3" onSubmit={(e) => { e.preventDefault(); if (reviewNotes.trim()) review.mutate(c.id); }}>
                  <Field id={`cr-st-${c.id}`} label="Resultado">
                    <Select value={reviewStatus} onValueChange={(v) => setReviewStatus(v as typeof reviewStatus)}>
                      <SelectTrigger id={`cr-st-${c.id}`}><SelectValue /></SelectTrigger>
                      <SelectContent>{REVIEW_STATUSES.map((s) => <SelectItem key={s} value={s}>{PROFESSIONAL_REVIEW[s].label}</SelectItem>)}</SelectContent>
                    </Select>
                  </Field>
                  <Field id={`cr-n-${c.id}`} label="Profissional informado e o que disse" className="sm:col-span-2">
                    <Input id={`cr-n-${c.id}`} maxLength={2000} value={reviewNotes} onChange={(e) => setReviewNotes(e.target.value)} />
                  </Field>
                  <p className="text-xs text-muted-foreground sm:col-span-2">Registre apenas uma revisão de fato recebida. A sua sessão fica gravada como quem registrou.</p>
                  <Button type="submit" size="sm" disabled={!reviewNotes.trim() || review.isPending}>Registrar</Button>
                </form>
              )}
            </li>
          ))}
        </ul>
        {list.data && <TaxPager total={list.data.total} skip={filter.skip ?? 0} take={50} onSkip={(skip) => setFilter({ ...filter, skip })} />}
      </SectionCard>
      <ImportForm />
    </div>
  );
}

function ImportForm() {
  const refresh = useRefresh();
  const [source, setSource] = useState("");
  const [version, setVersion] = useState("");
  const [lines, setLines] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [touched, setTouched] = useState(false);
  const parsed = parseClassificationLines(lines);
  const problems = [
    ...(source.trim() ? [] : ["Informe a fonte (ex.: tabela oficial publicada)."]),
    ...(parsed.items.length === 0 ? ["Cole ao menos uma linha."] : []),
    ...parsed.errors,
  ];
  const run = useMutation({
    mutationFn: async () => {
      const doc = file ? await uploadDocument("classification-source", file, `Fonte do catálogo: ${source.trim()}`) : null;
      return importClassifications({ kind: "CClassTrib", source: source.trim(), sourceVersion: version.trim() || null, sourceDocumentId: doc?.id ?? null, items: parsed.items });
    },
    onSuccess: (r) => {
      toast.success(`${r.imported} código(s) importado(s) como rascunho.${r.skipped.length ? ` Ignorados: ${r.skipped.join(", ")}.` : ""}`);
      setLines(""); setFile(null); setTouched(false); refresh();
    },
    onError: toastError,
  });
  return (
    <SectionCard title="Importar códigos" description="Entram como rascunho e não fazem nada até serem ativados. Duplicados são ignorados e listados.">
      <form className="grid gap-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) run.mutate(); }}>
        <Field id="ci-source" label="Fonte"><Input id="ci-source" maxLength={300} value={source} onChange={(e) => setSource(e.target.value)} /></Field>
        <Field id="ci-version" label="Versão da fonte (opcional)"><Input id="ci-version" maxLength={100} value={version} onChange={(e) => setVersion(e.target.value)} /></Field>
        <Field id="ci-lines" label="Linhas: código;descrição;início;fim (opcional)" className="sm:col-span-2">
          <Textarea id="ci-lines" rows={6} className="font-mono text-xs" value={lines} onChange={(e) => setLines(e.target.value)} />
        </Field>
        <div className="sm:col-span-2"><p className="mb-1 text-sm font-medium">Documento da fonte (opcional)</p><FilePicker id="ci-file" file={file} onChange={setFile} /></div>
        <div className="sm:col-span-2">{touched && <ProblemList problems={problems} />}</div>
        <div className="sm:col-span-2"><Button type="submit" disabled={run.isPending}>{run.isPending ? "Importando…" : `Importar ${parsed.items.length} código(s)`}</Button></div>
      </form>
    </SectionCard>
  );
}

// ── Obligations ─────────────────────────────────────────────────────────────

/** The ancillary-obligation catalogue the companies assess against. Deactivating hides it from new assessments; history stays. */
export function TaxObligationsSection() {
  const list = useQuery({ queryKey: PLATFORM_TAX_KEYS.obligations, queryFn: listPlatformObligations });
  const refresh = useRefresh();
  const [d, setD] = useState({ code: "", name: "", description: "", reference: "" });
  const toggle = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setObligationActive(id, active),
    onSuccess: refresh, onError: toastError,
  });
  const create = useMutation({
    mutationFn: () => defineObligation({ code: d.code.trim(), name: d.name.trim(), description: d.description.trim() || null, reference: d.reference.trim() }),
    onSuccess: () => { toast.success("Obrigação cadastrada."); setD({ code: "", name: "", description: "", reference: "" }); refresh(); },
    onError: toastError,
  });
  const ready = d.code.trim() && d.name.trim() && d.reference.trim();
  return (
    <SectionCard title="Obrigações acessórias" description="Catálogo configurável. Cada obrigação cita a sua referência normativa.">
      {list.isLoading && <Skeleton className="h-24 w-full" />}
      {list.isError && <TaxQueryError error={list.error} what="as obrigações" onRetry={() => void list.refetch()} />}
      <ul className="space-y-2">
        {list.data?.map((o) => (
          <li key={o.id} className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border p-2 text-sm">
            <div className="min-w-0">
              <p className="font-medium"><span className="font-mono text-xs">{o.code}</span> {o.name} {!o.active && <ToneBadge label="Inativa" tone="neutral" />}</p>
              <p className="text-xs text-muted-foreground">{o.reference}{o.description ? ` · ${o.description}` : ""} · {o.createdByName}</p>
            </div>
            <Button type="button" size="sm" variant="ghost" disabled={toggle.isPending} onClick={() => toggle.mutate({ id: o.id, active: !o.active })}>{o.active ? "Inativar" : "Reativar"}</Button>
          </li>
        ))}
      </ul>
      <form className="mt-4 grid gap-3 border-t border-border pt-4 sm:grid-cols-2 lg:grid-cols-4" onSubmit={(e) => { e.preventDefault(); if (ready) create.mutate(); }}>
        <Field id="ob-code" label="Código"><Input id="ob-code" maxLength={40} value={d.code} onChange={(e) => setD({ ...d, code: e.target.value })} /></Field>
        <Field id="ob-name" label="Nome"><Input id="ob-name" maxLength={200} value={d.name} onChange={(e) => setD({ ...d, name: e.target.value })} /></Field>
        <Field id="ob-ref" label="Referência normativa"><Input id="ob-ref" maxLength={300} value={d.reference} onChange={(e) => setD({ ...d, reference: e.target.value })} /></Field>
        <Field id="ob-desc" label="Descrição (opcional)"><Input id="ob-desc" maxLength={1000} value={d.description} onChange={(e) => setD({ ...d, description: e.target.value })} /></Field>
        <div className="sm:col-span-2 lg:col-span-4"><Button type="submit" variant="outline" disabled={!ready || create.isPending}>Cadastrar obrigação</Button></div>
      </form>
    </SectionCard>
  );
}

// ── Retention defaults ──────────────────────────────────────────────────────

/** Platform defaults per evidence type. There is no automatic disposal: a policy only makes a preview possible. */
export function TaxRetentionSection() {
  const list = useQuery({ queryKey: PLATFORM_TAX_KEYS.retention, queryFn: listPlatformRetention });
  const refresh = useRefresh();
  const [type, setType] = useState(ALL);
  const [months, setMonths] = useState("");
  const [startsAt, setStartsAt] = useState<RetentionStart>("EndOfUploadYear");
  const [override, setOverride] = useState(false);
  const [rationale, setRationale] = useState("");
  const [reference, setReference] = useState("");
  const n = Number(months);
  const ready = Number.isInteger(n) && n >= 1 && n <= 600 && rationale.trim() && reference.trim();
  const save = useMutation({
    mutationFn: () => configurePlatformRetention({
      evidenceType: type === ALL ? null : (type as EvidenceType), retentionMonths: n, startsAt, enabled: true, allowTenantOverride: override,
      rationale: rationale.trim(), reference: reference.trim(),
    }),
    onSuccess: () => { toast.success("Padrão de retenção registrado (versão nova)."); setMonths(""); setRationale(""); setReference(""); refresh(); },
    onError: toastError,
  });
  return (
    <SectionCard title="Retenção — padrão da plataforma" description="Nenhum arquivo é descartado automaticamente. Retenção legal sempre prevalece.">
      {list.isLoading && <Skeleton className="h-24 w-full" />}
      {list.isError && <TaxQueryError error={list.error} what="as políticas" onRetry={() => void list.refetch()} />}
      <ul className="space-y-1 text-sm">
        {list.data?.filter((p) => p.supersededAt === null).map((p) => (
          <li key={p.id}>
            <span className="font-medium">{p.evidenceType ? EVIDENCE_TYPES[p.evidenceType] : "Todos os tipos"}</span>: {p.retentionMonths} meses, {RETENTION_START[p.startsAt].toLowerCase()} · v{p.version}
            {p.allowTenantOverride ? " · empresa pode ajustar" : " · fixa"} <span className="text-xs text-muted-foreground">({p.reference} — {p.configuredByName})</span>
          </li>
        ))}
      </ul>
      <form className="mt-4 grid gap-3 border-t border-border pt-4 sm:grid-cols-2 lg:grid-cols-3" onSubmit={(e) => { e.preventDefault(); if (ready) save.mutate(); }}>
        <Field id="pr-type" label="Tipo">
          <Select value={type} onValueChange={setType}>
            <SelectTrigger id="pr-type"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Todos os tipos</SelectItem>
              {(Object.keys(EVIDENCE_TYPES) as EvidenceType[]).map((k) => <SelectItem key={k} value={k}>{EVIDENCE_TYPES[k]}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id="pr-months" label="Meses"><Input id="pr-months" inputMode="numeric" value={months} onChange={(e) => setMonths(e.target.value)} /></Field>
        <Field id="pr-start" label="Conta a partir">
          <Select value={startsAt} onValueChange={(v) => setStartsAt(v as RetentionStart)}>
            <SelectTrigger id="pr-start"><SelectValue /></SelectTrigger>
            <SelectContent>{(Object.keys(RETENTION_START) as RetentionStart[]).map((k) => <SelectItem key={k} value={k}>{RETENTION_START[k]}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id="pr-ref" label="Referência"><Input id="pr-ref" maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
        <Field id="pr-why" label="Motivo" className="lg:col-span-2"><Input id="pr-why" maxLength={1000} value={rationale} onChange={(e) => setRationale(e.target.value)} /></Field>
        <label htmlFor="pr-override" className="flex items-center gap-2 text-sm sm:col-span-2 lg:col-span-3">
          <Checkbox id="pr-override" checked={override} onCheckedChange={(v) => setOverride(v === true)} /> Empresas podem ajustar este padrão
        </label>
        <div className="sm:col-span-2 lg:col-span-3"><Button type="submit" variant="outline" disabled={!ready || save.isPending}>Registrar padrão</Button></div>
      </form>
    </SectionCard>
  );
}

// ── Documents ───────────────────────────────────────────────────────────────

const PURPOSES: Record<DocumentPurpose, string> = { "professional-review": "Revisão profissional", "classification-source": "Fonte do catálogo" };

export function TaxDocumentsSection() {
  const [purpose, setPurpose] = useState<DocumentPurpose | "">("");
  const list = useQuery({ queryKey: PLATFORM_TAX_KEYS.documents(purpose), queryFn: () => listDocuments(purpose) });
  const refresh = useRefresh();
  const [reasonDialog, askReason] = useReasonDialog();
  const revoke = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => revokeDocument(id, reason),
    onSuccess: () => { toast.success("Documento revogado (continua registrado)."); refresh(); },
    onError: toastError,
  });
  return (
    <SectionCard title="Documentos privados" description="Revisões profissionais e fontes de catálogo. Download autorizado a cada pedido; nunca por link público.">
      {reasonDialog}
      <Field id="doc-purpose" label="Finalidade" className="mb-3 w-full sm:w-64">
        <Select value={purpose || ALL} onValueChange={(v) => setPurpose(v === ALL ? "" : (v as DocumentPurpose))}>
          <SelectTrigger id="doc-purpose"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>Todas</SelectItem>
            {(Object.keys(PURPOSES) as DocumentPurpose[]).map((k) => <SelectItem key={k} value={k}>{PURPOSES[k]}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      {list.isLoading && <Skeleton className="h-24 w-full" />}
      {list.isError && <TaxQueryError error={list.error} what="os documentos" onRetry={() => void list.refetch()} />}
      {list.data?.length === 0 && <p className="text-sm text-muted-foreground">Nenhum documento.</p>}
      <ul className="space-y-2">
        {list.data?.map((f) => (
          <li key={f.id} className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border p-2 text-xs">
            <div className="min-w-0 space-y-0.5">
              <p className="flex flex-wrap items-center gap-2"><span className="break-all font-medium">{f.originalFileName}</span>
                <ToneBadge label={labelOf(FILE_STATUS, f.status)} tone={toneOf(FILE_STATUS, f.status)} /></p>
              <p className="break-all font-mono text-muted-foreground">SHA-256 {f.sha256}</p>
              <p className="text-muted-foreground">{(f.sizeBytes / 1024).toFixed(0)} KB · {f.uploadedByName}, {formatDateTime(f.uploadedAt)}{f.description ? ` · ${f.description}` : ""}</p>
            </div>
            <div className="flex gap-1">
              <DownloadButton path={documentUrl(f.id)} fileName={f.originalFileName} variant="ghost" />
              {f.status === "Active" && (
                <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={() => askReason({
                  title: "Revogar documento", description: "Deixa de valer, mas continua registrado e auditável.", confirmLabel: "Revogar", danger: true,
                  onConfirm: (reason) => revoke.mutate({ id: f.id, reason }),
                })}>Revogar</Button>
              )}
            </div>
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}

// ── Refusals and audit ──────────────────────────────────────────────────────

export function TaxAuditSection() {
  const refusals = useQuery({ queryKey: PLATFORM_TAX_KEYS.refusals, queryFn: () => getRefusals(50) });
  const [action, setAction] = useState("");
  const [skip, setSkip] = useState(0);
  const audit = useQuery({ queryKey: PLATFORM_TAX_KEYS.audit(action, skip), queryFn: () => getTaxAudit(action, skip, 50) });
  return (
    <div className="space-y-6">
      <SectionCard title="Recusas recentes" description="Trabalho novo recusado pela governança ou pela matriz de cobertura, com o código.">
        {refusals.isLoading && <Skeleton className="h-24 w-full" />}
        {refusals.isError && <TaxQueryError error={refusals.error} what="as recusas" onRetry={() => void refusals.refetch()} />}
        {refusals.data?.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma recusa registrada.</p>}
        <ul className="space-y-1 text-sm">
          {refusals.data?.map((r, i) => (
            <li key={`${r.at}-${i}`}>
              <span className="font-medium">{refusalTitle(r.code)}</span> <span className="font-mono text-xs text-muted-foreground">{r.code ?? "—"}</span>
              <span className="block text-xs text-muted-foreground">{r.description} · {r.actorName} · {formatDateTime(r.at)}{r.tenantId ? ` · empresa ${r.tenantId}` : ""}</span>
            </li>
          ))}
        </ul>
      </SectionCard>
      <SectionCard title="Auditoria do módulo">
        <Field id="au-action" label="Ação (código exato, opcional)" className="mb-3 w-full sm:w-80">
          <Input id="au-action" value={action} onChange={(e) => { setAction(e.target.value); setSkip(0); }} placeholder="ex.: tax_module_activated" />
        </Field>
        {audit.isLoading && <Skeleton className="h-24 w-full" />}
        {audit.isError && <TaxQueryError error={audit.error} what="a auditoria" onRetry={() => void audit.refetch()} />}
        {audit.data?.items.length === 0 && <p className="text-sm text-muted-foreground">Nenhum evento.</p>}
        <ul className="space-y-1 text-sm">
          {audit.data?.items.map((e) => (
            <li key={e.id}>
              <span className="font-mono text-xs">{e.action}</span> — {e.description}
              <span className="block text-xs text-muted-foreground">{e.actorName} · {formatDateTime(e.at)} · {e.severity}{e.tenantId ? ` · empresa ${e.tenantId}` : " · plataforma"}</span>
            </li>
          ))}
        </ul>
        {audit.data && <TaxPager total={audit.data.total} skip={skip} take={50} onSkip={setSkip} />}
      </SectionCard>
    </div>
  );
}
