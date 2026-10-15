import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { formatCurrency } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import { useOrder, useChangeOrderStatus, useAddOrderItem, useRemoveOrderItem, useUpdateOrderItem } from "../hooks/useOrders";
import { useProfessionals } from "../hooks/useProfessionals";
import { useCatalog } from "../hooks/useCatalog";
import { useServicePreset } from "../context/ServicePresetContext";
import {
  ORDER_STATUS_LABELS,
  ORDER_STATUS_VARIANTS,
  ORDER_ACTION_LABELS,
  allowedOrderTransitions,
  isOrderMutable,
} from "../lib/order-status";
import { PaymentsPanel } from "../components/PaymentsPanel";

/** Select value for "the order's professional" (Radix Select has no empty value). */
const ORDER_PRO = "__order__";

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { labels } = useServicePreset();
  const orderTerm = labels?.order ?? "Ordem";

  const { data: order, isLoading, isError, refetch } = useOrder(id);
  const { data: customers } = useCustomers(false);
  const { data: catalog } = useCatalog(true);
  const { data: professionals } = useProfessionals(false);

  const changeStatus = useChangeOrderStatus();
  const addItem = useAddOrderItem();
  const removeItem = useRemoveOrderItem();
  const updateItem = useUpdateOrderItem();

  const [newItemId, setNewItemId] = useState("");
  const [newQty, setNewQty] = useState("1");
  const [newProfessional, setNewProfessional] = useState(ORDER_PRO);

  if (isLoading) {
    return <div className="space-y-4"><Skeleton className="h-9 w-64" /><Skeleton className="h-72 w-full" /></div>;
  }
  if (isError || !order) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="sm" onClick={() => navigate("/service/ordens")}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Voltar
        </Button>
        <ErrorState type="notfound" onRetry={refetch} />
      </div>
    );
  }

  const customerName = customers?.find((c) => c.id === order.customerId)?.name ?? "—";
  const mutable = isOrderMutable(order.status);
  const transitions = allowedOrderTransitions(order.status);

  const applyStatus = (status: typeof transitions[number]) =>
    changeStatus.mutate(
      { id: order.id, status },
      {
        onSuccess: () => toast.success(`Status: ${ORDER_STATUS_LABELS[status]}.`),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível alterar o status."),
      }
    );

  const handleAddItem = async () => {
    if (!newItemId) { toast.error("Escolha um item do catálogo."); return; }
    const qty = Number(newQty);
    if (!Number.isFinite(qty) || qty <= 0) { toast.error("Quantidade inválida."); return; }
    try {
      await addItem.mutateAsync({ id: order.id, body: {
        catalogItemId: newItemId, quantity: qty, professionalId: newProfessional === ORDER_PRO ? null : newProfessional,
      } });
      setNewItemId(""); setNewQty("1"); setNewProfessional(ORDER_PRO);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Não foi possível adicionar o item.");
    }
  };

  const professionalName = (pid: string | null | undefined) => professionals?.find((p) => p.id === pid)?.name;
  const activePros = (professionals ?? []).filter((p) => p.isActive);
  const orderProName = professionalName(order.professionalId);
  const proTerm = labels?.professional ?? "Profissional";

  // who did the item earns its commission: the item's professional, else the order's
  const handleItemProfessional = (it: (typeof order.items)[number], value: string) =>
    updateItem.mutate(
      { id: order.id, itemId: it.id, body: { quantity: it.quantity, professionalId: value === ORDER_PRO ? null : value } },
      { onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível trocar o profissional.") },
    );

  const handleRemoveItem = async (itemId: string) => {
    try { await removeItem.mutateAsync({ id: order.id, itemId }); }
    catch (e) { toast.error(e instanceof ApiError ? e.message : "Não foi possível remover o item."); }
  };

  return (
    <div className="space-y-6">
      <Button variant="ghost" size="sm" className="-ml-2" onClick={() => navigate("/service/ordens")}>
        <ArrowLeft className="mr-2 h-4 w-4" /> {orderTerm}s
      </Button>

      <PageHeader
        eyebrow={`${orderTerm} ${order.code}`}
        title={customerName}
        actions={
          <div className="flex items-center gap-2">
            <StatusBadge variant={ORDER_STATUS_VARIANTS[order.status]} label={ORDER_STATUS_LABELS[order.status]} dot size="md" />
            {transitions.length > 0 && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="outline" size="sm">Mudar status</Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  {transitions.map((t) => (
                    <DropdownMenuItem key={t} onClick={() => applyStatus(t)}>{ORDER_ACTION_LABELS[t]}</DropdownMenuItem>
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </div>
        }
      />

      {/* Items */}
      <SectionCard title="Itens" noPadding>
        {order.items.length === 0 ? (
          <p className="px-5 py-6 text-center text-[12.5px] text-muted-foreground">Nenhum item nesta ordem.</p>
        ) : (
          <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] text-[13px]">
            <thead>
              <tr className="border-b border-border text-[11px] uppercase tracking-wide text-muted-foreground">
                <th className="px-5 py-2 text-left font-medium">Item</th>
                <th className="px-3 py-2 text-left font-medium">{proTerm}</th>
                <th className="px-3 py-2 text-right font-medium">Qtd</th>
                <th className="px-3 py-2 text-right font-medium">Unitário</th>
                <th className="px-3 py-2 text-right font-medium">Total</th>
                <th className="w-10" />
              </tr>
            </thead>
            <tbody>
              {order.items.map((it) => (
                <tr key={it.id} className="border-b border-border/60">
                  <td className="px-5 py-2.5 text-foreground">{it.nameSnapshot}</td>
                  <td className="px-3 py-1.5 text-muted-foreground">
                    {mutable ? (
                      <Select value={it.professionalId ?? ORDER_PRO} onValueChange={(v) => handleItemProfessional(it, v)}
                        disabled={updateItem.isPending}>
                        <SelectTrigger className="h-8 w-44 text-[12.5px]" aria-label={`${proTerm} de ${it.nameSnapshot}`}><SelectValue /></SelectTrigger>
                        <SelectContent>
                          <SelectItem value={ORDER_PRO}>{orderProName ? `Da ordem (${orderProName})` : `Sem ${proTerm.toLowerCase()}`}</SelectItem>
                          {activePros.map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
                          {it.professionalId && !activePros.some((p) => p.id === it.professionalId) && (
                            <SelectItem value={it.professionalId}>{professionalName(it.professionalId) ?? "—"}</SelectItem>
                          )}
                        </SelectContent>
                      </Select>
                    ) : (
                      professionalName(it.professionalId) ?? orderProName ?? "—"
                    )}
                  </td>
                  <td className="px-3 py-2.5 text-right text-muted-foreground">{it.quantity}</td>
                  <td className="px-3 py-2.5 text-right text-muted-foreground">{formatCurrency(it.unitPriceSnapshot)}</td>
                  <td className="px-3 py-2.5 text-right font-medium text-foreground">{formatCurrency(it.totalAmount)}</td>
                  <td className="px-2 py-2.5 text-right">
                    {mutable && (
                      <button onClick={() => handleRemoveItem(it.id)} disabled={removeItem.isPending}
                        className="text-muted-foreground hover:text-destructive" aria-label="Remover item">
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={4} className="px-5 py-3 text-right text-[12px] font-medium text-muted-foreground">Total</td>
                <td className="px-3 py-3 text-right text-[15px] font-bold text-foreground">{formatCurrency(order.totalAmount)}</td>
                <td />
              </tr>
            </tfoot>
          </table>
          </div>
        )}

        {mutable && (
          <div className="flex flex-wrap items-end gap-2 border-t border-border px-5 py-3">
            <div className="min-w-[12rem] flex-1">
              <Select value={newItemId} onValueChange={setNewItemId} disabled={addItem.isPending}>
                <SelectTrigger className="h-9"><SelectValue placeholder="Adicionar item do catálogo" /></SelectTrigger>
                <SelectContent>
                  {(catalog ?? []).map((c) => (
                    <SelectItem key={c.id} value={c.id}>{c.name} · {formatCurrency(c.price)}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Select value={newProfessional} onValueChange={setNewProfessional} disabled={addItem.isPending}>
              <SelectTrigger className="h-9 w-44" aria-label={`${proTerm} do item`}><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={ORDER_PRO}>{orderProName ? `Da ordem (${orderProName})` : `Sem ${proTerm.toLowerCase()}`}</SelectItem>
                {activePros.map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
              </SelectContent>
            </Select>
            <Input type="number" min={1} step={1} value={newQty} onChange={(e) => setNewQty(e.target.value)}
              className="h-9 w-20" aria-label="Quantidade" disabled={addItem.isPending} />
            <Button onClick={handleAddItem} disabled={addItem.isPending || !newItemId}>
              <Plus className="mr-1.5 h-4 w-4" /> Adicionar
            </Button>
          </div>
        )}
      </SectionCard>

      <PaymentsPanel target={{ kind: "order", id: order.id }} canRecord={order.status !== "Cancelled"} />
    </div>
  );
}
