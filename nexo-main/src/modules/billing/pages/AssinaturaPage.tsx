import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { CreditCard, ExternalLink, CheckCircle2, Clock, AlertTriangle, XCircle, Package } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ApiError } from "@/services/api-client";
import {
  listSubscriptions,
  createCheckout,
  createPortal,
  type SubscriptionDetail,
} from "@/services/billing.api";

// ── Module catalogue ──────────────────────────────────────────────────────────

const MODULES = [
  {
    key: "restaurante",
    label: "Restaurante & Delivery",
    description: "Salão, cozinha, cardápio digital, delivery e financeiro do restaurante.",
  },
  {
    key: "build",
    label: "Gestão de Obras",
    description: "Projetos, etapas, orçamentos e diário de obra com registro de clima.",
  },
] as const;

type BillingPeriod = "monthly" | "annual";

// ── Status display ────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, { label: string; variant: "default" | "secondary" | "destructive" | "outline"; icon: React.ReactNode }> = {
    Active:    { label: "Ativa",        variant: "default",     icon: <CheckCircle2 className="h-3 w-3" /> },
    Trialing:  { label: "Trial",        variant: "secondary",   icon: <Clock className="h-3 w-3" /> },
    PastDue:   { label: "Em atraso",    variant: "destructive", icon: <AlertTriangle className="h-3 w-3" /> },
    Canceled:  { label: "Cancelada",    variant: "outline",     icon: <XCircle className="h-3 w-3" /> },
    Suspended: { label: "Suspensa",     variant: "destructive", icon: <XCircle className="h-3 w-3" /> },
  };
  const cfg = map[status] ?? { label: status, variant: "outline" as const, icon: null };
  return (
    <Badge variant={cfg.variant} className="gap-1">
      {cfg.icon}
      {cfg.label}
    </Badge>
  );
}

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString("pt-BR");
}

// ── Module subscription card ──────────────────────────────────────────────────

interface ModuleCardProps {
  moduleKey: string;
  label: string;
  description: string;
  subscription: SubscriptionDetail | undefined;
  period: BillingPeriod;
  onSubscribe: (moduleKey: string, period: BillingPeriod) => Promise<void>;
  loading: boolean;
}

function ModuleCard({ moduleKey, label, description, subscription, period, onSubscribe, loading }: ModuleCardProps) {
  const isActive = subscription?.status === "Active" || subscription?.status === "Trialing";

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 rounded-md bg-primary/10 flex items-center justify-center shrink-0">
              <Package className="h-4 w-4 text-primary" />
            </div>
            <div>
              <CardTitle className="text-[15px]">{label}</CardTitle>
              <CardDescription className="text-[12px] mt-0.5">{description}</CardDescription>
            </div>
          </div>
          {subscription && <StatusBadge status={subscription.status} />}
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        {subscription ? (
          <div className="text-[12px] text-muted-foreground space-y-0.5">
            <p>Plano: <span className="text-foreground font-medium">{subscription.planType}</span></p>
            <p>Período atual: <span className="text-foreground">{formatDate(subscription.currentPeriodStart)} – {formatDate(subscription.currentPeriodEnd)}</span></p>
            {subscription.cancelAtPeriodEnd && (
              <p className="text-amber-500 font-medium">Cancelamento agendado para o fim do período.</p>
            )}
          </div>
        ) : (
          <Button
            size="sm"
            disabled={loading}
            onClick={() => onSubscribe(moduleKey, period)}
            className="mt-1"
          >
            {loading ? "Aguarde..." : `Assinar — ${period === "monthly" ? "Mensal" : "Anual"}`}
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function AssinaturaPage() {
  const [searchParams] = useSearchParams();
  const [period, setPeriod] = useState<BillingPeriod>("monthly");
  const [checkoutLoading, setCheckoutLoading] = useState<string | null>(null);
  const [portalLoading, setPortalLoading] = useState(false);

  // Show success toast when returning from Stripe checkout
  if (searchParams.get("sucesso") === "1") {
    toast.success("Assinatura confirmada! Acesso liberado em instantes.", { id: "checkout-success" });
  }

  const { data: subscriptions, isLoading, isError, error } = useQuery({
    queryKey: ["billing", "subscriptions"],
    queryFn: listSubscriptions,
    retry: false,
  });

  const billingUnavailable =
    isError && error instanceof ApiError && error.status === 404;

  const hasAnySubscription = (subscriptions?.length ?? 0) > 0;

  async function handleSubscribe(moduleKey: string, billingPeriod: BillingPeriod) {
    setCheckoutLoading(moduleKey);
    try {
      const { checkoutUrl } = await createCheckout(moduleKey, billingPeriod);
      window.location.href = checkoutUrl;
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao iniciar checkout.";
      toast.error(msg);
      setCheckoutLoading(null);
    }
  }

  async function handleManage() {
    setPortalLoading(true);
    try {
      const { portalUrl } = await createPortal();
      window.location.href = portalUrl;
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao abrir portal de assinatura.";
      toast.error(msg);
      setPortalLoading(false);
    }
  }

  return (
    <div className="max-w-3xl space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Assinatura</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Gerencie os módulos ativos do seu plano Orken.
          </p>
        </div>
        {hasAnySubscription && (
          <Button
            variant="outline"
            size="sm"
            disabled={portalLoading}
            onClick={handleManage}
            className="gap-1.5 shrink-0"
          >
            <ExternalLink className="h-3.5 w-3.5" />
            {portalLoading ? "Abrindo..." : "Gerenciar assinatura"}
          </Button>
        )}
      </div>

      {/* Billing unavailable */}
      {billingUnavailable && (
        <Card className="border-dashed">
          <CardContent className="py-10 text-center">
            <CreditCard className="h-8 w-8 text-muted-foreground mx-auto mb-3" />
            <p className="font-medium text-foreground">Billing em configuração</p>
            <p className="text-sm text-muted-foreground mt-1">
              O módulo de assinatura ainda não está habilitado. Entre em contato com o suporte Orken.
            </p>
          </CardContent>
        </Card>
      )}

      {/* Content when billing available */}
      {!billingUnavailable && (
        <>
          {/* Billing period toggle */}
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground">Período:</span>
            <div className="flex rounded-md border border-border overflow-hidden text-sm">
              <button
                onClick={() => setPeriod("monthly")}
                className={`px-3 py-1.5 transition-colors ${
                  period === "monthly"
                    ? "bg-primary text-primary-foreground"
                    : "hover:bg-muted text-foreground"
                }`}
              >
                Mensal
              </button>
              <button
                onClick={() => setPeriod("annual")}
                className={`px-3 py-1.5 transition-colors ${
                  period === "annual"
                    ? "bg-primary text-primary-foreground"
                    : "hover:bg-muted text-foreground"
                }`}
              >
                Anual
              </button>
            </div>
          </div>

          {/* Module cards */}
          {isLoading ? (
            <div className="space-y-3">
              {[0, 1].map((i) => (
                <div key={i} className="h-32 rounded-lg border border-border bg-muted/30 animate-pulse" />
              ))}
            </div>
          ) : (
            <div className="space-y-3">
              {MODULES.map((mod) => {
                const sub = subscriptions?.find((s) => s.moduleKey === mod.key);
                return (
                  <ModuleCard
                    key={mod.key}
                    moduleKey={mod.key}
                    label={mod.label}
                    description={mod.description}
                    subscription={sub}
                    period={period}
                    onSubscribe={handleSubscribe}
                    loading={checkoutLoading === mod.key}
                  />
                );
              })}
            </div>
          )}

          {/* No subscriptions notice */}
          {!isLoading && !hasAnySubscription && (
            <p className="text-sm text-muted-foreground">
              Nenhuma assinatura ativa. Clique em "Assinar" para adicionar um módulo.
            </p>
          )}
        </>
      )}
    </div>
  );
}
