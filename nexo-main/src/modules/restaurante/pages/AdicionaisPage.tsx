import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ListPlus, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency } from "@/lib/formatters";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useModifierGroups } from "../hooks/useModifierGroups";
import {
  addModifier, createModifierGroup, removeModifier, removeModifierGroup, updateModifierGroup,
} from "../api/restaurante.api";
import type { ModifierGroupDto } from "../types";

/** "2,50" / "2.50" → 2.5; invalid → NaN. Adicionais can be negative (a discount option, e.g. "sem queijo"). */
function price(value: string): number {
  const v = value.trim();
  if (!v) return 0;
  const n = Number(v.includes(",") ? v.replace(/\./g, "").replace(",", ".") : v);
  return Number.isFinite(n) ? Math.round(n * 100) / 100 : NaN;
}

/**
 * Adicionais do cardápio: grupos por produto (ponto da carne, acompanhamentos, extras) com as opções
 * e o preço de cada uma. O salão, o portal e a cozinha usam o que está aqui.
 */
export default function AdicionaisPage() {
  const { data: products, isLoading: loadingProducts } = useProducts({ isIngredient: false });
  const [productId, setProductId] = useState<string>("");
  const { data: groups, isLoading } = useModifierGroups(productId || null);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Adicionais"
        description="Grupos de opções por produto — ponto da carne, acompanhamentos, extras — com o preço de cada opção."
      />

      <SectionCard>
        <div className="max-w-md space-y-1.5">
          <Label htmlFor="mod-product">Produto</Label>
          {loadingProducts ? <Skeleton className="h-9 w-full" /> : (
            <Select value={productId} onValueChange={setProductId}>
              <SelectTrigger id="mod-product" aria-label="Produto"><SelectValue placeholder="Escolha um produto do cardápio" /></SelectTrigger>
              <SelectContent>
                {(products ?? []).filter((p) => p.isActive).map((p) => (
                  <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </div>
      </SectionCard>

      {!productId ? null : isLoading ? (
        <Skeleton className="h-40 w-full" />
      ) : (
        <>
          {(groups ?? []).length === 0 && (
            <SectionCard>
              <EmptyState icon={ListPlus} title="Sem adicionais" description="Crie um grupo para oferecer opções neste produto." />
            </SectionCard>
          )}
          {(groups ?? []).map((g) => <GroupCard key={g.id} group={g} />)}
          <NewGroupForm productId={productId} />
        </>
      )}
    </div>
  );
}

function useRefresh(productId: string) {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries({ queryKey: ["modifier-groups", productId] });
}

function GroupCard({ group }: { group: ModifierGroupDto }) {
  const refresh = useRefresh(group.productId);
  const [name, setName] = useState("");
  const [value, setValue] = useState("");
  const [required, setRequired] = useState(group.isRequired);
  const [min, setMin] = useState(String(group.minSelections));
  const [max, setMax] = useState(String(group.maxSelections));
  const onError = (e: Error) => toast.error(e.message || "Não foi possível salvar.");

  const add = useMutation({
    mutationFn: () => addModifier(group.id, name.trim(), price(value), group.modifiers.length),
    onSuccess: () => { setName(""); setValue(""); refresh(); },
    onError,
  });
  const remove = useMutation({ mutationFn: (id: string) => removeModifier(group.id, id), onSuccess: refresh, onError });
  const save = useMutation({
    mutationFn: () => updateModifierGroup(group.id, {
      name: group.name, isRequired: required, minSelections: Number(min), maxSelections: Number(max), sortOrder: group.sortOrder,
    }),
    onSuccess: () => { toast.success("Grupo salvo."); refresh(); },
    onError,
  });
  const drop = useMutation({ mutationFn: () => removeModifierGroup(group.id), onSuccess: refresh, onError });

  const limitsValid = /^\d+$/.test(min) && /^\d+$/.test(max) && Number(max) >= 1 && Number(min) <= Number(max);
  const changed = required !== group.isRequired || min !== String(group.minSelections) || max !== String(group.maxSelections);

  return (
    <SectionCard
      title={group.name}
      actions={<Button size="sm" variant="ghost" disabled={drop.isPending} onClick={() => drop.mutate()}>Remover grupo</Button>}
    >
      <div className="mb-4 flex flex-wrap items-end gap-4">
        <div className="flex items-center gap-2">
          <Switch id={`req-${group.id}`} checked={required} onCheckedChange={setRequired} />
          <Label htmlFor={`req-${group.id}`}>Obrigatório</Label>
        </div>
        <div className="w-24 space-y-1.5">
          <Label htmlFor={`min-${group.id}`}>Mínimo</Label>
          <Input id={`min-${group.id}`} inputMode="numeric" value={min} onChange={(e) => setMin(e.target.value)} />
        </div>
        <div className="w-24 space-y-1.5">
          <Label htmlFor={`max-${group.id}`}>Máximo</Label>
          <Input id={`max-${group.id}`} inputMode="numeric" value={max} onChange={(e) => setMax(e.target.value)} />
        </div>
        {changed && <Button size="sm" disabled={!limitsValid || save.isPending} onClick={() => save.mutate()}>Salvar regras</Button>}
      </div>

      <ul className="divide-y divide-border">
        {group.modifiers.filter((m) => m.isActive).map((m) => (
          <li key={m.id} className="flex items-center justify-between py-2 text-sm">
            <span>{m.name}</span>
            <span className="flex items-center gap-3">
              <span className="tabular-nums text-muted-foreground">
                {m.priceAdjustment === 0 ? "sem custo" : `${m.priceAdjustment > 0 ? "+" : ""}${formatCurrency(m.priceAdjustment)}`}
              </span>
              <Button size="icon" variant="ghost" aria-label={`Remover ${m.name}`} disabled={remove.isPending} onClick={() => remove.mutate(m.id)}>
                <Trash2 className="h-4 w-4" />
              </Button>
            </span>
          </li>
        ))}
      </ul>

      <div className="mt-3 flex flex-wrap items-end gap-2">
        <div className="min-w-[12rem] flex-1 space-y-1.5">
          <Label htmlFor={`opt-${group.id}`}>Nova opção</Label>
          <Input id={`opt-${group.id}`} value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex.: Bacon extra" />
        </div>
        <div className="w-28 space-y-1.5">
          <Label htmlFor={`price-${group.id}`}>Preço</Label>
          <Input id={`price-${group.id}`} inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} placeholder="0,00" />
        </div>
        <Button disabled={!name.trim() || Number.isNaN(price(value)) || add.isPending} onClick={() => add.mutate()}>
          <Plus className="mr-1 h-4 w-4" /> Adicionar
        </Button>
      </div>
      {group.isRequired && <Badge variant="outline" className="mt-3">O cliente escolhe ao menos {Math.max(1, group.minSelections)}</Badge>}
    </SectionCard>
  );
}

function NewGroupForm({ productId }: { productId: string }) {
  const refresh = useRefresh(productId);
  const [name, setName] = useState("");
  const [required, setRequired] = useState(false);
  const create = useMutation({
    mutationFn: () => createModifierGroup(productId, {
      name: name.trim(), isRequired: required, minSelections: required ? 1 : 0, maxSelections: 1, sortOrder: 0,
    }),
    onSuccess: () => { setName(""); setRequired(false); refresh(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível criar o grupo."),
  });

  return (
    <SectionCard title="Novo grupo">
      <div className="flex flex-wrap items-end gap-4">
        <div className="min-w-[14rem] flex-1 space-y-1.5">
          <Label htmlFor="new-group">Nome do grupo</Label>
          <Input id="new-group" value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex.: Ponto da carne" />
        </div>
        <div className="flex items-center gap-2 pb-2">
          <Switch id="new-group-required" checked={required} onCheckedChange={setRequired} />
          <Label htmlFor="new-group-required">Obrigatório</Label>
        </div>
        <Button disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>
          <Plus className="mr-1 h-4 w-4" /> Criar grupo
        </Button>
      </div>
    </SectionCard>
  );
}
