import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Globe, Link2, Eye, EyeOff, Truck, ShoppingBag,
  Check, Loader2, AlertCircle, ExternalLink, Copy,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { fetchMyStores, setPublicSlug } from "@/modules/stores/services/storesApi";
import { getFoodSettings, updatePortalInfo } from "../api/restaurante.api";
import { useAuth } from "@/modules/auth/context/AuthContext";
import type { UpdatePortalInfoRequest } from "../types";

const PORTAL_BASE = "app.orken.com.br";

// ── Slug normalization (mirrors backend NormalizeSlug) ─────────────────────────
function normalizeSlug(raw: string): string {
  return raw
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")   // remove diacritics
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

  const isLoading = loadingStores || loadingSettings;

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center py-20">
        <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="p-6 max-w-xl mx-auto space-y-8">

      <div>
        <h1 className="text-xl font-semibold">Portal do Restaurante</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Configure o endereço e as informações do seu cardápio online.
        </p>
      </div>

      {/* ── 1. Endereço ── */}
      <Section
        title="Endereço do portal"
        description="Seus clientes acessam o cardápio por este link."
      >
        <div className="space-y-3">
          <Field label="Nome do restaurante (para o link)">
            <div className="flex gap-2">
              <div className="flex items-center flex-1 rounded-lg border border-border bg-muted/40 overflow-hidden focus-within:ring-1 focus-within:ring-primary focus-within:border-primary transition-all">
                <span className="pl-3 pr-1 text-xs text-muted-foreground whitespace-nowrap select-none shrink-0">
                  {PORTAL_BASE}/
                </span>
                <input
                  type="text"
                  value={slugInput}
                  onChange={(e) => setSlugInput(e.target.value)}
                  placeholder="meu-restaurante"
                  className="flex-1 bg-transparent py-2 pr-3 text-sm outline-none placeholder:text-muted-foreground/50"
                />
              </div>
              <Button
                size="sm"
                disabled={!hasSlug || slugMut.isPending || previewSlug === currentStore?.publicSlug}
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

            {slugMut.isError && (
              <div className="flex items-center gap-2 text-xs text-destructive mt-1">
                <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                {(slugMut.error as Error)?.message?.includes("409") || (slugMut.error as Error)?.message?.includes("uso")
                  ? "Este endereço já está em uso. Escolha outro."
                  : "Erro ao salvar o endereço. Tente novamente."}
              </div>
            )}
          </Field>

          {/* Preview */}
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

      {/* ── 2. Controles ── */}
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

      {/* ── 3. Identidade ── */}
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

          <Field label="URL do logotipo" hint="Link direto para a imagem (JPEG ou PNG, quadrado recomendado).">
            <div className="flex gap-2">
              <Input
                value={logoUrl}
                onChange={(e) => setLogoUrl(e.target.value)}
                placeholder="https://..."
                className="flex-1"
              />
              {logoUrl && (
                <img
                  src={logoUrl}
                  alt="Logo preview"
                  className="w-9 h-9 rounded-lg object-cover border border-border shrink-0"
                  onError={(e) => (e.currentTarget.style.display = "none")}
                />
              )}
            </div>
          </Field>

          <Field label="URL da imagem de capa" hint="Banner exibido no topo do cardápio (proporção 3:1 recomendada).">
            <div className="space-y-2">
              <Input
                value={coverImageUrl}
                onChange={(e) => setCoverImageUrl(e.target.value)}
                placeholder="https://..."
              />
              {coverImageUrl && (
                <img
                  src={coverImageUrl}
                  alt="Cover preview"
                  className="w-full h-20 rounded-lg object-cover border border-border"
                  onError={(e) => (e.currentTarget.style.display = "none")}
                />
              )}
            </div>
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

      {/* ── Save ── */}
      <div className="pt-2 flex items-center gap-3">
        <Button
          onClick={handleSavePortalInfo}
          disabled={portalMut.isPending}
          className="w-full sm:w-auto"
        >
          {portalMut.isPending ? (
            <><Loader2 className="h-4 w-4 animate-spin mr-2" />Salvando...</>
          ) : (
            <>
              <Link2 className="h-4 w-4 mr-2" />
              Salvar configurações do portal
            </>
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
    </div>
  );
}
