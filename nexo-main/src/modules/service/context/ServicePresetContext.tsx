import { createContext, useContext, useMemo, type ReactNode } from "react";
import { Outlet } from "react-router-dom";
import type {
  ServiceCapabilities,
  ServiceLabels,
  ServicePresetDto,
} from "../api/service.api";
import { useHasServiceModule } from "../hooks/useHasServiceModule";
import { useServicePresetQuery } from "../hooks/useServicePreset";

export interface ServicePresetContextValue {
  preset: ServicePresetDto | undefined;
  /** Per-vertical display terms (e.g. "Paciente" vs "Aluno"). */
  labels: ServiceLabels | undefined;
  /** Capability flags that toggle which surfaces are shown (decision D2). */
  capabilities: ServiceCapabilities | undefined;
  isLoading: boolean;
  isError: boolean;
  refetch: () => void;
}

const ServicePresetContext = createContext<ServicePresetContextValue | null>(null);

/**
 * Provides the resolved Service preset to the Service area. Used as a route element
 * (renders an `<Outlet/>`), so the shared sidebar rendered by the nested layout can read
 * capabilities and adapt the nav. The preset query is gated by `useHasServiceModule`, so it
 * never fires for non-Service tenants.
 */
export function ServicePresetProvider({ children }: { children?: ReactNode }) {
  const hasService = useHasServiceModule();
  const query = useServicePresetQuery(hasService);

  const value = useMemo<ServicePresetContextValue>(
    () => ({
      preset: query.data,
      labels: query.data?.labels,
      capabilities: query.data?.capabilities,
      isLoading: query.isLoading,
      isError: query.isError,
      refetch: () => {
        void query.refetch();
      },
    }),
    [query.data, query.isLoading, query.isError, query.refetch]
  );

  return (
    <ServicePresetContext.Provider value={value}>
      {children ?? <Outlet />}
    </ServicePresetContext.Provider>
  );
}

/** Safe read — returns null outside the provider (e.g. the shared sidebar on non-Service routes). */
export function useServicePresetOptional(): ServicePresetContextValue | null {
  return useContext(ServicePresetContext);
}

/** Strict read for screens that live inside the Service area. */
export function useServicePreset(): ServicePresetContextValue {
  const ctx = useContext(ServicePresetContext);
  if (!ctx) {
    throw new Error("useServicePreset must be used inside ServicePresetProvider");
  }
  return ctx;
}
