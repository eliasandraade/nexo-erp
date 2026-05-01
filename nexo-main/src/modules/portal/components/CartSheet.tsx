import { useState, useEffect } from "react";
import { X, Minus, Plus, Loader2, MapPin, Tag } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { CartItem } from "../types";
import {
  createPortalOrder,
  getDeliveryZones,
  validateCoupon,
  type CreatePortalOrderRequest,
  type DeliveryZoneDto,
} from "../api/portal.api";

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

interface ViaCepResponse {
  bairro?:     string;
  logradouro?: string;
  localidade?: string;
  uf?:         string;
  erro?:       boolean;
}

export function CartSheet({
  open, onClose, items, onChangeQty, onRemove,
  slug, deliveryEnabled, takeawayEnabled,
}: CartSheetProps) {
  const navigate = useNavigate();

  const [orderType, setOrderType] = useState<"Delivery" | "Takeaway">(
    deliveryEnabled ? "Delivery" : "Takeaway"
  );
  const [name,    setName]    = useState("");
  const [phone,   setPhone]   = useState("");
  const [email,   setEmail]   = useState("");
  const [notes,   setNotes]   = useState("");

  // CEP + zone
  const [cep,           setCep]           = useState("");
  const [cepData,       setCepData]       = useState<ViaCepResponse | null>(null);
  const [cepLoading,    setCepLoading]    = useState(false);
  const [cepError,      setCepError]      = useState<string | null>(null);
  const [selectedZone,  setSelectedZone]  = useState<DeliveryZoneDto | null>(null);
  const [complement,    setComplement]    = useState("");

  // Coupon
  const [couponInput,    setCouponInput]    = useState("");
  const [appliedCoupon,  setAppliedCoupon]  = useState<string | null>(null);
  const [couponError,    setCouponError]    = useState<string | null>(null);
  const [discountAmount, setDiscountAmount] = useState(0);
  const [couponLoading,  setCouponLoading]  = useState(false);

  // Load delivery zones
  const { data: zones = [] } = useQuery({
    queryKey:  ["delivery-zones", slug],
    queryFn:   () => getDeliveryZones(slug),
    enabled:   open && orderType === "Delivery",
    staleTime: 5 * 60 * 1000,
  });

  // Reset delivery state when switching to Takeaway
  useEffect(() => {
    if (orderType !== "Delivery") {
      setSelectedZone(null);
      setCepData(null);
      setCep("");
      setCepError(null);
    }
  }, [orderType]);

  // Reset coupon when zone or subtotal changes
  useEffect(() => {
    setAppliedCoupon(null);
    setDiscountAmount(0);
    setCouponError(null);
  }, [selectedZone, orderType]);

  const lookupCep = async (raw: string) => {
    const digits = raw.replace(/\D/g, "");
    if (digits.length !== 8) return;
    setCepLoading(true);
    setCepError(null);
    setSelectedZone(null);
    try {
      const res  = await fetch(`https://viacep.com.br/ws/${digits}/json/`);
      const data = await res.json() as ViaCepResponse;
      if (data.erro) { setCepError("CEP não encontrado."); setCepData(null); return; }
      setCepData(data);
      const matched = zones.find(
        (z) => z.neighborhood.toLowerCase() === (data.bairro ?? "").toLowerCase()
      );
      if (matched) {
        setSelectedZone(matched);
      } else if (data.bairro) {
        setCepError(`Bairro "${data.bairro}" não está na área de entrega.`);
      } else {
        setCepError("Não foi possível identificar o bairro pelo CEP.");
      }
    } catch {
      setCepError("Erro ao buscar CEP. Tente novamente.");
    } finally {
      setCepLoading(false);
    }
  };

  const handleCepChange = (value: string) => {
    setCep(value);
    if (value.replace(/\D/g, "").length === 8) lookupCep(value);
  };

  const handleApplyCoupon = async () => {
    if (!couponInput.trim() || !phone.trim()) return;
    setCouponLoading(true);
    setCouponError(null);
    try {
      const res = await validateCoupon({
        publicSlug:    slug,
        couponCode:    couponInput.trim(),
        customerPhone: phone.trim(),
        itemsSubtotal,
        deliveryFee:   selectedZone?.fee ?? 0,
        neighborhood:  selectedZone?.neighborhood,
      });
      if (res.valid) {
        setAppliedCoupon(couponInput.trim().toUpperCase());
        setDiscountAmount(res.discountAmount);
      } else {
        setCouponError(res.error ?? "Cupom inválido.");
        setAppliedCoupon(null);
        setDiscountAmount(0);
      }
    } catch {
      setCouponError("Erro ao validar cupom.");
    } finally {
      setCouponLoading(false);
    }
  };

  const removeCoupon = () => {
    setAppliedCoupon(null);
    setDiscountAmount(0);
    setCouponInput("");
    setCouponError(null);
  };

  // Financials
  const itemsSubtotal = items.reduce(
    (s, i) => s + (i.price + i.modifiers.reduce((ms, m) => ms + m.price, 0)) * i.quantity,
    0
  );
  const deliveryFee = orderType === "Delivery" ? (selectedZone?.fee ?? 0) : 0;
  const total       = itemsSubtotal + deliveryFee - discountAmount;

  const fullAddress = cepData
    ? [cepData.logradouro, complement, cepData.bairro, cepData.localidade, cepData.uf]
        .filter(Boolean).join(", ")
    : complement;

  const mut = useMutation({
    mutationFn: (req: CreatePortalOrderRequest) => createPortalOrder(req),
    onSuccess:  (data) => { onClose(); navigate(`/rastrear/${data.trackingToken}`); },
  });

  const canSubmit =
    name.trim() && phone.trim() &&
    (orderType === "Takeaway" || (selectedZone !== null && complement.trim())) &&
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
        ? JSON.stringify({ address: fullAddress, cep: cep.replace(/\D/g, "") })
        : null,
      notes:               notes.trim() || null,
      deliveryZoneId:      selectedZone?.id ?? null,
      couponCode:          appliedCoupon ?? null,
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
                  <button onClick={() => onChangeQty(item.productId, -1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center">
                    <Minus className="h-3 w-3" />
                  </button>
                  <span className="w-5 text-center text-sm font-medium tabular-nums">{item.quantity}</span>
                  <button onClick={() => onChangeQty(item.productId, 1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center">
                    <Plus className="h-3 w-3" />
                  </button>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium leading-tight">{item.productName}</p>
                  {item.modifiers.length > 0 && (
                    <p className="text-xs text-muted-foreground">{item.modifiers.map((m) => m.label).join(", ")}</p>
                  )}
                  {item.notes && <p className="text-xs text-muted-foreground italic">{item.notes}</p>}
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

          {/* Order type toggle */}
          {deliveryEnabled && takeawayEnabled && (
            <div className="flex rounded-lg border border-border overflow-hidden shrink-0">
              {(["Delivery", "Takeaway"] as const).map((t) => (
                <button key={t} onClick={() => setOrderType(t)}
                  className={cn("flex-1 py-2.5 text-sm font-medium transition-colors",
                    orderType === t
                      ? "bg-primary text-primary-foreground"
                      : "text-muted-foreground hover:text-foreground")}>
                  {t === "Delivery" ? "Entrega" : "Retirada"}
                </button>
              ))}
            </div>
          )}

          {/* Customer fields */}
          <div className="flex flex-col gap-2 shrink-0">
            <Input placeholder="Seu nome *" value={name} onChange={(e) => setName(e.target.value)} />
            <Input placeholder="Telefone / WhatsApp *" value={phone} onChange={(e) => setPhone(e.target.value)} type="tel" />
            <Input placeholder="E-mail (opcional)" value={email} onChange={(e) => setEmail(e.target.value)} type="email" />
          </div>

          {/* Delivery address */}
          {orderType === "Delivery" && (
            <div className="flex flex-col gap-2 shrink-0">
              {/* CEP */}
              <div className="relative">
                <Input
                  placeholder="CEP *"
                  value={cep}
                  onChange={(e) => handleCepChange(e.target.value)}
                  maxLength={9}
                  className="pr-8"
                />
                {cepLoading && (
                  <Loader2 className="absolute right-2.5 top-1/2 -translate-y-1/2 h-4 w-4 animate-spin text-muted-foreground" />
                )}
              </div>

              {cepError && <p className="text-xs text-destructive">{cepError}</p>}

              {cepData && !cepError && (
                <div className="rounded-lg border border-border p-3 text-sm flex flex-col gap-1">
                  <div className="flex items-center gap-1.5 text-muted-foreground">
                    <MapPin className="h-3.5 w-3.5 shrink-0" />
                    <span>{[cepData.logradouro, cepData.bairro, cepData.localidade].filter(Boolean).join(", ")}</span>
                  </div>
                  {selectedZone && (
                    <p className="text-xs text-green-600 dark:text-green-400 font-medium">
                      Entrega disponível — Taxa: R$ {selectedZone.fee.toFixed(2)}
                    </p>
                  )}
                </div>
              )}

              {/* Complement / number */}
              <Input
                placeholder="Número e complemento *"
                value={complement}
                onChange={(e) => setComplement(e.target.value)}
              />

              {/* Manual zone picker (fallback when CEP didn't auto-match) */}
              {zones.length > 0 && !selectedZone && cepData && !cepError && (
                <div className="flex flex-col gap-1.5">
                  <p className="text-xs text-muted-foreground">Selecione seu bairro:</p>
                  <div className="grid grid-cols-2 gap-1.5 max-h-36 overflow-y-auto">
                    {zones.map((z) => (
                      <button key={z.id} onClick={() => setSelectedZone(z)}
                        className="text-left rounded-lg border border-border px-2.5 py-1.5 text-xs hover:border-primary transition-colors">
                        <span className="font-medium block">{z.neighborhood}</span>
                        <span className="text-muted-foreground">R$ {z.fee.toFixed(2)}</span>
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Notes */}
          <Textarea
            placeholder="Observações gerais (opcional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            className="resize-none shrink-0"
          />

          {/* Coupon */}
          <div className="flex flex-col gap-1.5 shrink-0">
            {appliedCoupon ? (
              <div className="flex items-center justify-between rounded-lg border border-green-200 bg-green-50 dark:bg-green-950/20 dark:border-green-800 px-3 py-2">
                <div className="flex items-center gap-2 text-green-700 dark:text-green-400">
                  <Tag className="h-3.5 w-3.5" />
                  <span className="text-sm font-medium">{appliedCoupon}</span>
                  <span className="text-xs">− R$ {discountAmount.toFixed(2)}</span>
                </div>
                <button onClick={removeCoupon} className="text-muted-foreground hover:text-destructive">
                  <X className="h-3.5 w-3.5" />
                </button>
              </div>
            ) : (
              <div className="flex gap-2">
                <Input
                  placeholder="Cupom de desconto"
                  value={couponInput}
                  onChange={(e) => setCouponInput(e.target.value.toUpperCase())}
                  className="flex-1"
                />
                <Button variant="outline" size="sm"
                  onClick={handleApplyCoupon}
                  disabled={!couponInput.trim() || couponLoading}
                  className="shrink-0">
                  {couponLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : "Aplicar"}
                </Button>
              </div>
            )}
            {couponError && <p className="text-xs text-destructive">{couponError}</p>}
          </div>

          {/* Price breakdown */}
          <div className="border-t border-border pt-2 flex flex-col gap-1 shrink-0">
            <div className="flex justify-between text-sm">
              <span className="text-muted-foreground">Subtotal</span>
              <span className="tabular-nums">R$ {itemsSubtotal.toFixed(2)}</span>
            </div>
            {orderType === "Delivery" && (
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Taxa de entrega</span>
                <span className="tabular-nums">
                  {selectedZone ? `R$ ${deliveryFee.toFixed(2)}` : "—"}
                </span>
              </div>
            )}
            {discountAmount > 0 && (
              <div className="flex justify-between text-sm text-green-600 dark:text-green-400">
                <span>Desconto</span>
                <span className="tabular-nums">− R$ {discountAmount.toFixed(2)}</span>
              </div>
            )}
            <div className="flex justify-between font-bold mt-1">
              <span>Total</span>
              <span className="tabular-nums">R$ {total.toFixed(2)}</span>
            </div>
          </div>
        </div>

        {mut.isError && (
          <p className="text-sm text-destructive text-center shrink-0 mt-2">
            Erro ao enviar pedido. Tente novamente.
          </p>
        )}

        <Button
          className="w-full h-12 text-base mt-4 shrink-0"
          onClick={handleSubmit}
          disabled={!canSubmit}
        >
          {mut.isPending ? "Enviando..." : `Confirmar pedido · R$ ${total.toFixed(2)}`}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
