import { Link } from "react-router-dom";
import {
  CalendarClock,
  ClipboardList,
  CreditCard,
  PackageCheck,
  type LucideIcon,
} from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { PageSkeleton } from "@/components/shared/PageSkeleton";
import { ErrorState } from "@/components/shared/ErrorState";
import { Skeleton } from "@/components/ui/skeleton";
import { formatCurrency } from "@/lib/formatters";
import { useServicePreset } from "../context/ServicePresetContext";
import { useServiceDashboard } from "../hooks/useServiceDashboard";
import { enabledSurfaces } from "../lib/service-surfaces";
import { appointmentStats, openOrdersStats, paidTotal } from "../lib/dashboard";

/**
 * Service dashboard + area landing. Real and preset-driven (decision D2): KPIs are derived
 * client-side from the list endpoints (there are no aggregate endpoints yet — documented gap,
 * no invented numbers), each gated by capability. Below, the surface shortcuts the vertical
 * actually supports.
 */
export default function ServiceOverviewPage() {
  const { preset, isLoading, isError, refetch } = useServicePreset();
  const dash = useServiceDashboard(preset?.capabilities);

  if (isLoading) return <PageSkeleton />;

  if (isError || !preset) {
    return (
      <div className="space-y-6">
        <PageHeader title="Serviços" description="Operação de serviços do seu negócio." />
        <ErrorState onRetry={refetch} />
      </div>
    );
  }

  const caps = preset.capabilities;
  const surfaces = enabledSurfaces(preset);

  const apptStats = dash.appointments.data ? appointmentStats(dash.appointments.data) : null;
  const openOrders = dash.orders.data ? openOrdersStats(dash.orders.data) : null;
  const paid = dash.payments.data ? paidTotal(dash.payments.data) : null;
  const activePkgs = dash.customerPackages.data?.length ?? null;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={preset.displayName}
        title="Serviços"
        description="Visão do dia e atalhos operacionais."
      />

      {/* KPIs — real, capability-gated */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {caps.appointments && (
          <KpiCard
            to="/service/agenda" icon={CalendarClock} label="Agenda de hoje"
            loading={dash.appointments.isLoading} error={dash.appointments.isError}
            value={apptStats ? String(apptStats.total) : "—"}
            hint={apptStats ? `${apptStats.done} concluídos · ${apptStats.remaining} restantes` : undefined}
          />
        )}
        {(caps.orders || caps.packages) && (
          <KpiCard
            to="/service/pagamentos" icon={CreditCard} label="Recebido hoje"
            loading={dash.payments.isLoading} error={dash.payments.isError}
            value={paid != null ? formatCurrency(paid) : "—"}
          />
        )}
        {caps.orders && (
          <KpiCard
            to="/service/ordens" icon={ClipboardList} label="Ordens abertas"
            loading={dash.orders.isLoading} error={dash.orders.isError}
            value={openOrders ? String(openOrders.count) : "—"}
            hint={openOrders ? `${formatCurrency(openOrders.total)} em aberto` : undefined}
          />
        )}
        {caps.packages && (
          <KpiCard
            to="/service/customer-packages" icon={PackageCheck} label="Pacotes ativos"
            loading={dash.customerPackages.isLoading} error={dash.customerPackages.isError}
            value={activePkgs != null ? String(activePkgs) : "—"}
          />
        )}
      </div>

      {/* Shortcuts */}
      <div className="space-y-2">
        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted-foreground">Atalhos</p>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {surfaces.map((surface) => {
            const Icon = surface.icon;
            return (
              <Link
                key={surface.key}
                to={surface.path}
                className="group flex items-start gap-3 rounded-lg border border-border bg-card p-4 transition-colors hover:border-primary/50 hover:bg-accent/40"
              >
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
                  <Icon className="h-[18px] w-[18px]" />
                </span>
                <div className="min-w-0">
                  <p className="text-[13.5px] font-semibold text-foreground">{surface.label(preset)}</p>
                  <p className="mt-0.5 text-[12px] leading-snug text-muted-foreground">{surface.description(preset)}</p>
                </div>
              </Link>
            );
          })}
        </div>
      </div>
    </div>
  );
}

interface KpiCardProps {
  to: string;
  icon: LucideIcon;
  label: string;
  value: string;
  hint?: string;
  loading?: boolean;
  error?: boolean;
}

function KpiCard({ to, icon: Icon, label, value, hint, loading, error }: KpiCardProps) {
  return (
    <Link
      to={to}
      className="group rounded-lg border border-border bg-card p-4 transition-colors hover:border-primary/50"
    >
      <div className="flex items-center justify-between">
        <p className="text-[11.5px] font-medium text-muted-foreground">{label}</p>
        <Icon className="h-4 w-4 text-muted-foreground/70 transition-colors group-hover:text-primary" />
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-6 w-20" />
      ) : (
        <p className="mt-1.5 text-[20px] font-bold leading-tight text-foreground">{error ? "—" : value}</p>
      )}
      {hint && !loading && !error && (
        <p className="mt-0.5 text-[11px] text-muted-foreground">{hint}</p>
      )}
    </Link>
  );
}
