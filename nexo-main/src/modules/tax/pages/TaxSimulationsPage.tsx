import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FlaskConical, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import {
  TAX_KEYS, compareSuppliers, createTaxSimulation, getTaxSimulation, listTaxSimulations,
  type SupplierOfferResultDto, type TaxDirection, type TaxRegime, type TaxType,
} from "../api/tax.api";
import { DIRECTIONS, TAX_NAMES } from "../lib/labels";
import {
  brazilTodayIso, emptyHypothesis, emptyOffer, emptyOperation, emptyScenario, errorMessage, isProfileMissing, offersProblems,
  simulationProblems, toCompareRequest, toOperationRequest, toScenarioRequest, type OfferDraft, type OperationDraft, type ScenarioDraft,
} from "../lib/forms";
import { LinesEditor, OperationEditor } from "../components/OperationEditor";
import { ScenarioCompare } from "../components/ScenarioCompare";
import { ToneBadge } from "../components/TaxBadges";
import { Field, ProblemList, ProfileMissingNotice, TaxNote, TaxSubNav } from "../components/TaxShell";

const KEEP = "__keep";

/** Simulações: comparar cenários (regime, data, alíquota hipotética) de uma operação, e o custo efetivo entre fornecedores. */
export default function TaxSimulationsPage() {
  const [params, setParams] = useSearchParams();
  const selected = params.get("id");
  const list = useQuery({ queryKey: TAX_KEYS.simulations, queryFn: listTaxSimulations });
  const open = (id: string | null) => setParams(id ? { id } : {}, { replace: false });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Simulações"
        description="Quanto muda se o regime, a data, o desconto ou a alíquota fossem outros — e qual fornecedor sai mais barato depois do crédito."
      />
      <TaxSubNav />
      <TaxNote>A simulação não altera vendas, livros, créditos nem obrigações. É só comparação.</TaxNote>

      {selected ? <SimulationView id={selected} onClose={() => open(null)} /> : <NewSimulation onCreated={open} />}

      <SectionCard title="Simulações guardadas" noPadding>
        {list.isLoading && <div className="p-5"><Skeleton className="h-24 w-full" /></div>}
        {list.isError && <ErrorState title="Não foi possível listar as simulações" onRetry={() => void list.refetch()} compact />}
        {list.data && list.data.length === 0 && <EmptyState icon={FlaskConical} title="Nenhuma simulação ainda" compact />}
        {list.data && list.data.length > 0 && (
          <ul className="divide-y divide-border">
            {list.data.map((s) => (
              <li key={s.id}>
                <button type="button" onClick={() => open(s.id)} className={cn("flex w-full flex-wrap items-center justify-between gap-2 px-5 py-3 text-left text-sm hover:bg-muted/50", s.id === selected && "bg-muted/50")}>
                  <span className="font-medium">{s.name}</span>
                  <span className="text-xs text-muted-foreground">{s.scenarios} {s.scenarios === 1 ? "cenário" : "cenários"} · {formatDateTime(s.createdAt)}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </SectionCard>

      <SupplierComparison />
    </div>
  );
}

function SimulationView({ id, onClose }: Readonly<{ id: string; onClose: () => void }>) {
  const sim = useQuery({ queryKey: TAX_KEYS.simulation(id), queryFn: () => getTaxSimulation(id) });
  return (
    <SectionCard
      title={sim.data?.name ?? "Simulação"}
      description={sim.data ? `Motor ${sim.data.engineVersion} · ${formatDateTime(sim.data.createdAt)}` : undefined}
      actions={<Button variant="outline" size="sm" onClick={onClose}>Nova simulação</Button>}
    >
      {sim.isLoading && <Skeleton className="h-40 w-full" />}
      {sim.isError && <ErrorState title="Não foi possível abrir a simulação" onRetry={() => void sim.refetch()} compact />}
      {sim.data && <ScenarioCompare scenarios={sim.data.scenarios} />}
    </SectionCard>
  );
}

function NewSimulation({ onCreated }: Readonly<{ onCreated: (id: string) => void }>) {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [baseline, setBaseline] = useState<OperationDraft>(() => emptyOperation());
  const [scenarios, setScenarios] = useState<ScenarioDraft[]>(() => [emptyScenario()]);
  const [touched, setTouched] = useState(false);
  const [profileMissing, setProfileMissing] = useState(false);
  const problems = simulationProblems(name, baseline, scenarios);
  const save = useMutation({
    mutationFn: () => createTaxSimulation({ name: name.trim(), baseline: toOperationRequest(baseline), scenarios: scenarios.map(toScenarioRequest) }),
    onSuccess: (s) => { void qc.invalidateQueries({ queryKey: TAX_KEYS.simulations }); onCreated(s.id); },
    onError: (e) => { if (isProfileMissing(e)) setProfileMissing(true); else toast.error(errorMessage(e)); },
  });
  const setScenario = (key: string, patch: Partial<ScenarioDraft>) => setScenarios(scenarios.map((s) => (s.key === key ? { ...s, ...patch } : s)));

  return (
    <SectionCard title="Nova simulação" description="Descreva a operação como ela é hoje e os cenários alternativos.">
      {profileMissing && <div className="mb-4"><ProfileMissingNotice /></div>}
      <form className="space-y-6" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
        <Field id="sim-name" label="Nome da simulação" className="max-w-md">
          <Input id="sim-name" maxLength={200} placeholder="Ex.: mudar para Lucro Presumido" value={name} onChange={(e) => setName(e.target.value)} />
        </Field>

        <div className="space-y-2">
          <h4 className="text-sm font-semibold">Como é hoje</h4>
          <OperationEditor idPrefix="sim-base" value={baseline} onChange={setBaseline} />
        </div>

        <div className="space-y-3">
          <h4 className="text-sm font-semibold">Cenários ({scenarios.length} de 10)</h4>
          <p className="text-xs text-muted-foreground">Cada cenário usa as mesmas linhas de hoje; só muda o que você escolher.</p>
          {scenarios.map((s, i) => (
            <fieldset key={s.key} className="space-y-3 rounded-md border border-border p-3">
              <legend className="px-1 text-xs font-medium text-muted-foreground">Cenário {i + 1}</legend>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <Field id={`${s.key}-name`} label="Nome"><Input id={`${s.key}-name`} maxLength={200} value={s.name} onChange={(e) => setScenario(s.key, { name: e.target.value })} /></Field>
                <Field id={`${s.key}-regime`} label="Regime">
                  <Select value={s.regime || KEEP} onValueChange={(v) => setScenario(s.key, { regime: v === KEEP ? "" : (v as TaxRegime) })}>
                    <SelectTrigger id={`${s.key}-regime`}><SelectValue /></SelectTrigger>
                    <SelectContent><SelectItem value={KEEP}>Igual ao de hoje</SelectItem>{TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
                  </Select>
                </Field>
                <Field id={`${s.key}-date`} label="Data (vazio = igual)"><Input id={`${s.key}-date`} type="date" value={s.referenceDate} onChange={(e) => setScenario(s.key, { referenceDate: e.target.value })} /></Field>
                <Field id={`${s.key}-dir`} label="Operação">
                  <Select value={s.direction || KEEP} onValueChange={(v) => setScenario(s.key, { direction: v === KEEP ? "" : (v as TaxDirection) })}>
                    <SelectTrigger id={`${s.key}-dir`}><SelectValue /></SelectTrigger>
                    <SelectContent><SelectItem value={KEEP}>Igual à de hoje</SelectItem>{(Object.keys(DIRECTIONS) as TaxDirection[]).map((d) => <SelectItem key={d} value={d}>{DIRECTIONS[d]}</SelectItem>)}</SelectContent>
                  </Select>
                </Field>
                <Field id={`${s.key}-cp`} label="Regime da outra parte">
                  <Select value={s.counterpartyRegime || KEEP} onValueChange={(v) => setScenario(s.key, { counterpartyRegime: v === KEEP ? "" : (v as TaxRegime) })}>
                    <SelectTrigger id={`${s.key}-cp`}><SelectValue /></SelectTrigger>
                    <SelectContent><SelectItem value={KEEP}>Igual ao de hoje</SelectItem>{TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
                  </Select>
                </Field>
                <label className="flex items-start gap-2 text-sm sm:col-span-1 lg:col-span-1" htmlFor={`${s.key}-conv`}>
                  <Switch id={`${s.key}-conv`} checked={s.convertConditionalDiscounts} onCheckedChange={(c) => setScenario(s.key, { convertConditionalDiscounts: c })} />
                  <span>Trocar descontos condicionais por incondicionais (na nota)</span>
                </label>
                <label className="flex items-start gap-2 text-sm lg:col-span-2" htmlFor={`${s.key}-cred`}>
                  <Switch id={`${s.key}-cred`} checked={s.assumeCreditable} onCheckedChange={(c) => setScenario(s.key, { assumeCreditable: c })} />
                  <span>Considerar que o imposto gera crédito para quem compra</span>
                </label>
              </div>

              <div className="space-y-2">
                {s.hypotheses.map((h) => (
                  <div key={h.key} className="grid gap-2 sm:grid-cols-[10rem_8rem_1fr_auto] sm:items-end">
                    <Field id={`${h.key}-tax`} label="Tributo">
                      <Select value={h.tax} onValueChange={(v) => setScenario(s.key, { hypotheses: s.hypotheses.map((x) => (x.key === h.key ? { ...x, tax: v as TaxType } : x)) })}>
                        <SelectTrigger id={`${h.key}-tax`}><SelectValue /></SelectTrigger>
                        <SelectContent>{(Object.keys(TAX_NAMES) as TaxType[]).map((t) => <SelectItem key={t} value={t}>{TAX_NAMES[t]}</SelectItem>)}</SelectContent>
                      </Select>
                    </Field>
                    <Field id={`${h.key}-rate`} label="Alíquota (%)">
                      <Input id={`${h.key}-rate`} inputMode="decimal" value={h.ratePercent} onChange={(e) => setScenario(s.key, { hypotheses: s.hypotheses.map((x) => (x.key === h.key ? { ...x, ratePercent: e.target.value } : x)) })} />
                    </Field>
                    <Field id={`${h.key}-label`} label="De onde vem">
                      <Input id={`${h.key}-label`} maxLength={200} placeholder="Ex.: estimativa de alíquota de referência 2027" value={h.label} onChange={(e) => setScenario(s.key, { hypotheses: s.hypotheses.map((x) => (x.key === h.key ? { ...x, label: e.target.value } : x)) })} />
                    </Field>
                    <Button type="button" variant="ghost" size="icon" aria-label="Remover alíquota hipotética" onClick={() => setScenario(s.key, { hypotheses: s.hypotheses.filter((x) => x.key !== h.key) })}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
                <div className="flex flex-wrap gap-2">
                  <Button type="button" variant="outline" size="sm" disabled={s.hypotheses.length >= 3} onClick={() => setScenario(s.key, { hypotheses: [...s.hypotheses, emptyHypothesis()] })}>
                    <Plus className="mr-1.5 h-3.5 w-3.5" /> Alíquota hipotética
                  </Button>
                  <Button type="button" variant="ghost" size="sm" disabled={scenarios.length === 1} onClick={() => setScenarios(scenarios.filter((x) => x.key !== s.key))}>
                    <Trash2 className="mr-1.5 h-3.5 w-3.5" /> Remover cenário
                  </Button>
                </div>
              </div>
            </fieldset>
          ))}
          <Button type="button" variant="outline" size="sm" disabled={scenarios.length >= 10} onClick={() => setScenarios([...scenarios, emptyScenario()])}>
            <Plus className="mr-1.5 h-3.5 w-3.5" /> Adicionar cenário
          </Button>
        </div>

        {touched && <ProblemList problems={problems} />}
        <Button type="submit" disabled={save.isPending}>{save.isPending ? "Simulando…" : "Simular"}</Button>
      </form>
    </SectionCard>
  );
}

function SupplierComparison() {
  const [date, setDate] = useState(brazilTodayIso());
  const [assumeCreditable, setAssumeCreditable] = useState(true);
  const [offers, setOffers] = useState<OfferDraft[]>(() => [emptyOffer(), emptyOffer()]);
  const [touched, setTouched] = useState(false);
  const [result, setResult] = useState<SupplierOfferResultDto[] | null>(null);
  const [profileMissing, setProfileMissing] = useState(false);
  const problems = offersProblems(offers);
  const compare = useMutation({
    mutationFn: () => compareSuppliers(toCompareRequest(date, offers, assumeCreditable)),
    onSuccess: setResult,
    onError: (e) => { if (isProfileMissing(e)) setProfileMissing(true); else toast.error(errorMessage(e)); },
  });
  const setOffer = (key: string, patch: Partial<OfferDraft>) => setOffers(offers.map((o) => (o.key === key ? { ...o, ...patch } : o)));

  return (
    <SectionCard title="Comparar fornecedores" description="O mesmo item de fornecedores diferentes: o mais barato é o que custa menos depois do crédito, não o de menor preço. Nada é guardado.">
      {profileMissing && <div className="mb-4"><ProfileMissingNotice /></div>}
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) compare.mutate(); }}>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <Field id="sup-date" label="Data da compra" className="sm:w-48"><Input id="sup-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></Field>
          <label htmlFor="sup-cred" className="flex items-center gap-2 text-sm">
            <Switch id="sup-cred" checked={assumeCreditable} onCheckedChange={setAssumeCreditable} />
            Considerar que o imposto da compra gera crédito
          </label>
        </div>
        {offers.map((o, i) => (
          <fieldset key={o.key} className="space-y-3 rounded-md border border-border p-3">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Fornecedor {i + 1}</legend>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field id={`${o.key}-name`} label="Nome"><Input id={`${o.key}-name`} maxLength={200} value={o.supplier} onChange={(e) => setOffer(o.key, { supplier: e.target.value })} /></Field>
              <Field id={`${o.key}-regime`} label="Regime do fornecedor">
                <Select value={o.regime || KEEP} onValueChange={(v) => setOffer(o.key, { regime: v === KEEP ? "" : (v as TaxRegime) })}>
                  <SelectTrigger id={`${o.key}-regime`}><SelectValue /></SelectTrigger>
                  <SelectContent><SelectItem value={KEEP}>Não sei</SelectItem>{TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
            </div>
            <LinesEditor idPrefix={o.key} lines={o.lines} onChange={(lines) => setOffer(o.key, { lines })} />
            <Button type="button" variant="ghost" size="sm" disabled={offers.length <= 2} onClick={() => setOffers(offers.filter((x) => x.key !== o.key))}>
              <Trash2 className="mr-1.5 h-3.5 w-3.5" /> Remover fornecedor
            </Button>
          </fieldset>
        ))}
        <Button type="button" variant="outline" size="sm" disabled={offers.length >= 10} onClick={() => setOffers([...offers, emptyOffer()])}>
          <Plus className="mr-1.5 h-3.5 w-3.5" /> Adicionar fornecedor
        </Button>
        {touched && <ProblemList problems={problems} />}
        <div><Button type="submit" disabled={compare.isPending}>{compare.isPending ? "Comparando…" : "Comparar"}</Button></div>
      </form>

      {result && (
        <div className="mt-5 space-y-3">
          <div className="overflow-x-auto rounded-md border border-border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Fornecedor</TableHead><TableHead className="text-right">Preço</TableHead><TableHead className="text-right">Imposto</TableHead>
                  <TableHead className="text-right">Crédito estimado</TableHead><TableHead className="text-right">Custo efetivo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.map((r) => (
                  <TableRow key={r.supplier}>
                    <TableCell className="min-w-32 font-medium">{r.supplier}{r.cheapest && <ToneBadge label="menor custo" tone="success" className="ml-2" />}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(r.charged)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(r.taxTotal)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(r.creditEstimate)}</TableCell>
                    <TableCell className="text-right tabular-nums font-medium">{formatCurrency(r.effectiveCost)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {result.some((r) => r.hypotheses.length > 0 || r.warnings.length > 0) && (
            <ul className="space-y-1 text-sm">
              {result.flatMap((r) => [
                ...r.hypotheses.map((h) => <li key={`${r.supplier}-h-${h}`} className="text-muted-foreground">{r.supplier}: {h}</li>),
                ...r.warnings.map((w) => <li key={`${r.supplier}-w-${w}`} className="text-warning">{r.supplier}: {w}</li>),
              ])}
            </ul>
          )}
        </div>
      )}
    </SectionCard>
  );
}
