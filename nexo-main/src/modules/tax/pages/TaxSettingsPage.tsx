import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import {
  TAX_KEYS, configureMateriality, configureRetention, getMateriality, getRetentionPreview, listRetentionPolicies,
  type EvidenceType, type RetentionStart, type TaxRetentionPolicyDto,
} from "../api/tax.api";
import { EVIDENCE_TYPES, RETENTION_START } from "../lib/labels";
import { brazilTodayIso, formatIsoDate, parseDecimal } from "../lib/forms";
import { Field, ProblemList, TaxNote, TaxSubNav } from "../components/TaxShell";
import { TaxQueryError, toastError } from "../components/TaxCommon";

/**
 * Políticas da empresa: materialidade (versionada — define quando a aprovação precisa de outra pessoa) e retenção das evidências
 * (prévia apenas: nada é descartado automaticamente). Só a diretoria altera; cada mudança vira uma versão nova, com motivo.
 */
export default function TaxSettingsPage() {
  const { session } = useAuth();
  const isDirector = session?.role === "diretoria";
  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Políticas"
        description="Materialidade das decisões e retenção das evidências. Cada mudança é uma versão nova, com motivo e referência — nada é sobrescrito."
      />
      <TaxSubNav />
      <MaterialitySection canEdit={isDirector} />
      <RetentionSection canEdit={isDirector} />
    </div>
  );
}

// ── Materiality ──────────────────────────────────────────────────────────────

function MaterialitySection({ canEdit }: Readonly<{ canEdit: boolean }>) {
  const data = useQuery({ queryKey: TAX_KEYS.materiality, queryFn: getMateriality });
  const m = data.data;
  return (
    <SectionCard
      title="Materialidade"
      description="Acima deste valor, a aprovação de um cenário exige outra pessoa além de quem analisou (maker-checker). Não muda nenhum cálculo."
    >
      {data.isLoading && <Skeleton className="h-24 w-full" />}
      {data.isError && <TaxQueryError error={data.error} what="a materialidade" onRetry={() => void data.refetch()} />}
      {m && (
        <div className="space-y-4">
          <p className="text-sm">
            {m.inForce
              ? <>Em vigor: <strong>{formatCurrency(m.inForce.threshold)}</strong> — versão {m.inForce.version}, desde {formatIsoDate(m.inForce.effectiveFrom)} ({m.inForce.decisionReference}).</>
              : <>Sem versão própria da empresa. {m.installationDefault === null ? "A instalação também não define um padrão." : <>Vale o padrão da instalação: <strong>{formatCurrency(m.installationDefault)}</strong>.</>}</>}
          </p>
          {canEdit ? <MaterialityForm /> : <TaxNote>Só a diretoria registra uma nova versão da materialidade.</TaxNote>}
          {m.history.length > 0 && (
            <ol className="space-y-2">
              {m.history.map((h) => (
                <li key={h.id} className="flex gap-2 text-sm">
                  <History className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
                  <div className="min-w-0">
                    <p className="font-medium">Versão {h.version}: {formatCurrency(h.threshold)} · {formatIsoDate(h.effectiveFrom)}{h.effectiveTo ? ` a ${formatIsoDate(h.effectiveTo)}` : " em diante"}</p>
                    <p className="break-words text-muted-foreground">{h.rationale} · {h.decisionReference}</p>
                    <p className="text-xs text-muted-foreground">{h.configuredByName} em {formatDateTime(h.configuredAt)}</p>
                  </div>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </SectionCard>
  );
}

function MaterialityForm() {
  const qc = useQueryClient();
  const [threshold, setThreshold] = useState("");
  const [from, setFrom] = useState(brazilTodayIso());
  const [rationale, setRationale] = useState("");
  const [reference, setReference] = useState("");
  const [touched, setTouched] = useState(false);
  const value = parseDecimal(threshold);
  const problems = [
    ...(value === null || Number.isNaN(value) || value <= 0 ? ["Informe um valor maior que zero."] : []),
    ...(/^\d{4}-\d{2}-\d{2}$/.test(from) ? [] : ["Informe a data de início."]),
    ...(rationale.trim() ? [] : ["Diga o motivo."]),
    ...(reference.trim() ? [] : ["Informe a referência da decisão (ata, e-mail, chamado…)."]),
  ];
  const save = useMutation({
    mutationFn: () => configureMateriality({
      threshold: value!, currency: "BRL", effectiveFrom: from, effectiveTo: null, rationale: rationale.trim(), decisionReference: reference.trim(),
    }),
    onSuccess: () => {
      toast.success("Nova versão da materialidade registrada.");
      setThreshold(""); setRationale(""); setReference(""); setTouched(false);
      void qc.invalidateQueries({ queryKey: TAX_KEYS.materiality });
    },
    onError: toastError,
  });
  return (
    <form className="grid gap-3 rounded-md border border-dashed border-border p-3 sm:grid-cols-2 lg:grid-cols-4"
      onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
      <Field id="mat-value" label="Valor (R$)"><Input id="mat-value" inputMode="decimal" placeholder="10.000,00" value={threshold} onChange={(e) => setThreshold(e.target.value)} /></Field>
      <Field id="mat-from" label="Vale a partir de"><Input id="mat-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
      <Field id="mat-ref" label="Referência da decisão"><Input id="mat-ref" maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
      <Field id="mat-why" label="Motivo"><Input id="mat-why" maxLength={1000} value={rationale} onChange={(e) => setRationale(e.target.value)} /></Field>
      <div className="sm:col-span-2 lg:col-span-4">{touched && <ProblemList problems={problems} />}</div>
      <div className="sm:col-span-2 lg:col-span-4"><Button type="submit" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar nova versão"}</Button></div>
    </form>
  );
}

// ── Retention ────────────────────────────────────────────────────────────────

const ALL_TYPES = "__all";

function RetentionSection({ canEdit }: Readonly<{ canEdit: boolean }>) {
  const policies = useQuery({ queryKey: TAX_KEYS.retention, queryFn: listRetentionPolicies });
  const [preview, setPreview] = useState(false);
  const inForce = (policies.data ?? []).filter((p) => p.supersededAt === null);
  return (
    <SectionCard
      title="Retenção das evidências"
      description="Por quanto tempo os arquivos ficam guardados. A plataforma define o padrão; a empresa só ajusta onde o padrão permite. Nenhum arquivo é apagado automaticamente."
    >
      {policies.isLoading && <Skeleton className="h-24 w-full" />}
      {policies.isError && <TaxQueryError error={policies.error} what="as políticas de retenção" onRetry={() => void policies.refetch()} />}
      {policies.data && (
        <div className="space-y-4">
          {inForce.length === 0
            ? <p className="text-sm text-muted-foreground">Nenhuma política em vigor: nenhum arquivo é elegível para descarte.</p>
            : <RetentionTable rows={inForce} />}
          {canEdit && <RetentionForm platform={inForce.filter((p) => p.tenantId === null)} />}
          {canEdit && (
            <div className="space-y-2">
              <Button type="button" variant="outline" onClick={() => setPreview(true)}>Ver prévia do que seria elegível</Button>
              {preview && <RetentionPreview />}
            </div>
          )}
          {!canEdit && <TaxNote>Só a diretoria ajusta a retenção e vê a prévia.</TaxNote>}
        </div>
      )}
    </SectionCard>
  );
}

function RetentionTable({ rows }: Readonly<{ rows: TaxRetentionPolicyDto[] }>) {
  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow><TableHead>Origem</TableHead><TableHead>Tipo de evidência</TableHead><TableHead className="text-right">Meses</TableHead><TableHead>Conta a partir</TableHead><TableHead>Situação</TableHead></TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((p) => (
            <TableRow key={p.id}>
              <TableCell>{p.tenantId ? "Empresa" : "Plataforma"} · v{p.version}</TableCell>
              <TableCell>{p.evidenceType ? EVIDENCE_TYPES[p.evidenceType] : "Todos"}</TableCell>
              <TableCell className="text-right tabular-nums">{p.retentionMonths}</TableCell>
              <TableCell>{RETENTION_START[p.startsAt]}</TableCell>
              <TableCell className="text-sm">
                {p.enabled ? "Ativa" : "Desligada"}{!p.tenantId && (p.allowTenantOverride ? " · empresa pode ajustar" : " · fixa")}
                <p className="text-xs text-muted-foreground">{p.reference} — {p.configuredByName}</p>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function RetentionForm({ platform }: Readonly<{ platform: TaxRetentionPolicyDto[] }>) {
  const qc = useQueryClient();
  const [type, setType] = useState<string>(ALL_TYPES);
  const [months, setMonths] = useState("");
  const [startsAt, setStartsAt] = useState<RetentionStart>("EndOfUploadYear");
  const [enabled, setEnabled] = useState(true);
  const [rationale, setRationale] = useState("");
  const [reference, setReference] = useState("");
  const [touched, setTouched] = useState(false);
  const evidenceType = type === ALL_TYPES ? null : (type as EvidenceType);
  const base = platform.find((p) => p.evidenceType === evidenceType) ?? platform.find((p) => p.evidenceType === null);
  const n = Number(months);
  const problems = [
    ...(base && !base.allowTenantOverride ? ["O padrão da plataforma para este tipo não permite ajuste da empresa."] : []),
    ...(!base ? ["Não há padrão da plataforma para este tipo: a empresa não pode criar uma política sozinha."] : []),
    ...(Number.isInteger(n) && n >= 1 && n <= 600 ? [] : ["Meses: um número inteiro de 1 a 600."]),
    ...(rationale.trim() ? [] : ["Diga o motivo."]),
    ...(reference.trim() ? [] : ["Informe a referência (norma interna, parecer, ata…)."]),
  ];
  const save = useMutation({
    mutationFn: () => configureRetention({
      evidenceType, retentionMonths: n, startsAt, enabled, allowTenantOverride: false, rationale: rationale.trim(), reference: reference.trim(),
    }),
    onSuccess: () => {
      toast.success("Política de retenção registrada (versão nova).");
      setMonths(""); setRationale(""); setReference(""); setTouched(false);
      void qc.invalidateQueries({ queryKey: TAX_KEYS.retention });
    },
    onError: toastError,
  });
  return (
    <form className="grid gap-3 rounded-md border border-dashed border-border p-3 sm:grid-cols-2 lg:grid-cols-3"
      onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
      <p className="text-sm font-medium sm:col-span-2 lg:col-span-3">Ajuste da empresa</p>
      <Field id="ret-type" label="Tipo de evidência">
        <Select value={type} onValueChange={setType}>
          <SelectTrigger id="ret-type"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_TYPES}>Todos os tipos</SelectItem>
            {(Object.keys(EVIDENCE_TYPES) as EvidenceType[]).map((k) => <SelectItem key={k} value={k}>{EVIDENCE_TYPES[k]}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      <Field id="ret-months" label="Meses" hint={base ? `Padrão da plataforma: ${base.retentionMonths} meses.` : undefined}>
        <Input id="ret-months" inputMode="numeric" value={months} onChange={(e) => setMonths(e.target.value)} />
      </Field>
      <Field id="ret-start" label="Conta a partir">
        <Select value={startsAt} onValueChange={(v) => setStartsAt(v as RetentionStart)}>
          <SelectTrigger id="ret-start"><SelectValue /></SelectTrigger>
          <SelectContent>{(Object.keys(RETENTION_START) as RetentionStart[]).map((k) => <SelectItem key={k} value={k}>{RETENTION_START[k]}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
      <Field id="ret-ref" label="Referência"><Input id="ret-ref" maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
      <Field id="ret-why" label="Motivo" className="lg:col-span-2"><Input id="ret-why" maxLength={1000} value={rationale} onChange={(e) => setRationale(e.target.value)} /></Field>
      <label htmlFor="ret-enabled" className="flex items-center gap-2 text-sm sm:col-span-2 lg:col-span-3">
        <Checkbox id="ret-enabled" checked={enabled} onCheckedChange={(c) => setEnabled(c === true)} /> Política ativa
      </label>
      <div className="sm:col-span-2 lg:col-span-3">{touched && <ProblemList problems={problems} />}</div>
      <div className="sm:col-span-2 lg:col-span-3"><Button type="submit" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar ajuste"}</Button></div>
    </form>
  );
}

function RetentionPreview() {
  const preview = useQuery({ queryKey: TAX_KEYS.retentionPreview, queryFn: getRetentionPreview });
  const p = preview.data;
  if (preview.isLoading) return <Skeleton className="h-20 w-full" />;
  if (preview.isError) return <TaxQueryError error={preview.error} what="a prévia" onRetry={() => void preview.refetch()} />;
  if (!p) return null;
  return (
    <div className="space-y-2 rounded-md border border-border p-3 text-sm">
      <p className="font-medium">{p.notice}</p>
      <p className="text-muted-foreground">
        Em {formatIsoDate(p.asOf)}: {p.filesConsidered} arquivo(s) considerados · {p.onLegalHold} em retenção legal · {p.withoutPolicy} sem política · {p.eligible.length} elegível(is).
      </p>
      {p.eligible.length > 0 && (
        <ul className="space-y-1">
          {p.eligible.map((f) => (
            <li key={f.fileId} className="text-xs">
              <span className="font-medium">{f.originalFileName}</span> · {EVIDENCE_TYPES[f.evidenceType]} · enviado {formatIsoDate(f.uploadedAt)} · elegível desde {formatIsoDate(f.eligibleFrom)} (política v{f.policyVersion})
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
