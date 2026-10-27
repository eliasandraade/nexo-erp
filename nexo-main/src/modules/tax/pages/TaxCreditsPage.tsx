import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PlugZap, Plus, Wallet } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import {
  TAX_KEY, TAX_KEYS, getSplitProvider, getTaxCreditTotals, listSplitExpectations, listTaxCredits, moveTaxCredit, registerTaxCredit,
  type CreditMovement, type CreditTax, type TaxCreditDto,
} from "../api/tax.api";
import {
  CREDIT_ENTRY_TYPES, CREDIT_LIFECYCLE, CREDIT_MOVEMENTS, CREDIT_TAX_NAMES, POTENTIAL_CREDIT_COPY, SPLIT_EXPECTATION_COPY, labelOf, toneOf,
} from "../lib/labels";
import { ToneBadge } from "../components/TaxBadges";
import {
  creditProblems, emptyCredit, errorMessage, formatIsoDate, movementProblems, toCreditRequest, toMovementRequest,
  type CreditDraft, type MovementDraft,
} from "../lib/forms";
import { SplitTable } from "../components/SplitTable";
import { Field, ProblemList, TaxNote, TaxSubNav } from "../components/TaxShell";

const TABS = ["creditos", "split"] as const;

/** Créditos de CBS/IBS (razão por crédito: reconhecido, apropriado, usado, bloqueado) e conferência do split payment. */
export default function TaxCreditsPage() {
  const [params, setParams] = useSearchParams();
  const tab = params.get("aba") === "split" ? "split" : "creditos";

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Créditos e split"
        description="Quanto de crédito existe, quanto já foi usado e o que está bloqueado; e se o que o pagamento reteve confere com o esperado."
      />
      <TaxSubNav />
      <Tabs value={tab} onValueChange={(v) => setParams(v === "split" ? { aba: "split" } : {})}>
        <TabsList>
          {TABS.map((t) => <TabsTrigger key={t} value={t}>{t === "split" ? "Split payment" : "Créditos"}</TabsTrigger>)}
        </TabsList>
        <TabsContent value="creditos" className="space-y-6"><Credits /></TabsContent>
        <TabsContent value="split" className="space-y-6"><SplitPayment /></TabsContent>
      </Tabs>
    </div>
  );
}

function Credits() {
  const [registering, setRegistering] = useState(false);
  const [moving, setMoving] = useState<TaxCreditDto | null>(null);
  const totals = useQuery({ queryKey: TAX_KEYS.creditTotals, queryFn: getTaxCreditTotals });
  const credits = useQuery({ queryKey: TAX_KEYS.credits, queryFn: listTaxCredits });

  return (
    <>
      {totals.isError && <ErrorState title="Não foi possível somar os créditos" onRetry={() => void totals.refetch()} compact />}
      {totals.data && totals.data.length > 0 && (
        <div className="space-y-3">
          {totals.data.map((t) => (
            <div key={t.tax} className="space-y-2">
              <h2 className="section-heading">{labelOf(CREDIT_TAX_NAMES, t.tax)}</h2>
              <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
                <MetricBlock label="Potencial estimado" value={formatCurrency(t.potential)} caption="não é crédito validado" />
                <MetricBlock label="A apropriar" value={formatCurrency(t.toBeAppropriated)} caption="requisitos confirmados" />
                <MetricBlock label="Apropriado" value={formatCurrency(t.appropriated)} />
                <MetricBlock label="Usado" value={formatCurrency(t.used)} />
                <MetricBlock label="Bloqueado" value={formatCurrency(t.blocked)} variant={t.blocked > 0 ? "warning" : "default"} />
                <MetricBlock label="Disponível" value={formatCurrency(t.available)} variant={t.available > 0 ? "success" : "default"} />
              </div>
            </div>
          ))}
        </div>
      )}

      <SectionCard
        title="Créditos registrados"
        description="Um crédito registrado começa como potencial. Só depois de confirmar os requisitos ele fica a apropriar; só o apropriado pode ser usado, contra imposto devido do mesmo tributo, uma vez por obrigação."
        actions={<Button size="sm" onClick={() => setRegistering(true)}><Plus className="mr-1.5 h-3.5 w-3.5" /> Registrar crédito</Button>}
      >
        {credits.isLoading && <Skeleton className="h-32 w-full" />}
        {credits.isError && <ErrorState title="Não foi possível listar os créditos" onRetry={() => void credits.refetch()} compact />}
        {credits.data?.length === 0 && <EmptyState icon={Wallet} title="Nenhum crédito registrado" description="Registre o crédito de uma nota de compra para acompanhar apropriação e uso." compact />}
        <ul className="space-y-3">
          {credits.data?.map((c) => (
            <li key={c.id} className="rounded-md border border-border p-3 text-sm">
              <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <p className="font-medium">{labelOf(CREDIT_TAX_NAMES, c.tax)} · {c.sourceDocumentType} de {formatIsoDate(c.acquiredOn)}{c.expiresOn ? ` · vence ${formatIsoDate(c.expiresOn)}` : ""}</p>
                  {c.sourceDocumentKey && <p className="break-all font-mono text-xs text-muted-foreground">{c.sourceDocumentKey}</p>}
                  <p className="flex flex-wrap items-center gap-2 pt-1">
                    <ToneBadge label={labelOf(CREDIT_LIFECYCLE, c.lifecycle)} tone={toneOf(CREDIT_LIFECYCLE, c.lifecycle)} />
                  </p>
                  {(c.stage === "PotentialIdentified" || c.stage === "RequirementsUnderReview") && (
                    <p className="text-xs text-warning">{POTENTIAL_CREDIT_COPY}</p>
                  )}
                  <p className="break-words text-xs text-muted-foreground">Estimativa: {c.potentialBasis}</p>
                  {c.recognitionBasis && <p className="break-words text-xs text-muted-foreground">Requisitos confirmados: {c.recognitionBasis}</p>}
                  {c.discardReason && <p className="break-words text-xs text-muted-foreground">Descartado: {c.discardReason}</p>}
                </div>
                <Button size="sm" variant="outline" onClick={() => setMoving(c)}>Movimentar</Button>
              </div>
              <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-4">
                <Pair label="Destacado" value={c.originalAmount} />
                <Pair label="Potencial" value={c.balance.potential} />
                <Pair label="A apropriar" value={c.balance.toBeAppropriated} />
                <Pair label="Disponível" value={c.balance.available} />
                <Pair label="Usado" value={c.balance.used} />
                <Pair label="Bloqueado" value={c.balance.blocked} />
              </dl>
              {c.entries.length > 0 && (
                <details className="mt-2">
                  <summary className="cursor-pointer text-xs font-medium">Razão do crédito ({c.entries.length})</summary>
                  <ul className="mt-1 space-y-1 text-xs">
                    {c.entries.map((e) => (
                      <li key={e.id} className="break-words">
                        <span className="font-medium">{labelOf(CREDIT_ENTRY_TYPES, e.type)}</span> {formatCurrency(e.amount)} em {formatIsoDate(e.occurredOn)}
                        {e.obligationReference ? ` · obrigação ${e.obligationReference}` : ""} · {e.reason} · {e.actorName}, {formatDateTime(e.createdAt)}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </li>
          ))}
        </ul>
      </SectionCard>

      <RegisterDialog open={registering} onClose={() => setRegistering(false)} />
      <MovementDialog key={moving?.id ?? "none"} credit={moving} onClose={() => setMoving(null)} />
    </>
  );
}

function Pair({ label, value }: Readonly<{ label: string; value: number }>) {
  return <div><dt className="text-muted-foreground">{label}</dt><dd className="tabular-nums">{formatCurrency(value)}</dd></div>;
}

function RegisterDialog({ open, onClose }: Readonly<{ open: boolean; onClose: () => void }>) {
  const qc = useQueryClient();
  const [d, setD] = useState<CreditDraft>(() => emptyCredit());
  const [touched, setTouched] = useState(false);
  const problems = creditProblems(d);
  const close = () => { setD(emptyCredit()); setTouched(false); onClose(); };
  const save = useMutation({
    mutationFn: () => registerTaxCredit(toCreditRequest(d)),
    onSuccess: () => { toast.success("Crédito registrado."); void qc.invalidateQueries({ queryKey: TAX_KEY }); close(); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const text = (k: keyof CreditDraft) => ({ value: d[k], onChange: (e: React.ChangeEvent<HTMLInputElement>) => setD({ ...d, [k]: e.target.value }) });

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) close(); }}>
      <DialogContent className="max-h-[90vh] max-w-lg overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Registrar crédito</DialogTitle>
          <DialogDescription>O imposto destacado na compra e quanto dele dá direito a crédito.</DialogDescription>
        </DialogHeader>
        <form id="credit-form" className="grid gap-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
          <Field id="cr-tax" label="Tributo">
            <Select value={d.tax} onValueChange={(v) => setD({ ...d, tax: v as CreditTax })}>
              <SelectTrigger id="cr-tax"><SelectValue /></SelectTrigger>
              <SelectContent>{(Object.keys(CREDIT_TAX_NAMES) as CreditTax[]).map((t) => <SelectItem key={t} value={t}>{CREDIT_TAX_NAMES[t]}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field id="cr-type" label="Documento"><Input id="cr-type" maxLength={40} placeholder="NF-e, NFS-e, CT-e…" {...text("sourceDocumentType")} /></Field>
          <Field id="cr-key" label="Chave de acesso (opcional)" className="sm:col-span-2"><Input id="cr-key" inputMode="numeric" {...text("sourceDocumentKey")} /></Field>
          <Field id="cr-sup" label="CPF/CNPJ do fornecedor (opcional)" className="sm:col-span-2"><Input id="cr-sup" inputMode="numeric" {...text("supplierDocument")} /></Field>
          <Field id="cr-acq" label="Data da aquisição"><Input id="cr-acq" type="date" {...text("acquiredOn")} /></Field>
          <Field id="cr-exp" label="Vence em (opcional)"><Input id="cr-exp" type="date" {...text("expiresOn")} /></Field>
          <Field id="cr-orig" label="Imposto destacado (R$)"><Input id="cr-orig" inputMode="decimal" {...text("originalAmount")} /></Field>
          <Field id="cr-elig" label="Crédito potencial estimado (R$)"><Input id="cr-elig" inputMode="decimal" {...text("potentialAmount")} /></Field>
          <Field id="cr-basis" label="De onde vem a estimativa" className="sm:col-span-2"><Input id="cr-basis" maxLength={500} placeholder="Ex.: insumo usado na atividade — a confirmar" {...text("potentialBasis")} /></Field>
          <p className="text-xs text-warning sm:col-span-2">{POTENTIAL_CREDIT_COPY}</p>
          <div className="sm:col-span-2">{touched && <ProblemList problems={problems} />}</div>
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={close}>Cancelar</Button>
          <Button type="submit" form="credit-form" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function reasonLabel(movement: CreditMovement): string {
  if (movement === "ConfirmToBeAppropriated") return "Base da confirmação dos requisitos";
  return ["Block", "Reverse", "Discard"].includes(movement) ? "Motivo" : "Motivo (opcional)";
}

const EMPTY_MOVE: MovementDraft = { movement: "StartRequirementsReview", amount: "", reason: "", obligationTax: "Cbs", obligationReference: "", onDate: "" };

/** The next step a credit usually takes: review its requirements, confirm them, then appropriate. */
function defaultMovement(credit: TaxCreditDto | null): CreditMovement {
  if (credit?.stage === "RequirementsUnderReview") return "ConfirmToBeAppropriated";
  if (credit?.stage === "ToBeAppropriated") return "Appropriate";
  return "StartRequirementsReview";
}

function MovementDialog({ credit, onClose }: Readonly<{ credit: TaxCreditDto | null; onClose: () => void }>) {
  const qc = useQueryClient();
  const [m, setM] = useState<MovementDraft>(() => ({ ...EMPTY_MOVE, movement: defaultMovement(credit) }));
  const [touched, setTouched] = useState(false);
  const problems = movementProblems(m);
  const close = () => { setM(EMPTY_MOVE); setTouched(false); onClose(); };
  const save = useMutation({
    mutationFn: () => moveTaxCredit(credit!.id, toMovementRequest(m)),
    onSuccess: () => { toast.success("Movimento registrado."); void qc.invalidateQueries({ queryKey: TAX_KEY }); close(); },
    onError: (e) => toast.error(e instanceof ApiError && e.status === 409 ? "Este crédito já foi usado nessa obrigação." : errorMessage(e)),
  });

  return (
    <Dialog open={credit !== null} onOpenChange={(v) => { if (!v) close(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Movimentar crédito</DialogTitle>
          <DialogDescription>{credit && `${labelOf(CREDIT_TAX_NAMES, credit.tax)} · ${labelOf(CREDIT_LIFECYCLE, credit.lifecycle)} · disponível ${formatCurrency(credit.balance.available)} · a apropriar ${formatCurrency(credit.balance.toBeAppropriated)}`}</DialogDescription>
        </DialogHeader>
        <form id="move-form" className="space-y-3" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
          <Field id="mv-kind" label="Movimento">
            <Select value={m.movement} onValueChange={(v) => setM({ ...m, movement: v as CreditMovement })}>
              <SelectTrigger id="mv-kind"><SelectValue /></SelectTrigger>
              <SelectContent>{(Object.keys(CREDIT_MOVEMENTS) as CreditMovement[]).map((k) => <SelectItem key={k} value={k}>{CREDIT_MOVEMENTS[k]}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          {m.movement !== "StartRequirementsReview" && m.movement !== "Discard" && (
            <Field id="mv-amount" label={m.movement === "Expire" ? "Valor (vazio = todo o saldo)" : "Valor (R$)"}>
              <Input id="mv-amount" inputMode="decimal" value={m.amount} onChange={(e) => setM({ ...m, amount: e.target.value })} />
            </Field>
          )}
          {m.movement === "Use" && (
            <div className="grid gap-3 sm:grid-cols-2">
              <Field id="mv-otax" label="Contra o imposto">
                <Select value={m.obligationTax} onValueChange={(v) => setM({ ...m, obligationTax: v as CreditTax })}>
                  <SelectTrigger id="mv-otax"><SelectValue /></SelectTrigger>
                  <SelectContent>{(Object.keys(CREDIT_TAX_NAMES) as CreditTax[]).map((t) => <SelectItem key={t} value={t}>{CREDIT_TAX_NAMES[t]}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              <Field id="mv-oref" label="Obrigação"><Input id="mv-oref" maxLength={100} placeholder="Apuração 10/2026" value={m.obligationReference} onChange={(e) => setM({ ...m, obligationReference: e.target.value })} /></Field>
            </div>
          )}
          <Field id="mv-reason" label={reasonLabel(m.movement)}>
            <Input id="mv-reason" maxLength={500} value={m.reason} onChange={(e) => setM({ ...m, reason: e.target.value })} />
          </Field>
          <Field id="mv-date" label="Data (vazio = hoje)"><Input id="mv-date" type="date" value={m.onDate} onChange={(e) => setM({ ...m, onDate: e.target.value })} /></Field>
          {touched && <ProblemList problems={problems} />}
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={close}>Cancelar</Button>
          <Button type="submit" form="move-form" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function SplitPayment() {
  const provider = useQuery({ queryKey: TAX_KEYS.splitProvider, queryFn: getSplitProvider });
  const expectations = useQuery({ queryKey: TAX_KEYS.splitExpectations("all"), queryFn: () => listSplitExpectations() });

  return (
    <>
      {provider.data && (
        <div className="flex items-start gap-2 rounded-md border border-warning/40 bg-warning/5 p-3 text-sm">
          <PlugZap className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
          <div>
            <p className="font-medium">{provider.data.configured ? "Provedor de pagamento conectado" : "Nenhum provedor de pagamento integrado"}</p>
            <p className="text-muted-foreground">{provider.data.message}</p>
          </div>
        </div>
      )}
      <TaxNote>
        {SPLIT_EXPECTATION_COPY} As expectativas nascem de um cálculo (abra o cálculo e registre). Só uma liquidação registrada — o que foi retido de
        fato, lançado à mão até haver integração — muda a conciliação; manual e Swagger de integração não são transação liquidada.
      </TaxNote>
      <SectionCard title="Retido x esperado">
        {expectations.isLoading && <Skeleton className="h-32 w-full" />}
        {expectations.isError && <ErrorState title="Não foi possível ler as retenções" onRetry={() => void expectations.refetch()} compact />}
        {expectations.data?.length === 0 && <EmptyState icon={Wallet} title="Nenhuma retenção esperada" compact />}
        {expectations.data && expectations.data.length > 0 && <SplitTable items={expectations.data} showCalculation />}
      </SectionCard>
    </>
  );
}
