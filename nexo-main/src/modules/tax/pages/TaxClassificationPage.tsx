import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Tags } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateTime } from "@/lib/formatters";
import {
  TAX_KEY, TAX_KEYS, listClassificationCodes, listItemClassifications, setItemClassification,
  type TaxItemClassificationDto, type TaxItemKind,
} from "../api/tax.api";
import { CLASSIFICATION_STATUS, ITEM_KINDS, PROFESSIONAL_REVIEW, labelOf, toneOf } from "../lib/labels";
import { ToneBadge } from "../components/TaxBadges";
import { Field, ProblemList, TaxNote, TaxSubNav } from "../components/TaxShell";
import { TaxPager, TaxQueryError, toastError } from "../components/TaxCommon";

const PAGE = 50;

/**
 * Classificação tributária dos produtos (cClassTrib, NCM/NBS). Um produto sem classificação, ou com um código fora do catálogo
 * ativo na data da operação, fica fora do recorte calculado — o sistema nunca adivinha a classificação.
 */
export default function TaxClassificationPage() {
  const [search, setSearch] = useState("");
  const [onlyMissing, setOnlyMissing] = useState(false);
  const [skip, setSkip] = useState(0);
  const [editing, setEditing] = useState<TaxItemClassificationDto | null>(null);
  const items = useQuery({
    queryKey: TAX_KEYS.items(search, onlyMissing, skip),
    queryFn: () => listItemClassifications(search, onlyMissing, skip, PAGE),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Classificação dos produtos"
        description="O código de classificação tributária (cClassTrib) de cada produto. Sem ele, ou com um código fora do catálogo ativo, a operação não é calculada."
      />
      <TaxSubNav />
      <TaxNote>
        O catálogo de códigos é mantido pela plataforma a partir da tabela oficial. Escolher o código é responsabilidade da empresa — na dúvida, consulte o seu contador.
      </TaxNote>

      <div className="flex flex-wrap items-end gap-3">
        <Field id="cl-search" label="Buscar produto" className="w-full sm:w-72">
          <Input id="cl-search" value={search} onChange={(e) => { setSearch(e.target.value); setSkip(0); }} placeholder="Nome do produto" />
        </Field>
        <label htmlFor="cl-missing" className="flex items-center gap-2 pb-2 text-sm">
          <Checkbox id="cl-missing" checked={onlyMissing} onCheckedChange={(v) => { setOnlyMissing(v === true); setSkip(0); }} /> Só sem classificação
        </label>
      </div>

      <SectionCard noPadding>
        {items.isLoading && <div className="p-5"><Skeleton className="h-40 w-full" /></div>}
        {items.isError && <TaxQueryError error={items.error} what="as classificações" onRetry={() => void items.refetch()} />}
        {items.data?.items.length === 0 && (
          <EmptyState icon={Tags} compact title={search || onlyMissing ? "Nenhum produto com estes filtros" : "Nenhum produto cadastrado"} />
        )}
        {items.data && items.data.items.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Produto</TableHead><TableHead>Tipo</TableHead><TableHead>NCM / NBS</TableHead><TableHead>cClassTrib</TableHead>
                    <TableHead>No catálogo</TableHead><TableHead><span className="sr-only">Ações</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.data.items.map((i) => (
                    <TableRow key={i.productId}>
                      <TableCell className="min-w-48">
                        <p className="font-medium">{i.productName}</p>
                        {i.setByName && <p className="text-xs text-muted-foreground">{i.setByName}{i.updatedAt ? ` · ${formatDateTime(i.updatedAt)}` : ""}</p>}
                      </TableCell>
                      <TableCell>{i.itemKind ? ITEM_KINDS[i.itemKind] : "—"}</TableCell>
                      <TableCell className="font-mono text-xs">{i.ncm ?? i.productNcm ?? "—"}{i.nbs ? ` / ${i.nbs}` : ""}</TableCell>
                      <TableCell className="font-mono text-xs">{i.cClassTrib ?? "—"}</TableCell>
                      <TableCell>
                        {i.cClassTrib === null && <ToneBadge label="Sem classificação" tone="warning" />}
                        {i.cClassTrib !== null && i.catalogStatus === null && <ToneBadge label="Fora do catálogo" tone="danger" />}
                        {i.catalogStatus && <ToneBadge label={labelOf(CLASSIFICATION_STATUS, i.catalogStatus)} tone={toneOf(CLASSIFICATION_STATUS, i.catalogStatus)} />}
                      </TableCell>
                      <TableCell><Button type="button" size="sm" variant="outline" onClick={() => setEditing(i)}>{i.cClassTrib ? "Alterar" : "Classificar"}</Button></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <TaxPager total={items.data.total} skip={skip} take={PAGE} onSkip={setSkip} />
          </>
        )}
      </SectionCard>
      {editing && <ClassifyDialog item={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function ClassifyDialog({ item, onClose }: Readonly<{ item: TaxItemClassificationDto; onClose: () => void }>) {
  const qc = useQueryClient();
  const [kind, setKind] = useState<TaxItemKind>(item.itemKind ?? "Good");
  const [ncm, setNcm] = useState(item.ncm ?? item.productNcm ?? "");
  const [nbs, setNbs] = useState(item.nbs ?? "");
  const [code, setCode] = useState(item.cClassTrib ?? "");
  const [codeSearch, setCodeSearch] = useState("");
  const [notes, setNotes] = useState(item.notes ?? "");
  const [touched, setTouched] = useState(false);
  const codes = useQuery({ queryKey: TAX_KEYS.codes(codeSearch), queryFn: () => listClassificationCodes(codeSearch) });
  const chosen = codes.data?.items.find((c) => c.code === code);
  const problems = [
    ...(code.trim() ? [] : ["Escolha o cClassTrib."]),
    ...(kind === "Good" && ncm.replace(/\D/g, "").length !== 8 && ncm.trim() !== "" ? ["NCM: 8 números."] : []),
    ...(kind === "Service" && !nbs.trim() ? ["Serviço: informe a NBS."] : []),
  ];
  const save = useMutation({
    mutationFn: () => setItemClassification(item.productId, {
      itemKind: kind, ncm: ncm.trim() || null, nbs: nbs.trim() || null, cClassTrib: code.trim(), notes: notes.trim() || null,
    }),
    onSuccess: (r) => {
      toast.success(r.catalogStatus === "Active" ? "Classificação registrada." : "Classificação registrada — mas o código não está ativo no catálogo: a operação continua fora do recorte.");
      void qc.invalidateQueries({ queryKey: [...TAX_KEY, "classification"] });
      onClose();
    },
    onError: toastError,
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Classificar “{item.productName}”</DialogTitle>
          <DialogDescription>A mudança fica registrada na auditoria. Cálculos já feitos mantêm a classificação que usaram.</DialogDescription>
        </DialogHeader>
        <form id="classify-form" className="grid gap-3 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); setTouched(true); if (problems.length === 0) save.mutate(); }}>
          <Field id="cl-kind" label="Tipo">
            <Select value={kind} onValueChange={(v) => setKind(v as TaxItemKind)}>
              <SelectTrigger id="cl-kind"><SelectValue /></SelectTrigger>
              <SelectContent>{(Object.keys(ITEM_KINDS) as TaxItemKind[]).map((k) => <SelectItem key={k} value={k}>{ITEM_KINDS[k]}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          {kind === "Good"
            ? <Field id="cl-ncm" label="NCM"><Input id="cl-ncm" inputMode="numeric" maxLength={10} value={ncm} onChange={(e) => setNcm(e.target.value)} /></Field>
            : <Field id="cl-nbs" label="NBS"><Input id="cl-nbs" maxLength={20} value={nbs} onChange={(e) => setNbs(e.target.value)} /></Field>}
          <Field id="cl-code-search" label="Procurar no catálogo" className="sm:col-span-2">
            <Input id="cl-code-search" value={codeSearch} onChange={(e) => setCodeSearch(e.target.value)} placeholder="Código ou descrição" />
          </Field>
          <div className="max-h-48 overflow-y-auto rounded-md border border-border sm:col-span-2" role="listbox" aria-label="Códigos do catálogo">
            {codes.isLoading && <p className="p-2 text-xs text-muted-foreground">Carregando…</p>}
            {codes.isError && <p className="p-2 text-xs text-destructive">Não foi possível carregar o catálogo.</p>}
            {codes.data?.items.length === 0 && <p className="p-2 text-xs text-muted-foreground">Nenhum código ativo encontrado. A plataforma ainda não importou o catálogo?</p>}
            {codes.data?.items.map((c) => (
              <button key={c.id} type="button" role="option" aria-selected={c.code === code} onClick={() => setCode(c.code)}
                className={`block w-full px-2 py-1.5 text-left text-xs hover:bg-muted ${c.code === code ? "bg-muted font-semibold" : ""}`}>
                <span className="font-mono">{c.code}</span> — {c.description}
                <span className="ml-1 text-muted-foreground">({labelOf(PROFESSIONAL_REVIEW, c.professionalReview)})</span>
              </button>
            ))}
          </div>
          <Field id="cl-code" label="cClassTrib escolhido">
            <Input id="cl-code" className="font-mono" maxLength={10} value={code} onChange={(e) => setCode(e.target.value)} />
          </Field>
          <div className="text-xs text-muted-foreground sm:pt-6">{chosen ? `${chosen.source}${chosen.sourceVersion ? ` · ${chosen.sourceVersion}` : ""}` : "Digite ou escolha da lista."}</div>
          <Field id="cl-notes" label="Observação (opcional)" className="sm:col-span-2"><Input id="cl-notes" maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
          <div className="sm:col-span-2">{touched && <ProblemList problems={problems} />}</div>
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button type="submit" form="classify-form" disabled={save.isPending}>{save.isPending ? "Salvando…" : "Salvar classificação"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
