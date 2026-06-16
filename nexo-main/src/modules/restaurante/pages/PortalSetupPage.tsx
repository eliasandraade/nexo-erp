import { useState, useEffect, useRef } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Globe, Link2, Eye, EyeOff, Truck, ShoppingBag,
  Check, Loader2, AlertCircle, ExternalLink, Copy, X,
  Tag, Plus, Pencil, Ban, MapPin,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { ImageUploadButton } from "@/components/shared/ImageUploadButton";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { PageHeader } from "@/components/shared/PageHeader";
import { fetchMyStores, setPublicSlug, checkSlugAvailability } from "@/modules/stores/services/storesApi";
import {
  getFoodSettings,
  updatePortalInfo,
  getRestauranteDeliveryZones,
  upsertDeliveryZones,
  getCoupons,
  createCoupon,
  updateCoupon,
  revokeCoupon,
} from "../api/restaurante.api";
import type {
  CouponDto,
  CreateCouponRequest,
} from "../api/restaurante.api";
import { useAuth } from "@/modules/auth/context/AuthContext";
import type { UpdatePortalInfoRequest } from "../types";

const PORTAL_BASE = "app.orken.com.br";

// ── Fortaleza neighborhoods ────────────────────────────────────────────────────
const FORTALEZA_NEIGHBORHOODS = [
  "Aeroporto", "Água Fria", "Aldeota", "Ancuri", "Antônio Bezerra",
  "Barra do Ceará", "Bela Vista", "Benfica", "Boa Vista",
  "Bom Jardim", "Bom Sucesso", "Cais do Porto",
  "Cambeba", "Castelão", "Centro", "Cidade 2000",
  "Cidade dos Funcionários", "Cocó", "Coaçu",
  "Conjunto Ceará", "Conjunto Esperança", "Conjunto Palmeiras",
  "Curió", "Damas", "Demócrito Rocha", "Dendê",
  "Dionísio Torres", "Edson Queiroz", "Fátima",
  "Floresta", "Genibau", "Granja Lisboa", "Granja Portugal",
  "Guararapes", "Henrique Jorge", "Itaoca",
  "Jangurussu", "Jacarecanga", "Jardim América",
  "Jardim das Oliveiras", "Jardim Iracema", "João XXIII",
  "Joaquim Távora", "José Bonifácio", "Lagoa Redonda",
  "Luciano Cavalcante", "Manoel Sátiro", "Maraponga",
  "Meireles", "Messejana", "Mondubim", "Montese",
  "Mucuripe", "Parangaba", "Passaré", "Pici",
  "Pirambú", "Praia de Iracema", "Quintino Cunha",
  "Rodolfo Teófilo", "Sabiaguaba", "São João do Tauape",
  "São Gerardo", "Sapiranga", "Serrinha", "Siqueira",
  "Tauape", "Varjota", "Vila Ellery", "Vila Velha",
  "Washington Soares",
].sort();

// ── Slug normalization ─────────────────────────────────────────────────────────
function normalizeSlug(raw: string): string {
  return raw
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9\s-]/g, "")
    .trim()
    .replace(/[\s_]+/g, "-")
    .replace(/-{2,}/g, "-");
}

// ── Toggle row ─────────────────────────────────────────────────────────────────
function Toggle({
  checked, onChange, label, description, icon: Icon,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  description: string;
  icon: React.ElementType;
}) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={cn(
        "flex items-center gap-4 w-full rounded-xl border p-4 text-left transition-colors",
        checked
          ? "border-primary/40 bg-primary/5"
          : "border-border bg-card hover:border-border/80",
      )}
    >
      <div className={cn(
        "p-2 rounded-lg shrink-0",
        checked ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground",
      )}>
        <Icon className="h-4 w-4" />
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium">{label}</p>
        <p className="text-xs text-muted-foreground mt-0.5">{description}</p>
      </div>
      <div className={cn(
        "w-9 h-5 rounded-full transition-colors shrink-0 relative",
        checked ? "bg-primary" : "bg-muted",
      )}>
        <div className={cn(
          "absolute top-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform",
          checked ? "translate-x-4" : "translate-x-0.5",
        )} />
      </div>
    </button>
  );
}

// ── Section card ───────────────────────────────────────────────────────────────
function Section({ title, description, children }: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-sm font-semibold">{title}</h2>
        {description && <p className="text-xs text-muted-foreground mt-0.5">{description}</p>}
      </div>
      {children}
    </div>
  );
}

// ── Field ──────────────────────────────────────────────────────────────────────
function Field({ label, children, hint }: {
  label: string;
  children: React.ReactNode;
  hint?: string;
}) {
  return (
    <div className="space-y-1.5">
      <label className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
        {label}
      </label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

// ── Coupon form state ─────────────────────────────────────────────────────────
interface CouponFormState {
  code:                     string;
  description:              string;
  discountType:             "Percentage" | "FixedAmount" | "DeliveryFee";
  discountValue:            string;
  minOrderAmount:           string;
  minDeliveryFee:           string;
  isFirstOrderOnly:         boolean;
  restrictToCustomerPhone:  string;
  maxUses:                  string;
  validFrom:                string;
  validUntil:               string;
}

function emptyCouponForm(): CouponFormState {
  return {
    code:                    "",
    description:             "",
    discountType:            "Percentage",
    discountValue:           "",
    minOrderAmount:          "",
    minDeliveryFee:          "",
    isFirstOrderOnly:        false,
    restrictToCustomerPhone: "",
    maxUses:                 "",
    validFrom:               "",
    validUntil:              "",
  };
}

function couponToForm(c: CouponDto): CouponFormState {
  return {
    code:                    c.code,
    description:             c.description ?? "",
    discountType:            c.discountType,
    discountValue:           String(c.discountValue),
    minOrderAmount:          c.minOrderAmount != null ? String(c.minOrderAmount) : "",
    minDeliveryFee:          c.minDeliveryFee != null ? String(c.minDeliveryFee) : "",
    isFirstOrderOnly:        c.isFirstOrderOnly,
    restrictToCustomerPhone: c.restrictToCustomerPhone ?? "",
    maxUses:                 c.maxUses != null ? String(c.maxUses) : "",
    validFrom:               c.validFrom ? c.validFrom.slice(0, 10) : "",
    validUntil:              c.validUntil ? c.validUntil.slice(0, 10) : "",
  };
}

function formToRequest(f: CouponFormState): CreateCouponRequest {
  return {
    code:                    f.code.trim().toUpperCase(),
    description:             f.description.trim() || undefined,
    discountType:            f.discountType,
    discountValue:           parseFloat(f.discountValue) || 0,
    minOrderAmount:          f.minOrderAmount ? parseFloat(f.minOrderAmount) : undefined,
    minDeliveryFee:          f.minDeliveryFee ? parseFloat(f.minDeliveryFee) : undefined,
    isFirstOrderOnly:        f.isFirstOrderOnly,
    restrictToCustomerPhone: f.restrictToCustomerPhone.trim() || undefined,
    maxUses:                 f.maxUses ? parseInt(f.maxUses) : undefined,
    validFrom:               f.validFrom || undefined,
    validUntil:              f.validUntil || undefined,
  };
}

// ── Discount type label ────────────────────────────────────────────────────────
function discountLabel(type: CouponDto["discountType"], value: number): string {
  if (type === "Percentage")  return `${value}%`;
  if (type === "FixedAmount") return `R$ ${value.toFixed(2)}`;
  return "Frete grátis";
}

// ── Coupon Dialog ─────────────────────────────────────────────────────────────
function CouponDialog({
  open,
  onClose,
  editingCoupon,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  editingCoupon: CouponDto | null;
  onSaved: () => void;
}) {
  const isEdit = editingCoupon !== null;
  const [form, setForm] = useState<CouponFormState>(emptyCouponForm);

  useEffect(() => {
    if (open) {
      setForm(editingCoupon ? couponToForm(editingCoupon) : emptyCouponForm());
    }
  }, [open, editingCoupon]);

  const set = <K extends keyof CouponFormState>(key: K, value: CouponFormState[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const createMut = useMutation({
    mutationFn: (req: CreateCouponRequest) => createCoupon(req),
    onSuccess: () => { onSaved(); onClose(); },
  });

  const updateMut = useMutation({
    mutationFn: (req: CreateCouponRequest) =>
      updateCoupon(editingCoupon!.id, { ...req, code: undefined } as never),
    onSuccess: () => { onSaved(); onClose(); },
  });

  const isPending = createMut.isPending || updateMut.isPending;
  const error     = createMut.error || updateMut.error;

  function handleSubmit() {
    const req = formToRequest(form);
    if (!req.code || !req.discountValue) return;
    if (isEdit) updateMut.mutate(req);
    else createMut.mutate(req);
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Editar cupom" : "Novo cupom"}</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {/* Code */}
          {!isEdit && (
            <Field label="Código do cupom">
              <Input
                value={form.code}
                onChange={(e) => set("code", e.target.value.toUpperCase())}
                placeholder="Ex: BOAS-VINDAS"
                className="font-mono"
              />
            </Field>
          )}
          {isEdit && (
            <div className="rounded-xl border border-border bg-muted/30 px-3 py-2 flex items-center gap-2">
              <Tag className="h-3.5 w-3.5 text-muted-foreground" />
              <span className="text-sm font-mono font-medium">{editingCoupon!.code}</span>
            </div>
          )}

          {/* Description */}
          <Field label="Descrição (opcional)" hint="Uso interno, não exibida ao cliente.">
            <Input
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
              placeholder="Ex: Cupom de boas-vindas"
            />
          </Field>

          {/* Discount type + value */}
          <div className="grid grid-cols-2 gap-3">
            <Field label="Tipo de desconto">
              <Select
                value={form.discountType}
                onValueChange={(v) => set("discountType", v as CouponFormState["discountType"])}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Percentage">Percentual (%)</SelectItem>
                  <SelectItem value="FixedAmount">Valor fixo (R$)</SelectItem>
                  <SelectItem value="DeliveryFee">Desconto no frete</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field
              label={
                form.discountType === "Percentage"
                  ? "Percentual (%)"
                  : form.discountType === "FixedAmount"
                  ? "Valor (R$)"
                  : "Valor do desconto (R$)"
              }
            >
              <Input
                type="number"
                min={0}
                step={form.discountType === "Percentage" ? 1 : 0.01}
                value={form.discountValue}
                onChange={(e) => set("discountValue", e.target.value)}
                placeholder={form.discountType === "Percentage" ? "10" : "5.00"}
              />
            </Field>
          </div>

          {/* Min conditions */}
          <div className="grid grid-cols-2 gap-3">
            <Field label="Pedido mínimo (R$)" hint="Opcional">
              <Input
                type="number"
                min={0}
                step={0.01}
                value={form.minOrderAmount}
                onChange={(e) => set("minOrderAmount", e.target.value)}
                placeholder="0.00"
              />
            </Field>
            <Field label="Taxa mín. de entrega (R$)" hint="Opcional">
              <Input
                type="number"
                min={0}
                step={0.01}
                value={form.minDeliveryFee}
                onChange={(e) => set("minDeliveryFee", e.target.value)}
                placeholder="0.00"
              />
            </Field>
          </div>

          {/* Max uses */}
          <Field label="Máximo de usos" hint="Deixe vazio para ilimitado.">
            <Input
              type="number"
              min={1}
              step={1}
              value={form.maxUses}
              onChange={(e) => set("maxUses", e.target.value)}
              placeholder="Ex: 100"
            />
          </Field>

          {/* Validity */}
          <div className="grid grid-cols-2 gap-3">
            <Field label="Válido a partir de">
              <Input
                type="date"
                value={form.validFrom}
                onChange={(e) => set("validFrom", e.target.value)}
              />
            </Field>
            <Field label="Válido até">
              <Input
                type="date"
                value={form.validUntil}
                onChange={(e) => set("validUntil", e.target.value)}
              />
            </Field>
          </div>

          {/* Restrict to phone */}
          <Field label="Restringir a cliente (telefone)" hint="Opcional — deixe vazio para todos.">
            <Input
              type="tel"
              value={form.restrictToCustomerPhone}
              onChange={(e) => set("restrictToCustomerPhone", e.target.value)}
              placeholder="Ex: 85999999999"
            />
          </Field>

          {/* First order only */}
          <label className="flex items-center gap-3 cursor-pointer select-none">
            <Checkbox
              checked={form.isFirstOrderOnly}
              onCheckedChange={(v) => set("isFirstOrderOnly", !!v)}
            />
            <div>
              <p className="text-sm font-medium">Somente primeiro pedido</p>
              <p className="text-xs text-muted-foreground">
                O cupom só pode ser usado se o cliente não tiver pedidos anteriores.
              </p>
            </div>
          </label>
        </div>

        {error && (
          <div className="flex items-center gap-2 text-xs text-destructive">
            <AlertCircle className="h-3.5 w-3.5 shrink-0" />
            {(error as Error).message ?? "Erro ao salvar cupom."}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={isPending}>
            Cancelar
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={isPending || !form.code.trim() || !form.discountValue}
          >
            {isPending ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : null}
            {isEdit ? "Salvar alterações" : "Criar cupom"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────
export default function PortalSetupPage() {
  const { session } = useAuth();
  const qc = useQueryClient();
  const storeId = session?.storeId ?? "";

  // ── Data ──────────────────────────────────────────────────────────────────
  const { data: stores = [], isLoading: loadingStores } = useQuery({
    queryKey: ["stores", "mine"],
    queryFn: fetchMyStores,
    enabled: !!storeId,
  });

  const { data: settings, isLoading: loadingSettings } = useQuery({
    queryKey: ["food-settings"],
    queryFn: getFoodSettings,
    enabled: !!storeId,
  });

  const currentStore = stores.find((s) => s.id === storeId);

  // ── Slug state ────────────────────────────────────────────────────────────
  const [slugInput, setSlugInput]       = useState("");
  const [slugSaved, setSlugSaved]       = useState(false);
  const [copied, setCopied]             = useState(false);
  const previewSlug = normalizeSlug(slugInput);
  const portalUrl   = `${PORTAL_BASE}/${previewSlug}`;
  const hasSlug     = !!previewSlug && previewSlug.length >= 3;

  // ── Slug availability (debounced) ─────────────────────────────────────────
  const [debouncedSlug, setDebouncedSlug] = useState("");
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    debounceTimer.current = setTimeout(() => setDebouncedSlug(previewSlug), 500);
    return () => { if (debounceTimer.current) clearTimeout(debounceTimer.current); };
  }, [previewSlug]);

  const slugAlreadySaved = debouncedSlug === currentStore?.publicSlug;

  const { data: slugCheck, isFetching: checkingSlug } = useQuery({
    queryKey: ["slug-check", debouncedSlug, storeId],
    queryFn: () => checkSlugAvailability(debouncedSlug, storeId || undefined),
    enabled: hasSlug && debouncedSlug.length >= 3 && !slugAlreadySaved,
    staleTime: 10_000,
  });

  useEffect(() => {
    if (currentStore?.publicSlug) setSlugInput(currentStore.publicSlug);
  }, [currentStore?.publicSlug]);

  const slugMut = useMutation({
    mutationFn: () => setPublicSlug(storeId, previewSlug || null),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["stores", "mine"] });
      setSlugSaved(true);
      setTimeout(() => setSlugSaved(false), 2500);
    },
  });

  // ── Portal info state ─────────────────────────────────────────────────────
  const [acceptingOrders, setAcceptingOrders] = useState(true);
  const [deliveryEnabled, setDeliveryEnabled] = useState(true);
  const [takeawayEnabled, setTakeawayEnabled] = useState(true);
  const [displayName,     setDisplayName]     = useState("");
  const [description,     setDescription]     = useState("");
  const [logoUrl,         setLogoUrl]         = useState("");
  const [coverImageUrl,   setCoverImageUrl]   = useState("");
  const [whatsAppPhone,   setWhatsAppPhone]   = useState("");

  useEffect(() => {
    if (!settings) return;
    setAcceptingOrders(settings.acceptingOrders);
    setDeliveryEnabled(settings.deliveryEnabled);
    setTakeawayEnabled(settings.takeawayEnabled);
    setDisplayName(settings.displayName     ?? "");
    setDescription(settings.description    ?? "");
    setLogoUrl(settings.logoUrl             ?? "");
    setCoverImageUrl(settings.coverImageUrl ?? "");
    setWhatsAppPhone(settings.whatsAppPhone ?? "");
  }, [settings]);

  const portalMut = useMutation({
    mutationFn: (req: UpdatePortalInfoRequest) => updatePortalInfo(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["food-settings"] }),
  });

  function handleSavePortalInfo() {
    portalMut.mutate({
      displayName:     displayName     || null,
      logoUrl:         logoUrl         || null,
      coverImageUrl:   coverImageUrl   || null,
      description:     description     || null,
      whatsAppPhone:   whatsAppPhone   || null,
      businessHoursJson: settings?.businessHoursJson ?? null,
      acceptingOrders,
      deliveryEnabled,
      takeawayEnabled,
    });
  }

  function copyUrl() {
    navigator.clipboard.writeText(`https://${portalUrl}`).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  // ── Delivery zones state ──────────────────────────────────────────────────
  const { data: zonesData = [], isLoading: loadingZones } = useQuery({
    queryKey: ["delivery-zones"],
    queryFn: getRestauranteDeliveryZones,
    enabled: !!storeId,
  });

  // zoneFees: neighborhood → fee (only enabled neighborhoods present)
  const [zoneFees, setZoneFees] = useState<Record<string, number>>({});
  const [zonesSaved, setZonesSaved] = useState(false);

  useEffect(() => {
    const map: Record<string, number> = {};
    zonesData.forEach((z) => { map[z.neighborhood] = z.fee; });
    setZoneFees(map);
  }, [zonesData]);

  const zonesMut = useMutation({
    mutationFn: () =>
      upsertDeliveryZones({
        zones: Object.entries(zoneFees).map(([neighborhood, fee]) => ({ neighborhood, fee })),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["delivery-zones"] });
      setZonesSaved(true);
      setTimeout(() => setZonesSaved(false), 2500);
    },
  });

  function toggleNeighborhood(n: string, checked: boolean) {
    setZoneFees((prev) => {
      if (!checked) {
        const next = { ...prev };
        delete next[n];
        return next;
      }
      return { ...prev, [n]: prev[n] ?? 0 };
    });
  }

  function setNeighborhoodFee(n: string, fee: number) {
    setZoneFees((prev) => ({ ...prev, [n]: fee }));
  }

  const enabledCount = Object.keys(zoneFees).length;

  // ── Coupons state ─────────────────────────────────────────────────────────
  const { data: coupons = [], isLoading: loadingCoupons } = useQuery({
    queryKey: ["coupons"],
    queryFn: getCoupons,
    enabled: !!storeId,
  });

  const [couponDialogOpen, setCouponDialogOpen] = useState(false);
  const [editingCoupon, setEditingCoupon]       = useState<CouponDto | null>(null);
  const [revokeConfirm, setRevokeConfirm]       = useState<string | null>(null); // coupon id

  const revokeMut = useMutation({
    mutationFn: (id: string) => revokeCoupon(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["coupons"] });
      setRevokeConfirm(null);
    },
  });

  function openCreateDialog() {
    setEditingCoupon(null);
    setCouponDialogOpen(true);
  }

  function openEditDialog(c: CouponDto) {
    setEditingCoupon(c);
    setCouponDialogOpen(true);
  }

  const isLoading = loadingStores || loadingSettings;

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center py-20">
        <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="p-6 max-w-2xl mx-auto space-y-6">

      <PageHeader
        eyebrow="Orken Menu"
        title="Cardápio online"
        description="Endereço público, identidade, zonas de entrega e cupons do seu cardápio online."
      />

      <Tabs defaultValue="portal" className="w-full">
        <TabsList className="w-full justify-start">
          <TabsTrigger value="portal" className="gap-1.5">
            <Globe className="h-3.5 w-3.5" /> Portal
          </TabsTrigger>
          <TabsTrigger value="zones" className="gap-1.5">
            <MapPin className="h-3.5 w-3.5" /> Zonas de Entrega
          </TabsTrigger>
          <TabsTrigger value="coupons" className="gap-1.5">
            <Tag className="h-3.5 w-3.5" /> Cupons
          </TabsTrigger>
        </TabsList>

        {/* ══ Portal tab ══════════════════════════════════════════════════════ */}
        <TabsContent value="portal" className="mt-6 space-y-8">

          {/* 1. Endereço */}
          <Section
            title="Endereço do portal"
            description="Seus clientes acessam o cardápio por este link."
          >
            <div className="space-y-3">
              <Field label="Nome do restaurante (para o link)">
                <div className="flex gap-2">
                  <div className={cn(
                    "flex items-center flex-1 rounded-lg border bg-muted/40 overflow-hidden focus-within:ring-1 transition-all",
                    hasSlug && debouncedSlug === previewSlug && !slugAlreadySaved
                      ? slugCheck?.available === false
                        ? "border-destructive focus-within:ring-destructive focus-within:border-destructive"
                        : slugCheck?.available === true
                          ? "border-primary/60 focus-within:ring-primary focus-within:border-primary"
                          : "border-border focus-within:ring-primary focus-within:border-primary"
                      : "border-border focus-within:ring-primary focus-within:border-primary",
                  )}>
                    <span className="pl-3 pr-1 text-xs text-muted-foreground whitespace-nowrap select-none shrink-0">
                      {PORTAL_BASE}/
                    </span>
                    <input
                      type="text"
                      value={slugInput}
                      onChange={(e) => setSlugInput(e.target.value)}
                      placeholder="meu-restaurante"
                      className="flex-1 bg-transparent py-2 text-sm outline-none placeholder:text-muted-foreground/50"
                    />
                    <span className="pr-2 shrink-0">
                      {hasSlug && checkingSlug && (
                        <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                      )}
                      {hasSlug && !checkingSlug && slugAlreadySaved && (
                        <Check className="h-3.5 w-3.5 text-primary" />
                      )}
                      {hasSlug && !checkingSlug && !slugAlreadySaved && slugCheck?.available === true && (
                        <Check className="h-3.5 w-3.5 text-primary" />
                      )}
                      {hasSlug && !checkingSlug && !slugAlreadySaved && slugCheck?.available === false && (
                        <X className="h-3.5 w-3.5 text-destructive" />
                      )}
                    </span>
                  </div>
                  <Button
                    size="sm"
                    disabled={
                      !hasSlug ||
                      slugMut.isPending ||
                      checkingSlug ||
                      previewSlug === currentStore?.publicSlug ||
                      slugCheck?.available === false
                    }
                    onClick={() => slugMut.mutate()}
                    className="shrink-0"
                  >
                    {slugMut.isPending ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    ) : slugSaved ? (
                      <Check className="h-3.5 w-3.5" />
                    ) : (
                      "Salvar"
                    )}
                  </Button>
                </div>

                {hasSlug && !checkingSlug && !slugAlreadySaved && debouncedSlug === previewSlug && (
                  slugCheck?.available === false ? (
                    <div className="flex items-center gap-2 text-xs text-destructive mt-1">
                      <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                      {slugCheck.reason ?? "Este endereço já está em uso. Escolha outro."}
                    </div>
                  ) : slugCheck?.available === true ? (
                    <div className="flex items-center gap-2 text-xs text-primary mt-1">
                      <Check className="h-3.5 w-3.5 shrink-0" />
                      Endereço disponível!
                    </div>
                  ) : null
                )}

                {slugMut.isError && (
                  <div className="flex items-center gap-2 text-xs text-destructive mt-1">
                    <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                    {(slugMut.error as Error)?.message?.includes("409") || (slugMut.error as Error)?.message?.includes("uso")
                      ? "Este endereço já está em uso. Escolha outro."
                      : "Erro ao salvar o endereço. Tente novamente."}
                  </div>
                )}
              </Field>

              {hasSlug && (
                <div className="rounded-xl border border-border bg-card p-3 flex items-center gap-3">
                  <Globe className="h-4 w-4 text-primary shrink-0" />
                  <div className="flex-1 min-w-0">
                    <p className="text-xs text-muted-foreground">Link do seu cardápio</p>
                    <p className="text-sm font-medium truncate">{portalUrl}</p>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={copyUrl}
                      title="Copiar link"
                      className="p-1.5 rounded-md hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
                    >
                      {copied ? <Check className="h-3.5 w-3.5 text-primary" /> : <Copy className="h-3.5 w-3.5" />}
                    </button>
                    {currentStore?.publicSlug === previewSlug && (
                      <a
                        href={`https://${portalUrl}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        title="Abrir portal"
                        className="p-1.5 rounded-md hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
                      >
                        <ExternalLink className="h-3.5 w-3.5" />
                      </a>
                    )}
                  </div>
                </div>
              )}

              {!currentStore?.publicSlug && (
                <div className="flex items-start gap-2 text-xs text-amber-400/90 bg-amber-950/30 border border-amber-700/30 rounded-lg px-3 py-2">
                  <AlertCircle className="h-3.5 w-3.5 shrink-0 mt-0.5" />
                  O portal ainda não está ativo. Salve um endereço para ativar.
                </div>
              )}
            </div>
          </Section>

          {/* 2. Controles */}
          <Section
            title="Controles do portal"
            description="Defina o que seus clientes podem pedir."
          >
            <div className="space-y-2">
              <Toggle
                checked={acceptingOrders}
                onChange={setAcceptingOrders}
                icon={acceptingOrders ? Eye : EyeOff}
                label="Aceitando pedidos"
                description={
                  acceptingOrders
                    ? "O restaurante está aberto e recebendo pedidos."
                    : "O cardápio é exibido, mas novos pedidos não são aceitos."
                }
              />
              <Toggle
                checked={deliveryEnabled}
                onChange={setDeliveryEnabled}
                icon={Truck}
                label="Entrega habilitada"
                description="Clientes podem solicitar entrega no endereço."
              />
              <Toggle
                checked={takeawayEnabled}
                onChange={setTakeawayEnabled}
                icon={ShoppingBag}
                label="Retirada habilitada"
                description="Clientes podem pedir para retirar no local."
              />
            </div>
          </Section>

          {/* 3. Identidade */}
          <Section
            title="Identidade do portal"
            description="Informações exibidas no topo do cardápio."
          >
            <div className="space-y-4">
              <Field label="Nome de exibição" hint="Aparece no topo do cardápio. Se vazio, usa o nome da loja.">
                <Input
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder="Ex: Amigos Bar e Restô"
                />
              </Field>

              <Field label="Descrição" hint="Slogan ou descrição curta exibida abaixo do nome.">
                <Input
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Ex: Petiscos e chope gelado desde 2010"
                />
              </Field>

              <Field label="Logotipo" hint="JPEG, PNG ou WebP. Quadrado recomendado.">
                <ImageUploadButton
                  context="restaurant-logo"
                  value={logoUrl}
                  onChange={(url) => setLogoUrl(url ?? "")}
                  label="Logo"
                />
              </Field>

              <Field label="Imagem de capa" hint="Banner exibido no topo do cardápio (proporção 3:1 recomendada).">
                <ImageUploadButton
                  context="restaurant-cover"
                  value={coverImageUrl}
                  onChange={(url) => setCoverImageUrl(url ?? "")}
                  label="Capa"
                />
              </Field>

              <Field label="WhatsApp para contato" hint="Número com DDD, sem espaços. Exibido no portal para dúvidas.">
                <Input
                  value={whatsAppPhone}
                  onChange={(e) => setWhatsAppPhone(e.target.value)}
                  placeholder="Ex: 11999999999"
                  type="tel"
                />
              </Field>
            </div>
          </Section>

          {/* Save */}
          <div className="pt-2 flex items-center gap-3">
            <Button
              onClick={handleSavePortalInfo}
              disabled={portalMut.isPending}
              className="w-full sm:w-auto"
            >
              {portalMut.isPending ? (
                <><Loader2 className="h-4 w-4 animate-spin mr-2" />Salvando...</>
              ) : (
                <><Link2 className="h-4 w-4 mr-2" />Salvar configurações do portal</>
              )}
            </Button>

            {portalMut.isSuccess && (
              <span className="flex items-center gap-1.5 text-sm text-primary">
                <Check className="h-4 w-4" /> Salvo
              </span>
            )}

            {portalMut.isError && (
              <span className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-4 w-4" /> Erro ao salvar
              </span>
            )}
          </div>
        </TabsContent>

        {/* ══ Zonas de entrega tab ═════════════════════════════════════════════ */}
        <TabsContent value="zones" className="mt-6 space-y-6">
          <div>
            <h2 className="text-sm font-semibold">Bairros atendidos</h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Selecione os bairros de Fortaleza onde você faz entregas e defina a taxa para cada um.
              {enabledCount > 0 && (
                <span className="ml-1 text-primary font-medium">
                  {enabledCount} bairro{enabledCount !== 1 ? "s" : ""} ativo{enabledCount !== 1 ? "s" : ""}.
                </span>
              )}
            </p>
          </div>

          {loadingZones ? (
            <div className="flex items-center justify-center py-10">
              <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
            </div>
          ) : (
            <div className="space-y-1.5">
              {FORTALEZA_NEIGHBORHOODS.map((n) => {
                const enabled = n in zoneFees;
                return (
                  <div
                    key={n}
                    className={cn(
                      "flex items-center gap-3 rounded-lg border px-3 py-2.5 transition-colors",
                      enabled ? "border-primary/30 bg-primary/5" : "border-border bg-card",
                    )}
                  >
                    <Checkbox
                      checked={enabled}
                      onCheckedChange={(v) => toggleNeighborhood(n, !!v)}
                      id={`zone-${n}`}
                    />
                    <label
                      htmlFor={`zone-${n}`}
                      className="flex-1 text-sm cursor-pointer select-none"
                    >
                      {n}
                    </label>
                    {enabled && (
                      <div className="flex items-center gap-1.5 shrink-0">
                        <span className="text-xs text-muted-foreground">R$</span>
                        <Input
                          type="number"
                          min={0}
                          step={0.5}
                          value={zoneFees[n] ?? ""}
                          onChange={(e) => setNeighborhoodFee(n, parseFloat(e.target.value) || 0)}
                          className="w-20 h-7 text-sm text-right"
                          onClick={(e) => e.stopPropagation()}
                        />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}

          <div className="flex items-center gap-3 pt-2">
            <Button
              onClick={() => zonesMut.mutate()}
              disabled={zonesMut.isPending}
            >
              {zonesMut.isPending ? (
                <><Loader2 className="h-4 w-4 animate-spin mr-2" />Salvando...</>
              ) : (
                <><Check className="h-4 w-4 mr-2" />Salvar zonas de entrega</>
              )}
            </Button>

            {zonesSaved && (
              <span className="flex items-center gap-1.5 text-sm text-primary">
                <Check className="h-4 w-4" /> Salvo
              </span>
            )}

            {zonesMut.isError && (
              <span className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-4 w-4" /> Erro ao salvar
              </span>
            )}
          </div>
        </TabsContent>

        {/* ══ Cupons tab ══════════════════════════════════════════════════════ */}
        <TabsContent value="coupons" className="mt-6 space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-sm font-semibold">Cupons de desconto</h2>
              <p className="text-xs text-muted-foreground mt-0.5">
                Crie cupons para oferecer descontos nos pedidos do portal.
              </p>
            </div>
            <Button size="sm" onClick={openCreateDialog}>
              <Plus className="h-3.5 w-3.5 mr-1" /> Novo cupom
            </Button>
          </div>

          {loadingCoupons ? (
            <div className="flex items-center justify-center py-10">
              <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
            </div>
          ) : coupons.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-8 text-center">
              <Tag className="h-8 w-8 text-muted-foreground/40 mx-auto mb-2" />
              <p className="text-sm text-muted-foreground">Nenhum cupom cadastrado.</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                Clique em "Novo cupom" para criar o primeiro.
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              {coupons.map((c) => (
                <div
                  key={c.id}
                  className={cn(
                    "rounded-xl border px-4 py-3 flex items-center gap-3 transition-colors",
                    c.isActive ? "border-border bg-card" : "border-border/40 bg-muted/20",
                  )}
                >
                  {/* Code + discount */}
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-mono text-sm font-semibold">{c.code}</span>
                      <Badge variant={c.isActive ? "default" : "secondary"} className="text-xs">
                        {discountLabel(c.discountType, c.discountValue)}
                      </Badge>
                      {!c.isActive && (
                        <Badge variant="destructive" className="text-xs">Revogado</Badge>
                      )}
                    </div>
                    <div className="flex flex-wrap items-center gap-2 mt-1">
                      {c.description && (
                        <span className="text-xs text-muted-foreground">{c.description}</span>
                      )}
                      {c.minOrderAmount != null && (
                        <span className="text-xs text-muted-foreground">
                          Mín. R$ {c.minOrderAmount.toFixed(2)}
                        </span>
                      )}
                      {c.isFirstOrderOnly && (
                        <span className="text-xs text-muted-foreground">Primeiro pedido</span>
                      )}
                      <span className="text-xs text-muted-foreground">
                        {c.maxUses != null
                          ? `${c.usedCount}/${c.maxUses} usos`
                          : `${c.usedCount} usos`}
                      </span>
                      {c.validUntil && (
                        <span className="text-xs text-muted-foreground">
                          Até {new Date(c.validUntil).toLocaleDateString("pt-BR")}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Actions */}
                  {c.isActive && (
                    <div className="flex items-center gap-1 shrink-0">
                      <button
                        onClick={() => openEditDialog(c)}
                        title="Editar"
                        className="p-1.5 rounded-md hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </button>
                      <button
                        onClick={() => setRevokeConfirm(c.id)}
                        title="Revogar"
                        className="p-1.5 rounded-md hover:bg-muted transition-colors text-muted-foreground hover:text-destructive"
                      >
                        <Ban className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </TabsContent>
      </Tabs>

      {/* ── Coupon dialog ──────────────────────────────────────────────────── */}
      <CouponDialog
        open={couponDialogOpen}
        onClose={() => setCouponDialogOpen(false)}
        editingCoupon={editingCoupon}
        onSaved={() => qc.invalidateQueries({ queryKey: ["coupons"] })}
      />

      {/* ── Revoke confirm dialog ──────────────────────────────────────────── */}
      <Dialog
        open={!!revokeConfirm}
        onOpenChange={(o) => !o && setRevokeConfirm(null)}
      >
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Revogar cupom?</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Esta ação é permanente. O cupom não poderá mais ser usado por novos clientes,
            mas os usos anteriores são mantidos.
          </p>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setRevokeConfirm(null)}
              disabled={revokeMut.isPending}
            >
              Cancelar
            </Button>
            <Button
              variant="destructive"
              onClick={() => revokeConfirm && revokeMut.mutate(revokeConfirm)}
              disabled={revokeMut.isPending}
            >
              {revokeMut.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin mr-2" />
              ) : (
                <Ban className="h-4 w-4 mr-2" />
              )}
              Revogar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
