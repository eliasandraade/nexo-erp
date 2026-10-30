import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import { STATES } from "@/modules/fiscal/lib/validation";
import { formatDateTime } from "@/lib/formatters";
import {
  TAX_KEY, TAX_KEYS, addTaxProfileVersion, getTaxProfile, type SimplesSituation, type TaxProfileDto, type TaxProfileVersionDto,
  type TaxRegime,
} from "../api/tax.api";
import { SIMPLES_SITUATIONS, regimeLabel } from "../lib/labels";
import { TaxQueryError, toastError } from "../components/TaxCommon";
import { ComplianceAssessments } from "../components/ComplianceAssessments";
import {
  brazilTodayIso, formatIsoDate, isSimplesRegime, profileDraftProblems, simplesSituationsFor, toProfileVersionRequest, type ProfileDraft,
} from "../lib/forms";
import { Field, ProblemList, TaxNote, TaxSubNav } from "../components/TaxShell";

function initialDraft(p: TaxProfileDto): ProfileDraft {
  const c = p.current;
  const s = p.suggestion;
  return {
    validFrom: brazilTodayIso(),
    regime: c?.regime ?? s?.regime ?? "SimplesNacional",
    state: c?.state ?? s?.state ?? "",
    cityIbgeCode: c?.cityIbgeCode ?? s?.cityIbgeCode ?? "",
    primaryCnae: c?.primaryCnae ?? "",
    secondaryCnaes: c?.secondaryCnaes.join(", ") ?? "",
    specialRegimes: c?.specialRegimes.join(", ") ?? "",
    benefits: c?.benefits.join("\n") ?? "",
    reason: "",
    simplesSituation: c?.simplesSituation ?? "",
    regularOption: optionOf(c?.ibsCbsRegularRegimeOption ?? null),
  };
}

function optionOf(v: boolean | null): ProfileDraft["regularOption"] {
  if (v === null) return "";
  return v ? "yes" : "no";
}

function regularOptionLabel(v: boolean | null) {
  if (v === null) return "Não informada — operações não são calculadas";
  return v ? "Optou pelo regime regular do IBS/CBS" : "Recolhe IBS/CBS pelo Simples";
}

/** Perfil tributário: os fatos que decidem as regras. Cada mudança é uma nova versão, as anteriores ficam guardadas. */
export default function TaxProfilePage() {
  const { session } = useAuth();
  const isDirector = session?.role === "diretoria";
  const profile = useQuery({ queryKey: TAX_KEYS.profile, queryFn: getTaxProfile });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Perfil tributário"
        description="Regime, UF, município, CNAE e regimes especiais do estabelecimento. É deles que depende cada regra aplicada."
      />
      <TaxSubNav />

      {profile.isLoading && <Skeleton className="h-64 w-full" />}
      {profile.isError && <TaxQueryError error={profile.error} what="o perfil tributário" onRetry={() => void profile.refetch()} />}
      {profile.data && (
        <>
          {profile.data.current ? <CurrentVersion v={profile.data.current} /> : (
            <TaxNote>Nenhum perfil cadastrado ainda. {profile.data.suggestion ? "Os dados do emissor (Fiscal) já preenchem regime, UF e município." : ""}</TaxNote>
          )}
          {isDirector
            ? <VersionForm key={profile.data.current?.id ?? "new"} profile={profile.data} />
            : <TaxNote>Só a diretoria registra uma nova versão do perfil tributário.</TaxNote>}
          <ComplianceAssessments items={profile.data.complianceAssessments} canRecord={isDirector} />
          <VersionHistory versions={profile.data.versions} />
        </>
      )}
    </div>
  );
}

function CurrentVersion({ v }: Readonly<{ v: TaxProfileVersionDto }>) {
  return (
    <SectionCard title={`Em vigor: versão ${v.versionNumber}`} description={`Desde ${formatIsoDate(v.validFrom)}`}>
      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
        <Item label="Regime">{regimeLabel(v.regime)}</Item>
        <Item label="Situação no Simples">{SIMPLES_SITUATIONS[v.simplesSituation]}</Item>
        {v.simplesSituation !== "NotOptant" && <Item label="IBS/CBS">{regularOptionLabel(v.ibsCbsRegularRegimeOption)}</Item>}
        <Item label="UF / município (IBGE)">{v.state} · {v.cityIbgeCode}</Item>
        <Item label="CNAE principal">{v.primaryCnae}</Item>
        <Item label="CNAEs secundários">{v.secondaryCnaes.join(", ") || "—"}</Item>
        <Item label="Regimes especiais">{v.specialRegimes.join(", ") || "—"}</Item>
        <Item label="Benefícios" className="sm:col-span-2 lg:col-span-3">{v.benefits.join("; ") || "—"}</Item>
      </dl>
    </SectionCard>
  );
}

function Item({ label, className, children }: Readonly<{ label: string; className?: string; children: React.ReactNode }>) {
  return (
    <div className={`min-w-0 ${className ?? ""}`}>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-words">{children}</dd>
    </div>
  );
}

function VersionForm({ profile }: Readonly<{ profile: TaxProfileDto }>) {
  const qc = useQueryClient();
  const [draft, setDraft] = useState<ProfileDraft>(() => initialDraft(profile));
  const [touched, setTouched] = useState(false);
  const problems = profileDraftProblems(draft);
  const save = useMutation({
    mutationFn: () => addTaxProfileVersion(toProfileVersionRequest(draft)),
    onSuccess: (v) => {
      toast.success(`Versão ${v.versionNumber} registrada, válida a partir de ${formatIsoDate(v.validFrom)}.`);
      void qc.invalidateQueries({ queryKey: TAX_KEY });
    },
    onError: toastError,
  });
  const text = (k: keyof ProfileDraft) => ({
    value: draft[k] as string,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft({ ...draft, [k]: e.target.value }),
  });

  return (
    <SectionCard
      title={profile.current ? "Registrar uma mudança" : "Cadastrar o perfil"}
      description={`Cria uma nova versão a partir de ${formatIsoDate(draft.validFrom)}; as anteriores ficam guardadas. Versões nunca são editadas.`}
    >
      <form
        className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"
        onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}
      >
        <Field id="tp-from" label="Vale a partir de"><Input id="tp-from" type="date" {...text("validFrom")} /></Field>
        <Field id="tp-regime" label="Regime">
          <Select value={draft.regime} onValueChange={(v) => {
            const regime = v as TaxRegime;
            setDraft({ ...draft, regime, simplesSituation: "", regularOption: isSimplesRegime(regime) ? draft.regularOption : "" });
          }}>
            <SelectTrigger id="tp-regime"><SelectValue /></SelectTrigger>
            <SelectContent>{TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        {isSimplesRegime(draft.regime) && (
          <>
            <Field id="tp-simples" label="Situação no Simples" hint="Sublimite, MEI e SIMEI mudam o tratamento — na dúvida, deixe “não informada”.">
              <Select value={draft.simplesSituation || "__derived"} onValueChange={(v) => setDraft({ ...draft, simplesSituation: v === "__derived" ? "" : (v as SimplesSituation) })}>
                <SelectTrigger id="tp-simples"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="__derived">Conforme o regime</SelectItem>
                  {simplesSituationsFor(draft.regime).map((s) => <SelectItem key={s} value={s}>{SIMPLES_SITUATIONS[s]}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field id="tp-regular" label="Opção pelo regime regular do IBS/CBS" hint="Sem essa informação, as operações ficam fora do recorte calculado.">
              <Select value={draft.regularOption || "__unknown"} onValueChange={(v) => setDraft({ ...draft, regularOption: v === "__unknown" ? "" : (v as "yes" | "no") })}>
                <SelectTrigger id="tp-regular"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="__unknown">Não informada</SelectItem>
                  <SelectItem value="no">Recolhe IBS/CBS pelo Simples</SelectItem>
                  <SelectItem value="yes">Optou pelo regime regular</SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </>
        )}
        <Field id="tp-uf" label="UF">
          <Select value={draft.state} onValueChange={(v) => setDraft({ ...draft, state: v })}>
            <SelectTrigger id="tp-uf"><SelectValue placeholder="Escolha" /></SelectTrigger>
            <SelectContent>{STATES.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id="tp-ibge" label="Município (código IBGE)"><Input id="tp-ibge" inputMode="numeric" placeholder="3550308" {...text("cityIbgeCode")} /></Field>
        <Field id="tp-cnae" label="CNAE principal"><Input id="tp-cnae" inputMode="numeric" placeholder="4781-4/00" {...text("primaryCnae")} /></Field>
        <Field id="tp-cnae2" label="CNAEs secundários" hint="Separe por vírgula." className="sm:col-span-1 lg:col-span-3">
          <Input id="tp-cnae2" {...text("secondaryCnaes")} />
        </Field>
        <Field id="tp-special" label="Regimes especiais" hint="Separe por vírgula. Deixe vazio se não houver." className="sm:col-span-2">
          <Input id="tp-special" {...text("specialRegimes")} />
        </Field>
        <Field id="tp-benefits" label="Benefícios fiscais" hint="Um por linha." className="sm:col-span-2 lg:col-span-4">
          <Textarea id="tp-benefits" rows={2} {...text("benefits")} />
        </Field>
        <Field id="tp-reason" label="Motivo da nova versão" className="sm:col-span-2 lg:col-span-4">
          <Input id="tp-reason" maxLength={500} placeholder="Ex.: mudança de regime para Lucro Presumido" {...text("reason")} />
        </Field>
        <div className="sm:col-span-2 lg:col-span-4">{touched && <ProblemList problems={problems} />}</div>
        <div className="sm:col-span-2 lg:col-span-4">
          <Button type="submit" disabled={save.isPending}>{save.isPending ? "Registrando…" : "Registrar nova versão"}</Button>
        </div>
      </form>
    </SectionCard>
  );
}

function VersionHistory({ versions }: Readonly<{ versions: TaxProfileVersionDto[] }>) {
  if (versions.length === 0) return null;
  return (
    <SectionCard title="Histórico de versões" description="Cada cálculo guarda a versão do perfil que usou.">
      <ol className="space-y-3">
        {versions.map((v) => (
          <li key={v.id} className="flex gap-3 text-sm">
            <History className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
            <div className="min-w-0">
              <p className="font-medium">Versão {v.versionNumber} · a partir de {formatIsoDate(v.validFrom)} · {regimeLabel(v.regime)} · {SIMPLES_SITUATIONS[v.simplesSituation]} · {v.state}</p>
              <p className="break-words text-muted-foreground">{v.reason}</p>
              <p className="text-xs text-muted-foreground">Por {v.createdByName} em {formatDateTime(v.createdAt)}</p>
            </div>
          </li>
        ))}
      </ol>
    </SectionCard>
  );
}
