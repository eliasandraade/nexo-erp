import { useState, useMemo } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Minus, Plus, Search, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useCreateManualDelivery } from "../hooks/useDeliveryMutations";
import type { CreateManualDeliveryRequest, CreateManualItemRequest, DeliveryOrderType } from "../types";

// ── Types ─────────────────────────────────────────────────────────────────────

interface CartItem {
  productId: string;
  productName: string;
  quantity: number;
  notes: string;
}

const CHANNELS = [
  { value: "PhoneCall", label: "Telefone"  },
  { value: "InPerson",  label: "Balcão"    },
  { value: "WhatsApp",  label: "WhatsApp"  },
  { value: "Other",     label: "Outro"     },
] as const;

// ── Product picker ────────────────────────────────────────────────────────────

function ProductPicker({
  onAdd,
  onClose,
}: {
  onAdd: (item: CartItem) => void;
  onClose: () => void;
}) {
  const { data: products = [] } = useProducts(false);
  const [search,   setSearch]   = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [qty,      setQty]      = useState(1);
  const [notes,    setNotes]    = useState("");

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return products.filter(
      (p) => p.isActive && (q === "" || p.name.toLowerCase().includes(q))
    );
  }, [products, search]);

  const selectedProduct = products.find((p) => p.id === selected);

  const handleAdd = () => {
    if (!selected || !selectedProduct) return;
    onAdd({
      productId:   selected,
      productName: selectedProduct.name,
      quantity:    qty,
      notes:       notes.trim(),
    });
    onClose();
  };

  return (
    <div className="flex flex-col gap-3 flex-1 min-h-0">
      {selected ? (
        /* ── Config selected product ── */
        <div className="flex flex-col gap-3">
          <button
            onClick={() => { setSelected(null); setQty(1); setNotes(""); }}
            className="text-sm text-muted-foreground hover:text-foreground text-left"
          >
            ← Voltar
          </button>
          <p className="font-semibold">{selectedProduct?.name}</p>
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => setQty((q) => Math.max(1, q - 1))}
              disabled={qty <= 1}
              className="h-10 w-10 rounded-lg border border-border flex items-center justify-center disabled:opacity-30"
            >
              <Minus className="h-4 w-4" />
            </button>
            <span className="text-xl font-semibold w-8 text-center tabular-nums">{qty}</span>
            <button
              type="button"
              onClick={() => setQty((q) => q + 1)}
              className="h-10 w-10 rounded-lg border border-border flex items-center justify-center"
            >
              <Plus className="h-4 w-4" />
            </button>
          </div>
          <Input
            placeholder="Observação do item (opcional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
          <Button onClick={handleAdd} className="w-full">
            Adicionar {qty > 1 ? `(${qty}×)` : ""}
          </Button>
        </div>
      ) : (
        /* ── Product search ── */
        <>
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
            <Input
              placeholder="Buscar produto..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9"
              autoFocus
            />
          </div>
          <div className="overflow-y-auto flex-1 min-h-0 max-h-52">
            {filtered.length === 0 ? (
              <p className="text-sm text-muted-foreground text-center py-6">
                Nenhum produto encontrado.
              </p>
            ) : (
              <div className="flex flex-col gap-1.5">
                {filtered.map((p) => (
                  <button
                    key={p.id}
                    onClick={() => setSelected(p.id)}
                    className="flex items-center justify-between rounded-lg border border-border bg-card px-3 py-2.5 text-left hover:border-primary/40 hover:bg-primary/5 transition-colors"
                  >
                    <span className="text-sm font-medium">{p.name}</span>
                    <span className="text-sm text-muted-foreground tabular-nums">
                      R$ {p.salePrice.toFixed(2)}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}

// ── ManualOrderSheet ──────────────────────────────────────────────────────────

interface ManualOrderSheetProps {
  open: boolean;
  onClose: () => void;
  storeId: string;
}

export function ManualOrderSheet({ open, onClose, storeId }: ManualOrderSheetProps) {
  const createMut = useCreateManualDelivery(storeId);

  const [customerName,    setCustomerName]    = useState("");
  const [customerPhone,   setCustomerPhone]   = useState("");
  const [customerEmail,   setCustomerEmail]   = useState("");
  const [orderType,       setOrderType]       = useState<DeliveryOrderType>("Delivery");
  const [deliveryAddress, setDeliveryAddress] = useState("");
  const [channel,         setChannel]         = useState("PhoneCall");
  const [estMinutes,      setEstMinutes]       = useState("");
  const [notes,           setNotes]           = useState("");
  const [cartItems,       setCartItems]       = useState<CartItem[]>([]);
  const [showPicker,      setShowPicker]      = useState(false);

  const reset = () => {
    setCustomerName(""); setCustomerPhone(""); setCustomerEmail("");
    setOrderType("Delivery"); setDeliveryAddress("");
    setChannel("PhoneCall"); setEstMinutes(""); setNotes("");
    setCartItems([]); setShowPicker(false);
  };

  const handleClose = () => { reset(); onClose(); };

  const handleAddItem = (item: CartItem) => {
    setCartItems((prev) => {
      const existing = prev.findIndex((i) => i.productId === item.productId);
      if (existing >= 0) {
        const next = [...prev];
        next[existing] = { ...next[existing], quantity: next[existing].quantity + item.quantity };
        return next;
      }
      return [...prev, item];
    });
    setShowPicker(false);
  };

  const removeItem = (productId: string) =>
    setCartItems((prev) => prev.filter((i) => i.productId !== productId));

  const handleSubmit = async () => {
    if (!customerName.trim() || !customerPhone.trim()) return;
    if (orderType === "Delivery" && !deliveryAddress.trim()) return;

    const items: CreateManualItemRequest[] = cartItems.map((i) => ({
      productId: i.productId,
      quantity:  i.quantity,
      notes:     i.notes || null,
    }));

    const req: CreateManualDeliveryRequest = {
      orderType,
      customerName:       customerName.trim(),
      customerPhone:      customerPhone.trim(),
      customerEmail:      customerEmail.trim() || null,
      deliveryAddressJson: orderType === "Delivery" ? deliveryAddress.trim() : null,
      estimatedMinutes:   estMinutes ? parseInt(estMinutes, 10) : null,
      notes:              notes.trim() || null,
      channel,
      items:              items.length > 0 ? items : undefined,
    };

    await createMut.mutateAsync(req);
    handleClose();
  };

  const canSubmit =
    customerName.trim() &&
    customerPhone.trim() &&
    (orderType === "Takeaway" || deliveryAddress.trim());

  return (
    <Sheet open={open} onOpenChange={(v) => !v && handleClose()}>
      <SheetContent
        side="bottom"
        className="rounded-t-2xl flex flex-col max-h-[92vh] pb-safe-bottom pb-6"
      >
        <SheetHeader className="mb-4 shrink-0">
          <SheetTitle>Pedido manual</SheetTitle>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">

          {/* ── Channel ── */}
          <div className="flex gap-2 flex-wrap shrink-0">
            {CHANNELS.map((c) => (
              <button
                key={c.value}
                type="button"
                onClick={() => setChannel(c.value)}
                className={cn(
                  "px-3 py-1.5 rounded-full text-sm font-medium transition-colors",
                  channel === c.value
                    ? "bg-primary text-primary-foreground"
                    : "bg-muted text-muted-foreground hover:text-foreground"
                )}
              >
                {c.label}
              </button>
            ))}
          </div>

          {/* ── Order type ── */}
          <div className="flex rounded-lg border border-border overflow-hidden shrink-0">
            {(["Delivery", "Takeaway"] as DeliveryOrderType[]).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => setOrderType(t)}
                className={cn(
                  "flex-1 py-2.5 text-sm font-medium transition-colors",
                  orderType === t
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground"
                )}
              >
                {t === "Delivery" ? "Entrega" : "Retirada"}
              </button>
            ))}
          </div>

          {/* ── Customer ── */}
          <div className="flex flex-col gap-2 shrink-0">
            <Input
              placeholder="Nome do cliente *"
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
            />
            <Input
              placeholder="Telefone *"
              value={customerPhone}
              onChange={(e) => setCustomerPhone(e.target.value)}
              type="tel"
            />
            <Input
              placeholder="E-mail (opcional)"
              value={customerEmail}
              onChange={(e) => setCustomerEmail(e.target.value)}
              type="email"
            />
          </div>

          {/* ── Delivery address ── */}
          {orderType === "Delivery" && (
            <Textarea
              placeholder="Endereço de entrega *"
              value={deliveryAddress}
              onChange={(e) => setDeliveryAddress(e.target.value)}
              rows={2}
              className="resize-none shrink-0"
            />
          )}

          {/* ── Estimated time ── */}
          <Input
            placeholder="Tempo estimado (minutos, opcional)"
            value={estMinutes}
            onChange={(e) => setEstMinutes(e.target.value.replace(/\D/g, ""))}
            type="number"
            min={1}
            className="shrink-0"
          />

          {/* ── Items ── */}
          <div className="flex flex-col gap-2 shrink-0">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium">Itens do pedido</span>
              <button
                type="button"
                onClick={() => setShowPicker((v) => !v)}
                className="text-sm text-primary hover:underline"
              >
                + Adicionar produto
              </button>
            </div>

            {showPicker && (
              <div className="rounded-xl border border-border bg-muted/30 p-3 flex flex-col gap-3">
                <ProductPicker
                  onAdd={handleAddItem}
                  onClose={() => setShowPicker(false)}
                />
              </div>
            )}

            {cartItems.length > 0 && (
              <div className="flex flex-col gap-1.5">
                {cartItems.map((item) => (
                  <div
                    key={item.productId}
                    className="flex items-center gap-2 rounded-lg bg-muted/40 px-3 py-2"
                  >
                    <span className="text-sm flex-1">
                      {item.quantity}× {item.productName}
                      {item.notes && (
                        <span className="text-muted-foreground"> · {item.notes}</span>
                      )}
                    </span>
                    <button
                      onClick={() => removeItem(item.productId)}
                      className="text-muted-foreground hover:text-destructive transition-colors"
                    >
                      <X className="h-3.5 w-3.5" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* ── Notes ── */}
          <Textarea
            placeholder="Observações gerais (opcional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            className="resize-none shrink-0"
          />
        </div>

        {/* ── Submit ── */}
        <Button
          className="w-full h-12 text-base mt-4 shrink-0"
          onClick={handleSubmit}
          disabled={!canSubmit || createMut.isPending}
        >
          {createMut.isPending ? "Criando pedido..." : "Criar pedido"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
