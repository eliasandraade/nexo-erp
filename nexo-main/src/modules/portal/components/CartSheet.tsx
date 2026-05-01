import { useState } from "react";
import { X, Minus, Plus } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { CartItem } from "../types";
import { createPortalOrder, type CreatePortalOrderRequest } from "../api/portal.api";

interface CartSheetProps {
  open:            boolean;
  onClose:         () => void;
  items:           CartItem[];
  onChangeQty:     (productId: string, delta: number) => void;
  onRemove:        (productId: string) => void;
  slug:            string;
  deliveryEnabled: boolean;
  takeawayEnabled: boolean;
}

export function CartSheet({
  open, onClose, items, onChangeQty, onRemove,
  slug, deliveryEnabled, takeawayEnabled,
}: CartSheetProps) {
  const navigate = useNavigate();

  const [orderType, setOrderType]     = useState<"Delivery" | "Takeaway">(
    deliveryEnabled ? "Delivery" : "Takeaway"
  );
  const [name,    setName]    = useState("");
  const [phone,   setPhone]   = useState("");
  const [email,   setEmail]   = useState("");
  const [address, setAddress] = useState("");
  const [notes,   setNotes]   = useState("");

  const total = items.reduce(
    (s, i) => s + (i.price + i.modifiers.reduce((ms, m) => ms + m.price, 0)) * i.quantity,
    0
  );

  const mut = useMutation({
    mutationFn: (req: CreatePortalOrderRequest) => createPortalOrder(req),
    onSuccess: (data) => {
      onClose();
      navigate(`/rastrear/${data.trackingToken}`);
    },
  });

  const canSubmit =
    name.trim() && phone.trim() &&
    (orderType === "Takeaway" || address.trim()) &&
    items.length > 0 && !mut.isPending;

  const handleSubmit = () => {
    if (!canSubmit) return;
    mut.mutate({
      publicSlug:          slug,
      orderType,
      customerName:        name.trim(),
      customerPhone:       phone.trim(),
      customerEmail:       email.trim() || null,
      deliveryAddressJson: orderType === "Delivery"
        ? JSON.stringify({ address: address.trim() })
        : null,
      notes:               notes.trim() || null,
      items: items.map((i) => ({
        productId: i.productId,
        quantity:  i.quantity,
        notes:     i.notes || null,
        modifiers: i.modifiers.map((m) => ({ modifierId: m.modifierId })),
      })),
    });
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl flex flex-col max-h-[92vh] pb-safe-bottom pb-6">
        <SheetHeader className="mb-4 shrink-0">
          <SheetTitle>Seu pedido</SheetTitle>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">
          {/* Items */}
          <div className="flex flex-col gap-2 shrink-0">
            {items.map((item) => (
              <div key={item.productId} className="flex items-start gap-2">
                <div className="flex items-center gap-2 shrink-0">
                  <button
                    onClick={() => onChangeQty(item.productId, -1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center"
                  >
                    <Minus className="h-3 w-3" />
                  </button>
                  <span className="w-5 text-center text-sm font-medium tabular-nums">{item.quantity}</span>
                  <button
                    onClick={() => onChangeQty(item.productId, 1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center"
                  >
                    <Plus className="h-3 w-3" />
                  </button>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium leading-tight">{item.productName}</p>
                  {item.modifiers.length > 0 && (
                    <p className="text-xs text-muted-foreground">
                      {item.modifiers.map((m) => m.label).join(", ")}
                    </p>
                  )}
                  {item.notes && (
                    <p className="text-xs text-muted-foreground italic">{item.notes}</p>
                  )}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <span className="text-sm tabular-nums">
                    R$ {((item.price + item.modifiers.reduce((s, m) => s + m.price, 0)) * item.quantity).toFixed(2)}
                  </span>
                  <button onClick={() => onRemove(item.productId)} className="text-muted-foreground hover:text-destructive">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            ))}
          </div>

          <div className="border-t border-border pt-2 flex justify-between items-center shrink-0">
            <span className="text-sm text-muted-foreground">Total</span>
            <span className="font-bold tabular-nums">R$ {total.toFixed(2)}</span>
          </div>

          {/* Order type */}
          {deliveryEnabled && takeawayEnabled && (
            <div className="flex rounded-lg border border-border overflow-hidden shrink-0">
              {(["Delivery", "Takeaway"] as const).map((t) => (
                <button
                  key={t}
                  onClick={() => setOrderType(t)}
                  className={cn(
                    "flex-1 py-2.5 text-sm font-medium transition-colors",
                    orderType === t ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"
                  )}
                >
                  {t === "Delivery" ? "Entrega" : "Retirada"}
                </button>
              ))}
            </div>
          )}

          {/* Customer */}
          <div className="flex flex-col gap-2 shrink-0">
            <Input placeholder="Seu nome *" value={name} onChange={(e) => setName(e.target.value)} />
            <Input placeholder="Telefone / WhatsApp *" value={phone} onChange={(e) => setPhone(e.target.value)} type="tel" />
            <Input placeholder="E-mail (opcional)" value={email} onChange={(e) => setEmail(e.target.value)} type="email" />
          </div>

          {orderType === "Delivery" && (
            <Textarea
              placeholder="Endereço de entrega *"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
              rows={2}
              className="resize-none shrink-0"
            />
          )}

          <Textarea
            placeholder="Observações gerais (opcional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            className="resize-none shrink-0"
          />
        </div>

        {mut.isError && (
          <p className="text-sm text-destructive text-center shrink-0">
            Erro ao enviar pedido. Tente novamente.
          </p>
        )}

        <Button
          className="w-full h-12 text-base mt-4 shrink-0"
          onClick={handleSubmit}
          disabled={!canSubmit}
        >
          {mut.isPending ? "Enviando..." : "Confirmar pedido"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
