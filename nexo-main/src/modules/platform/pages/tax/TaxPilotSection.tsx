import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatDateTime } from "@/lib/formatters";
import { formatIsoDate } from "@/modules/tax/lib/forms";
import { Field, ProblemList } from "@/modules/tax/components/TaxShell";
import { TaxQueryError, toastError, useReasonDialog } from "@/modules/tax/components/TaxCommon";
import { fetchPlatformTenants } from "../../services/platformApi";
import {
  PLATFORM_TAX_KEY, PLATFORM_TAX_KEYS, addPilotParticipant, configurePilotScope, getPilotScope, removePilotParticipant, type TaxPilotScopeDto,
} from "../../services/taxAdminApi";

const WHOLE_COMPANY = "__all";

function scopeWindow(scope: TaxPilotScopeDto): string {
  if (!scope.validFrom) return "";
  const end = scope.validTo ? ` a ${formatIsoDate(scope.validTo)}` : " em diante";
  return ` · ${formatIsoDate(scope.validFrom)}${end}`;
}

/** The controlled pilot's scope: the window and the companies (or stores) admitted. Nothing here activates the module. */
export function TaxPilotSection() {
  const scope = useQuery({ queryKey: PLATFORM_TAX_KEYS.pilot, queryFn: getPilotScope });
  const tenants = useQuery({ queryKey: ["platform", "tenants"], queryFn: fetchPlatformTenants, staleTime: 60_000 });
  const [reasonDialog, askReason] = useReasonDialog();
  const qc = useQueryClient();
  const remove = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => removePilotParticipant(id, reason),
    onSuccess: () => { toast.success("Participante retirado do piloto."); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); },
    onError: toastError,
  });
  const tenantName = (id: string) => tenants.data?.find((t) => t.id === id)?.tradeName ?? tenants.data?.find((t) => t.id === id)?.companyName ?? id;
  const storeName = (tenantId: string, storeId: string | null) =>
    storeId ? tenants.data?.find((t) => t.id === tenantId)?.stores.find((s) => s.id === storeId)?.name ?? storeId : "empresa inteira";

  return (
    <div className="space-y-6">
      {reasonDialog}
      <SectionCard title="Escopo do piloto" description="Janela e referência da decisão. Só empresas listadas abaixo trabalham no piloto controlado.">
        {scope.isLoading && <Skeleton className="h-24 w-full" />}
        {scope.isError && <TaxQueryError error={scope.error} what="o escopo do piloto" onRetry={() => void scope.refetch()} />}
        {scope.data && (
          <div className="space-y-4 text-sm">
            <p>
              {scope.data.enabled ? "Escopo ligado" : "Escopo desligado"}
              {scopeWindow(scope.data)}
              {scope.data.decisionReference ? ` · ${scope.data.decisionReference}` : ""}
              {scope.data.activatedByName ? ` · ${scope.data.activatedByName} em ${formatDateTime(scope.data.activatedAt ?? "")}` : ""}
            </p>
            <ScopeForm key={scope.data.id ?? "new"} scope={scope.data} />
          </div>
        )}
      </SectionCard>

      <SectionCard title="Participantes">
        {scope.data && (
          <div className="space-y-4">
            {scope.data.participants.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma empresa no piloto.</p>}
            <ul className="space-y-2">
              {scope.data.participants.map((p) => (
                <li key={p.id} className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border p-2 text-sm">
                  <div>
                    <p className="font-medium">{tenantName(p.tenantId)} · {storeName(p.tenantId, p.storeId)}</p>
                    <p className="text-xs text-muted-foreground">
                      {p.active ? "Ativo" : `Retirado por ${p.removedByName} em ${formatDateTime(p.removedAt ?? "")}: ${p.removalReason}`} · incluído por {p.addedByName} em {formatDateTime(p.addedAt)}
                    </p>
                  </div>
                  {p.active && (
                    <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={() => askReason({
                      title: "Retirar do piloto", description: "A empresa deixa de trabalhar no piloto a partir de agora; o histórico fica.", confirmLabel: "Retirar", danger: true,
                      onConfirm: (reason) => remove.mutate({ id: p.id, reason }),
                    })}>Retirar</Button>
                  )}
                </li>
              ))}
            </ul>
            <AddParticipant tenants={tenants.data ?? []} />
          </div>
        )}
      </SectionCard>
    </div>
  );
}

function ScopeForm({ scope }: Readonly<{ scope: TaxPilotScopeDto }>) {
  const qc = useQueryClient();
  const [enabled, setEnabled] = useState(scope.enabled);
  const [from, setFrom] = useState(scope.validFrom ?? "");
  const [to, setTo] = useState(scope.validTo ?? "");
  const [reference, setReference] = useState("");
  const [touched, setTouched] = useState(false);
  const problems = [
    ...(/^\d{4}-\d{2}-\d{2}$/.test(from) ? [] : ["Informe o início."]),
    ...(to && to < from ? ["O fim é anterior ao início."] : []),
    ...(reference.trim() ? [] : ["Informe a referência da decisão."]),
  ];
  const save = useMutation({
    mutationFn: () => configurePilotScope({ enabled, validFrom: from, validTo: to || null, decisionReference: reference.trim() }),
    onSuccess: () => { toast.success("Escopo do piloto registrado."); setReference(""); setTouched(false); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); },
    onError: toastError,
  });
  return (
    <form className="grid gap-3 rounded-md border border-dashed border-border p-3 sm:grid-cols-2 lg:grid-cols-4"
      onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
      <Field id="ps-from" label="Início"><Input id="ps-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
      <Field id="ps-to" label="Fim (opcional)"><Input id="ps-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
      <Field id="ps-ref" label="Referência da decisão" className="lg:col-span-2"><Input id="ps-ref" maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} /></Field>
      <label htmlFor="ps-enabled" className="flex items-center gap-2 text-sm sm:col-span-2 lg:col-span-4">
        <Checkbox id="ps-enabled" checked={enabled} onCheckedChange={(v) => setEnabled(v === true)} /> Escopo ligado
      </label>
      <div className="sm:col-span-2 lg:col-span-4">{touched && <ProblemList problems={problems} />}</div>
      <div className="sm:col-span-2 lg:col-span-4"><Button type="submit" disabled={save.isPending}>Registrar escopo</Button></div>
    </form>
  );
}

function AddParticipant({ tenants }: Readonly<{ tenants: { id: string; companyName: string; tradeName?: string; stores: { id: string; name: string }[] }[] }>) {
  const qc = useQueryClient();
  const [tenantId, setTenantId] = useState("");
  const [storeId, setStoreId] = useState(WHOLE_COMPANY);
  const stores = tenants.find((t) => t.id === tenantId)?.stores ?? [];
  const add = useMutation({
    mutationFn: () => addPilotParticipant(tenantId, storeId === WHOLE_COMPANY ? null : storeId),
    onSuccess: () => { toast.success("Empresa incluída no piloto."); setTenantId(""); setStoreId(WHOLE_COMPANY); void qc.invalidateQueries({ queryKey: PLATFORM_TAX_KEY }); },
    onError: toastError,
  });
  return (
    <form className="grid gap-3 border-t border-border pt-3 sm:grid-cols-3" onSubmit={(e) => { e.preventDefault(); if (tenantId) add.mutate(); }}>
      <Field id="pp-tenant" label="Empresa">
        <Select value={tenantId} onValueChange={(v) => { setTenantId(v); setStoreId(WHOLE_COMPANY); }}>
          <SelectTrigger id="pp-tenant"><SelectValue placeholder="Escolha" /></SelectTrigger>
          <SelectContent>{tenants.map((t) => <SelectItem key={t.id} value={t.id}>{t.tradeName || t.companyName}</SelectItem>)}</SelectContent>
        </Select>
      </Field>
      <Field id="pp-store" label="Loja">
        <Select value={storeId} onValueChange={setStoreId} disabled={!tenantId}>
          <SelectTrigger id="pp-store"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={WHOLE_COMPANY}>Empresa inteira</SelectItem>
            {stores.map((s) => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      <div className="flex items-end"><Button type="submit" variant="outline" disabled={!tenantId || add.isPending}>Incluir no piloto</Button></div>
    </form>
  );
}
