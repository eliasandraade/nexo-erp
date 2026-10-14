import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Tags, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import {
  createPriceList, getPriceList, linkCustomer, listPriceLists, PRICE_LISTS_KEY, removeListPrice,
  setDefaultPriceList, setListPrice, setPriceListActive, unlinkCustomer, updatePriceList,
  type PriceListDetailDto, type PriceListDto,
} from "../api/pricelists.api";

// Validity is stored in UTC; the date field shows the operator's local day (not the UTC one).
const dateInput = (iso: string | null) => {
  if (!iso) return "";
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const toIso = (date: string, endOfDay = false) =>
  date ? new Date(`${date}T${endOfDay ? "23:59:59" : "00:00:00"}`).toISOString() : null;

function validityLabel(l: PriceListDto) {
  if (!l.validFrom && !l.validUntil) return "Sem prazo";
  const f = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString("pt-BR") : "…");
  return `${f(l.validFrom)} a ${f(l.validUntil)}`;
}

/**
 * Listas de preço. The server prices every sale line from them: the customer's list or the
 * default list (active, within validity, containing the product) — higher priority wins, the
 * customer's list on a tie — otherwise the product's own price.
 */
export default function ListasDePrecoPage() {
  const qc = useQueryClient();
  const { data: lists, isLoading, isError, refetch } = useQuery({ queryKey: PRICE_LISTS_KEY, queryFn: listPriceLists });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newName, setNewName] = useState("");
  const refresh = () => qc.invalidateQueries({ queryKey: PRICE_LISTS_KEY });

  const create = useMutation({
    mutationFn: () => createPriceList({ name: newName.trim(), isDefault: !lists?.some((l) => l.isDefault) }),
    onSuccess: (l) => { setNewName(""); setSelectedId(l.id); refresh(); toast.success("Lista criada."); },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Listas de preço"
        description="Preços por cliente, promoções com vigência e a tabela padrão. O PDV e a venda usam o preço resolvido aqui."
      />

      <SectionCard title="Nova lista">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[16rem] flex-1 space-y-1.5">
            <Label htmlFor="pl-new-name">Nome</Label>
            <Input id="pl-new-name" value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="Ex.: Atacado, Promoção de verão" />
          </div>
          <Button onClick={() => create.mutate()} disabled={!newName.trim() || create.isPending}>
            <Plus className="mr-1 h-4 w-4" /> Criar lista
          </Button>
        </div>
      </SectionCard>

      {isLoading ? (
        <Skeleton className="h-40 w-full" />
      ) : isError ? (
        <ErrorState title="Não foi possível carregar as listas" onRetry={() => void refetch()} />
      ) : !lists?.length ? (
        <SectionCard>
          <EmptyState icon={Tags} title="Nenhuma lista de preço" description="Sem listas, a venda usa o preço de cada produto." />
        </SectionCard>
      ) : (
        <SectionCard title="Listas">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Lista</TableHead>
                <TableHead>Vigência</TableHead>
                <TableHead className="text-right">Prioridade</TableHead>
                <TableHead className="text-right">Produtos</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lists.map((l) => (
                <TableRow key={l.id} className="cursor-pointer" onClick={() => setSelectedId(l.id)}>
                  <TableCell>
                    <button type="button" className="font-medium hover:underline">{l.name}</button>
                    <span className="ml-2 inline-flex gap-1">
                      {l.isDefault && <Badge>Padrão</Badge>}
                      {!l.isActive && <Badge variant="secondary">Inativa</Badge>}
                    </span>
                  </TableCell>
                  <TableCell className="text-sm">{validityLabel(l)}</TableCell>
                  <TableCell className="text-right tabular-nums">{l.priority}</TableCell>
                  <TableCell className="text-right tabular-nums">{l.itemCount}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </SectionCard>
      )}

      {selectedId && <ListEditor key={selectedId} id={selectedId} onChanged={refresh} />}
    </div>
  );
}

function ListEditor({ id, onChanged }: { id: string; onChanged: () => void }) {
  const qc = useQueryClient();
  const { data: list } = useQuery({ queryKey: [...PRICE_LISTS_KEY, id], queryFn: () => getPriceList(id) });
  const { data: products = [] } = useProducts();
  const { data: customers = [] } = useCustomers();
  const [productId, setProductId] = useState("");
  const [price, setPrice] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [form, setForm] = useState<{ name: string; validFrom: string; validUntil: string; priority: string } | null>(null);

  const apply = (detail: PriceListDetailDto) => { qc.setQueryData([...PRICE_LISTS_KEY, id], detail); onChanged(); };
  const fail = (e: Error) => toast.error(e.message);

  const save = useMutation({
    mutationFn: () => updatePriceList(id, {
      name: form!.name.trim(),
      validFrom: toIso(form!.validFrom),
      validUntil: toIso(form!.validUntil, true),
      priority: Number.parseInt(form!.priority, 10) || 0,   // the API takes an integer
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: [...PRICE_LISTS_KEY, id] }); onChanged(); setForm(null); toast.success("Lista atualizada."); },
    onError: fail,
  });
  const addPrice = useMutation({
    mutationFn: () => setListPrice(id, productId, Number(price.replace(",", "."))),
    onSuccess: (d) => { setPrice(""); apply(d); },
    onError: fail,
  });
  const removePrice = useMutation({ mutationFn: (p: string) => removeListPrice(id, p), onSuccess: apply, onError: fail });
  const addCustomer = useMutation({ mutationFn: () => linkCustomer(id, customerId), onSuccess: (d) => { setCustomerId(""); apply(d); }, onError: fail });
  const removeCustomer = useMutation({ mutationFn: (c: string) => unlinkCustomer(id, c), onSuccess: apply, onError: fail });
  const makeDefault = useMutation({ mutationFn: () => setDefaultPriceList(id), onSuccess: () => { qc.invalidateQueries({ queryKey: PRICE_LISTS_KEY }); }, onError: fail });
  const toggle = useMutation({ mutationFn: (active: boolean) => setPriceListActive(id, active), onSuccess: () => { qc.invalidateQueries({ queryKey: PRICE_LISTS_KEY }); }, onError: fail });

  if (!list) return <Skeleton className="h-48 w-full" />;
  const editing = form ?? {
    name: list.name, validFrom: dateInput(list.validFrom), validUntil: dateInput(list.validUntil), priority: String(list.priority),
  };
  const priceValue = Number(price.replace(",", "."));

  return (
    <SectionCard
      title={list.name}
      description={list.isDefault ? "Lista padrão: vale para todos os clientes sem lista própria." : "Vale para os clientes vinculados abaixo."}
    >
      <div className="space-y-6">
        <div className="grid gap-3 sm:grid-cols-4 sm:items-end">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="pl-name">Nome</Label>
            <Input id="pl-name" value={editing.name} onChange={(e) => setForm({ ...editing, name: e.target.value })} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pl-from">Vigência: início</Label>
            <Input id="pl-from" type="date" value={editing.validFrom} onChange={(e) => setForm({ ...editing, validFrom: e.target.value })} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pl-until">Vigência: fim</Label>
            <Input id="pl-until" type="date" value={editing.validUntil} onChange={(e) => setForm({ ...editing, validUntil: e.target.value })} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pl-priority">Prioridade</Label>
            <Input id="pl-priority" inputMode="numeric" value={editing.priority} onChange={(e) => setForm({ ...editing, priority: e.target.value })} />
          </div>
          <div className="flex flex-wrap gap-2 sm:col-span-3">
            <Button onClick={() => save.mutate()} disabled={!form || save.isPending}>Salvar</Button>
            {!list.isDefault && <Button variant="outline" onClick={() => makeDefault.mutate()}>Tornar padrão</Button>}
            <Button variant="outline" onClick={() => toggle.mutate(!list.isActive)}>{list.isActive ? "Desativar" : "Ativar"}</Button>
          </div>
        </div>
        <p className="text-xs text-muted-foreground">
          Quando um cliente tem lista própria e também existe a lista padrão, vence a de maior prioridade (no empate, a do cliente).
        </p>

        <div className="space-y-3">
          <h3 className="text-sm font-semibold">Preços</h3>
          <div className="grid gap-3 sm:grid-cols-[1fr_9rem_auto] sm:items-end">
            <div className="space-y-1.5">
              <Label htmlFor="pl-product">Produto</Label>
              <Select value={productId} onValueChange={setProductId}>
                <SelectTrigger id="pl-product"><SelectValue placeholder="Escolha o produto" /></SelectTrigger>
                <SelectContent>
                  {products.map((p) => <SelectItem key={p.id} value={p.id}>{p.name} ({p.code}) · {formatCurrency(p.salePrice)}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pl-price">Preço na lista</Label>
              <Input id="pl-price" inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
            </div>
            <Button onClick={() => addPrice.mutate()} disabled={!productId || !(priceValue >= 0) || price.trim() === "" || addPrice.isPending}>
              Definir preço
            </Button>
          </div>
          {list.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhum produto nesta lista: a venda usa o preço do produto.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Produto</TableHead>
                  <TableHead className="text-right">Preço</TableHead>
                  <TableHead className="w-10" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.items.map((i) => (
                  <TableRow key={i.id}>
                    <TableCell className="text-sm">{i.productName}<span className="block text-xs text-muted-foreground">{i.productCode}</span></TableCell>
                    <TableCell className="text-right tabular-nums">{formatCurrency(i.price)}</TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon" aria-label={`Tirar ${i.productName} da lista`} onClick={() => removePrice.mutate(i.productId)}>
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </div>

        {!list.isDefault && (
          <div className="space-y-3">
            <h3 className="text-sm font-semibold">Clientes</h3>
            <div className="flex flex-wrap items-end gap-3">
              <div className="min-w-[16rem] flex-1 space-y-1.5">
                <Label htmlFor="pl-customer">Vincular cliente</Label>
                <Select value={customerId} onValueChange={setCustomerId}>
                  <SelectTrigger id="pl-customer"><SelectValue placeholder="Escolha o cliente" /></SelectTrigger>
                  <SelectContent>
                    {customers.map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
                  </SelectContent>
                </Select>
              </div>
              <Button variant="outline" onClick={() => addCustomer.mutate()} disabled={!customerId || addCustomer.isPending}>Vincular</Button>
            </div>
            <p className="text-xs text-muted-foreground">Cada cliente tem uma lista: vincular aqui troca a lista anterior dele.</p>
            <ul className="divide-y divide-border text-sm">
              {(list.customers ?? []).map((c) => (
                <li key={c.customerId} className="flex items-center justify-between py-2">
                  {c.name}
                  <Button variant="ghost" size="sm" onClick={() => removeCustomer.mutate(c.customerId)}>Desvincular</Button>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </SectionCard>
  );
}
