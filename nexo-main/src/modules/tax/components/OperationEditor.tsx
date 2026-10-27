import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import { STATES } from "@/modules/fiscal/lib/validation";
import type { TaxDirection, TaxItemKind, TaxRegime } from "../api/tax.api";
import { DIRECTIONS, ITEM_KINDS } from "../lib/labels";
import { emptyLine, type LineDraft, type OperationDraft } from "../lib/forms";
import { Field } from "./TaxShell";

const NONE = "__none";

/** Editor of the lines of an operation (add/remove), with the discount rules explained next to the fields. */
export function LinesEditor({ idPrefix, lines, onChange }: Readonly<{ idPrefix: string; lines: LineDraft[]; onChange: (lines: LineDraft[]) => void }>) {
  const set = (key: string, patch: Partial<LineDraft>) => onChange(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  return (
    <div className="space-y-3">
      {lines.map((line, i) => {
        const id = `${idPrefix}-${line.key}`;
        const text = (k: keyof LineDraft) => ({
          value: line[k] as string,
          onChange: (e: React.ChangeEvent<HTMLInputElement>) => set(line.key, { [k]: e.target.value }),
        });
        return (
          <fieldset key={line.key} className="rounded-md border border-border p-3">
            <legend className="px-1 text-xs font-medium text-muted-foreground">Linha {i + 1}</legend>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Field id={`${id}-desc`} label="Item" className="sm:col-span-2">
                <Input id={`${id}-desc`} maxLength={300} placeholder="Ex.: camiseta algodão" {...text("description")} />
              </Field>
              <Field id={`${id}-kind`} label="Tipo">
                <Select value={line.itemKind} onValueChange={(v) => set(line.key, { itemKind: v as TaxItemKind })}>
                  <SelectTrigger id={`${id}-kind`}><SelectValue /></SelectTrigger>
                  <SelectContent>{(Object.keys(ITEM_KINDS) as TaxItemKind[]).map((k) => <SelectItem key={k} value={k}>{ITEM_KINDS[k]}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              <Field id={`${id}-ncm`} label="NCM (opcional)">
                <Input id={`${id}-ncm`} inputMode="numeric" placeholder="0000.00.00" {...text("ncm")} />
              </Field>
              <Field id={`${id}-qty`} label="Quantidade">
                <Input id={`${id}-qty`} inputMode="decimal" {...text("quantity")} />
              </Field>
              <Field id={`${id}-gross`} label="Valor da linha (R$)" hint="Preço total antes dos descontos.">
                <Input id={`${id}-gross`} inputMode="decimal" placeholder="0,00" {...text("grossAmount")} />
              </Field>
              <Field id={`${id}-uncond`} label="Desconto incondicional (R$)" hint="Consta da nota e não depende de nada depois — sai da base.">
                <Input id={`${id}-uncond`} inputMode="decimal" placeholder="0,00" {...text("unconditionalDiscount")} />
              </Field>
              <Field id={`${id}-cond`} label="Desconto condicional (R$)" hint="Depende de evento futuro (ex.: pagar em dia) — entra na base.">
                <Input id={`${id}-cond`} inputMode="decimal" placeholder="0,00" {...text("conditionalDiscount")} />
              </Field>
              <Field id={`${id}-sur`} label="Acréscimos (R$)" hint="Frete, juros, seguro cobrados junto.">
                <Input id={`${id}-sur`} inputMode="decimal" placeholder="0,00" {...text("surcharges")} />
              </Field>
              <div className="min-w-0 space-y-1 sm:col-span-2">
                <label htmlFor={`${id}-excl`} className="text-sm font-medium leading-none">ICMS/ISS/PIS/Cofins no preço (R$)</label>
                <div className="flex flex-wrap items-center gap-3">
                  <Input
                    id={`${id}-excl`} inputMode="decimal" placeholder={line.excludedTaxesUnknown ? "Não sei" : "0,00"} className="w-40"
                    disabled={line.excludedTaxesUnknown} {...text("excludedTaxes")}
                  />
                  <label htmlFor={`${id}-unknown`} className="flex items-center gap-2 text-sm">
                    <Checkbox
                      id={`${id}-unknown`} checked={line.excludedTaxesUnknown}
                      onCheckedChange={(c) => set(line.key, { excludedTaxesUnknown: c === true, excludedTaxes: c === true ? "" : line.excludedTaxes })}
                    />
                    Não sei
                  </label>
                </div>
                <p className="text-xs text-muted-foreground">Na transição esses tributos saem da base da CBS/IBS. Sem o valor, o cálculo avisa que a base pode estar maior.</p>
              </div>
              <Field id={`${id}-special`} label="Tratamento especial (opcional)" hint="Código do regime diferenciado, se houver.">
                <Input id={`${id}-special`} maxLength={60} {...text("specialTreatment")} />
              </Field>
              <div className="flex items-end">
                <Button type="button" variant="ghost" size="sm" disabled={lines.length === 1}
                  onClick={() => onChange(lines.filter((l) => l.key !== line.key))}>
                  <Trash2 className="mr-1.5 h-3.5 w-3.5" /> Remover linha
                </Button>
              </div>
            </div>
          </fieldset>
        );
      })}
      <Button type="button" variant="outline" size="sm" onClick={() => onChange([...lines, emptyLine()])}>
        <Plus className="mr-1.5 h-3.5 w-3.5" /> Adicionar linha
      </Button>
    </div>
  );
}

/** Header of an operation (date, direction, the other party) plus its lines. */
export function OperationEditor({ idPrefix, value, onChange }: Readonly<{ idPrefix: string; value: OperationDraft; onChange: (v: OperationDraft) => void }>) {
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Field id={`${idPrefix}-date`} label="Data da operação">
          <Input id={`${idPrefix}-date`} type="date" value={value.operationDate} onChange={(e) => onChange({ ...value, operationDate: e.target.value })} />
        </Field>
        <Field id={`${idPrefix}-dir`} label="Operação">
          <Select value={value.direction} onValueChange={(v) => onChange({ ...value, direction: v as TaxDirection })}>
            <SelectTrigger id={`${idPrefix}-dir`}><SelectValue /></SelectTrigger>
            <SelectContent>{(Object.keys(DIRECTIONS) as TaxDirection[]).map((d) => <SelectItem key={d} value={d}>{DIRECTIONS[d]}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field id={`${idPrefix}-cp`} label="Regime da outra parte">
          <Select value={value.counterpartyRegime || NONE} onValueChange={(v) => onChange({ ...value, counterpartyRegime: v === NONE ? "" : (v as TaxRegime) })}>
            <SelectTrigger id={`${idPrefix}-cp`}><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>Não sei / consumidor</SelectItem>
              {TAX_REGIMES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id={`${idPrefix}-uf`} label="UF da outra parte">
          <Select value={value.counterpartyState || NONE} onValueChange={(v) => onChange({ ...value, counterpartyState: v === NONE ? "" : v })}>
            <SelectTrigger id={`${idPrefix}-uf`}><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>Não informar</SelectItem>
              {STATES.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id={`${idPrefix}-desc`} label="Descrição (opcional)" className="sm:col-span-2 lg:col-span-4">
          <Input id={`${idPrefix}-desc`} maxLength={300} value={value.description} onChange={(e) => onChange({ ...value, description: e.target.value })} />
        </Field>
      </div>
      <LinesEditor idPrefix={`${idPrefix}-l`} lines={value.lines} onChange={(lines) => onChange({ ...value, lines })} />
    </div>
  );
}
