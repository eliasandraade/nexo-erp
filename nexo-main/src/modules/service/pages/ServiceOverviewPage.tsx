import { Link } from "react-router-dom";
import { PageHeader } from "@/components/shared/PageHeader";
import { PageSkeleton } from "@/components/shared/PageSkeleton";
import { ErrorState } from "@/components/shared/ErrorState";
import { useServicePreset } from "../context/ServicePresetContext";
import { enabledSurfaces } from "../lib/service-surfaces";

/**
 * Service area landing. Real, preset-driven: it lists exactly the surfaces the tenant's
 * vertical supports (decision D2) and links into them. No mock data — every card is derived
 * from `GET /v1/service/preset`. The surface routes themselves land in the stacked PRs.
 */
export default function ServiceOverviewPage() {
  const { preset, isLoading, isError, refetch } = useServicePreset();

  if (isLoading) return <PageSkeleton />;

  if (isError || !preset) {
    return (
      <div className="space-y-6">
        <PageHeader title="Serviços" description="Operação de serviços do seu negócio." />
        <ErrorState onRetry={refetch} />
      </div>
    );
  }

  const surfaces = enabledSurfaces(preset);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={preset.displayName}
        title="Serviços"
        description="Escolha uma área para começar a operar."
      />

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
                <p className="text-[13.5px] font-semibold text-foreground">
                  {surface.label(preset)}
                </p>
                <p className="mt-0.5 text-[12px] leading-snug text-muted-foreground">
                  {surface.description(preset)}
                </p>
              </div>
            </Link>
          );
        })}
      </div>
    </div>
  );
}
