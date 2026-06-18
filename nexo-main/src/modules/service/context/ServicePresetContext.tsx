import { createContext, useContext, useMemo, type ReactNode } from "react";
import { Outlet } from "react-router-dom";
import type {
  ServiceCapabilities,
  ServiceLabels,
  ServicePresetDto,
} from "../api/service.api";
import { useHasServiceModule } from "../hooks/useHasServiceModule";
import { useServicePresetQuery } from "../hooks/useServicePreset";
import { useServiceSettings } from "../hooks/useServiceSettings";
import ServiceOnboardingPage from "../pages/ServiceOnboardingPage";

export interface ServicePresetContextValue {
  preset: ServicePresetDto | undefined;
  /** Per-vertical display terms (e.g. "Paciente" vs "Aluno"). */
  labels: ServiceLabels | undefined;
  /** Capability flags that toggle which surfaces are shown (decision D2). */
  capabilities: ServiceCapabilities | undefined;
  /** Whether the active store has chosen a preset (vertical). */
  isConfigured: boolean;
  presetKey: string | null;
  isLoading: boolean;
  isError: boolean;
  refetch: () => void;
}

const ServicePresetContext = createContext<ServicePresetContextValue | null>(null);

/**
 * Provides the resolved Service preset to the Service area (v1.1 single-module model). The
 * commercial entitlement is the single `service` module; the vertical is configured per store
 * via SvcSettings. Used as a route element:
 *   - while settings load → spinner;
 *   - if the store hasn't chosen a vertical → full-screen onboarding (no app shell);
 *   - once configured → renders the layout + screens, with capabilities for the sidebar.
 */
export function ServicePresetProvider({ children }: { children?: ReactNode }) {
  const hasService = useHasServiceModule();
  const settingsQ = useServiceSettings(hasService);
  const isConfigured = settingsQ.data?.isConfigured ?? false;
  const presetQ = useServicePresetQuery(hasService && isConfigured);

  const value = useMemo<ServicePresetContextValue>(
    () => ({
      preset: presetQ.data,
      labels: presetQ.data?.labels,
      capabilities: presetQ.data?.capabilities,
      isConfigured,
      presetKey: settingsQ.data?.presetKey ?? null,
      isLoading: presetQ.isLoading,
      isError: presetQ.isError,
      refetch: () => {
        void presetQ.refetch();
      },
    }),
    [presetQ.data, presetQ.isLoading, presetQ.isError, presetQ.refetch, isConfigured, settingsQ.data?.presetKey]
  );

  // Onboarding gate (only for Service tenants — the route guard already enforces that).
  if (hasService) {
    if (settingsQ.isLoading) {
      return (
        <div className="flex h-screen items-center justify-center bg-background">
          <div className="h-6 w-6 animate-spin rounded-full border-2 border-muted border-t-primary" />
        </div>
      );
    }
    if (!settingsQ.isError && !isConfigured) {
      return <ServiceOnboardingPage />;
    }
  }

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
