import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, FileWarning, Info, ScanBarcode } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { MetricBlock } from "@/components/shared/MetricBlock";
import { ErrorState } from "@/components/shared/ErrorState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  FISCAL_KEY, TAX_REGIMES, getFiscalProfile, getFiscalReadiness, saveFiscalProfile,
  type FiscalProfileDto, type FiscalProfileInput, type TaxRegime,
} from "../api/fiscal.api";
import { ProductFiscalDialog } from "../components/ProductFiscalDialog";
import { STATES, profileProblems } from "../lib/validation";


const EMPTY: FiscalProfileInput = {
  legalName: "", tradeName: null, cnpj: "", stateRegistration: "", municipalRegistration: null, taxRegime: "SimplesNacional",
  street: "", number: "", complement: null, district: "", cityName: "", cityIbgeCode: "", state: "", zipCode: "",
  operationNature: "Venda de mercadoria", nfceSeries: null, nfceNextNumber: null, nfeSeries: null, nfeNextNumber: null,
};

function toInput(p: FiscalProfileDto): FiscalProfileInput {
  return {
    legalName: p.legalName, tradeName: p.tradeName, cnpj: p.cnpj, stateRegistration: p.stateRegistration,
    municipalRegistration: p.municipalRegistration, taxRegime: p.taxRegime, street: p.street, number: p.number,
    complement: p.complement, district: p.district, cityName: p.cityName, cityIbgeCode: p.cityIbgeCode, state: p.state,
    zipCode: p.zipCode, operationNature: p.operationNature, nfceSeries: p.nfceSeries, nfceNextNumber: p.nfceNextNumber,
    nfeSeries: p.nfeSeries, nfeNextNumber: p.nfeNextNumber,
  };
}

/** "" → null, otherwise the integer typed (NaN is kept so validation can complain). */
const toNumber = (v: string) => (v.trim() === "" ? null : Number(v));

/**
 * Fiscal (base): os dados que a nota fiscal vai precisar — o emissor desta loja e os dados fiscais
 * dos produtos. A emissão (NFC-e/NF-e) ainda não está disponível; aqui só se prepara o terreno.
 */
export default function FiscalPage() {
  const [fixing, setFixing] = useState<string | null>(null);
  const readiness = useQuery({ queryKey: [...FISCAL_KEY, "readiness"], queryFn: getFiscalReadiness });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Fiscal"
        description="Deixe a loja pronta para a nota fiscal: dados do emissor e dados fiscais dos produtos."
      />

      <p className="flex items-start gap-2 rounded-md border border-dashed p-3 text-sm text-muted-foreground">
        <Info className="mt-0.5 h-4 w-4 shrink-0" />
        A emissão de NFC-e e NF-e ainda não está disponível no Orken. Os dados preenchidos aqui ficam guardados para quando ela chegar; nada é enviado à Sefaz.
      </p>

      {readiness.isLoading && <Skeleton className="h-24 w-full" />}
      {readiness.isError && <ErrorState title="Não foi possível conferir a preparação fiscal" onRetry={() => void readiness.refetch()} />}
      {readiness.data && (
        <div className="grid gap-4 sm:grid-cols-3">
          <MetricBlock label="Emissor" value={readiness.data.profileComplete ? "Preenchido" : "Falta preencher"} icon={Building2}
            variant={readiness.data.profileComplete ? "success" : "warning"} />
          <MetricBlock label="Produtos sem NCM" value={readiness.data.productsWithoutNcm} icon={FileWarning}
            caption={`de ${readiness.data.activeProducts} produtos à venda`} variant={readiness.data.productsWithoutNcm > 0 ? "warning" : "success"} />
          <MetricBlock label="Código de barras fora do padrão" value={readiness.data.productsWithInvalidBarcode} icon={ScanBarcode}
            caption="vão como SEM GTIN na nota" />
        </div>
      )}

      <ProfileSection />

      <SectionCard title="Produtos para revisar" description="Sem NCM ou com código de barras que não é um GTIN. Mostra até 50 por vez.">
        {readiness.data && readiness.data.productsToFix.length === 0 && (
          <p className="text-sm text-muted-foreground">Nenhum produto pendente.</p>
        )}
        {readiness.data && readiness.data.productsToFix.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow><TableHead>Código</TableHead><TableHead>Produto</TableHead><TableHead>O que falta</TableHead><TableHead className="w-0" /></TableRow>
            </TableHeader>
            <TableBody>
              {readiness.data.productsToFix.map((p) => (
                <TableRow key={p.productId}>
                  <TableCell className="whitespace-nowrap font-mono text-xs">{p.code}</TableCell>
                  <TableCell className="max-w-[16rem] truncate">{p.name}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{p.problems.join(" · ")}</TableCell>
                  <TableCell>
                    <Button size="sm" variant="outline" onClick={() => setFixing(p.productId)}>Corrigir<span className="sr-only"> {p.name}</span></Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <ProductFiscalDialog productId={fixing} open={fixing !== null} onOpenChange={(o) => { if (!o) setFixing(null); }} />
    </div>
  );
}

function ProfileSection() {
  const qc = useQueryClient();
  const [form, setForm] = useState<FiscalProfileInput>(EMPTY);
  const [touched, setTouched] = useState(false);
  const profile = useQuery({ queryKey: [...FISCAL_KEY, "profile"], queryFn: getFiscalProfile, refetchOnWindowFocus: false });

  useEffect(() => {
    if (profile.data) setForm(toInput(profile.data));
  }, [profile.data]);

  const save = useMutation({
    mutationFn: () => saveFiscalProfile(form),
    onSuccess: () => {
      toast.success("Dados do emissor salvos.");
      void qc.invalidateQueries({ queryKey: FISCAL_KEY });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const problems = profileProblems(form);
  const numeric = (key: "nfceSeries" | "nfceNextNumber" | "nfeSeries" | "nfeNextNumber") => ({
    value: form[key] === null ? "" : String(form[key]),
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: toNumber(e.target.value) }),
  });
  const text = (key: keyof FiscalProfileInput) => ({
    value: (form[key] as string | null) ?? "",
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value }),
  });

  if (profile.isLoading) return <Skeleton className="h-64 w-full" />;
  if (profile.isError) return <ErrorState title="Não foi possível ler os dados do emissor" onRetry={() => void profile.refetch()} />;

  return (
    <SectionCard title="Emissor desta loja" description="Cada loja (filial) tem o seu CNPJ e a sua inscrição estadual.">
      <form
        className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"
        onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}
      >
        <Field id="fp-legal" label="Razão social" className="sm:col-span-2"><Input id="fp-legal" {...text("legalName")} /></Field>
        <Field id="fp-trade" label="Nome fantasia" className="sm:col-span-2"><Input id="fp-trade" {...text("tradeName")} /></Field>
        <Field id="fp-cnpj" label="CNPJ"><Input id="fp-cnpj" inputMode="numeric" placeholder="00.000.000/0000-00" {...text("cnpj")} /></Field>
        <Field id="fp-ie" label="Inscrição estadual"><Input id="fp-ie" placeholder="Números ou ISENTO" {...text("stateRegistration")} /></Field>
        <Field id="fp-im" label="Inscrição municipal"><Input id="fp-im" {...text("municipalRegistration")} /></Field>
        <Field id="fp-regime" label="Regime tributário">
          <Select value={form.taxRegime} onValueChange={(v) => setForm({ ...form, taxRegime: v as TaxRegime })}>
            <SelectTrigger id="fp-regime"><SelectValue /></SelectTrigger>
            <SelectContent>{TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id="fp-street" label="Logradouro" className="sm:col-span-2"><Input id="fp-street" {...text("street")} /></Field>
        <Field id="fp-number" label="Número"><Input id="fp-number" placeholder="S/N" {...text("number")} /></Field>
        <Field id="fp-comp" label="Complemento"><Input id="fp-comp" {...text("complement")} /></Field>
        <Field id="fp-district" label="Bairro"><Input id="fp-district" {...text("district")} /></Field>
        <Field id="fp-city" label="Município"><Input id="fp-city" {...text("cityName")} /></Field>
        <Field id="fp-ibge" label="Código IBGE do município"><Input id="fp-ibge" inputMode="numeric" placeholder="3550308" {...text("cityIbgeCode")} /></Field>
        <Field id="fp-uf" label="UF">
          <Select value={form.state || undefined} onValueChange={(v) => setForm({ ...form, state: v })}>
            <SelectTrigger id="fp-uf"><SelectValue placeholder="Escolha" /></SelectTrigger>
            <SelectContent>{STATES.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id="fp-cep" label="CEP"><Input id="fp-cep" inputMode="numeric" placeholder="00000-000" {...text("zipCode")} /></Field>
        <Field id="fp-nature" label="Natureza da operação" className="sm:col-span-2 lg:col-span-4">
          <Input id="fp-nature" maxLength={60} {...text("operationNature")} />
        </Field>
        <Field id="fp-nfce-series" label="Série da NFC-e"><Input id="fp-nfce-series" type="number" min={0} max={889} {...numeric("nfceSeries")} /></Field>
        <Field id="fp-nfce-next" label="Próximo número da NFC-e"><Input id="fp-nfce-next" type="number" min={1} {...numeric("nfceNextNumber")} /></Field>
        <Field id="fp-nfe-series" label="Série da NF-e"><Input id="fp-nfe-series" type="number" min={0} max={889} {...numeric("nfeSeries")} /></Field>
        <Field id="fp-nfe-next" label="Próximo número da NF-e"><Input id="fp-nfe-next" type="number" min={1} {...numeric("nfeNextNumber")} /></Field>
        <p className="text-xs text-muted-foreground sm:col-span-2 lg:col-span-4">
          Séries e numeração ficam guardadas para quando a emissão começar. Se a loja já emite por outro sistema, use a próxima numeração dele.
        </p>
        {touched && problems.length > 0 && (
          <ul className="list-disc pl-5 text-sm text-destructive sm:col-span-2 lg:col-span-4">
            {problems.map((p) => <li key={p}>{p}</li>)}
          </ul>
        )}
        <div className="sm:col-span-2 lg:col-span-4">
          <Button type="submit" disabled={save.isPending}>{save.isPending ? "Salvando…" : "Salvar dados do emissor"}</Button>
        </div>
      </form>
    </SectionCard>
  );
}

function Field({ id, label, className, children }: Readonly<{ id: string; label: string; className?: string; children: React.ReactNode }>) {
  return (
    <div className={`space-y-1 ${className ?? ""}`}>
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  );
}
