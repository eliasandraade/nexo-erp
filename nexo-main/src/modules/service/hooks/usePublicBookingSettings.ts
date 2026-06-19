import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getPublicBookingSettings,
  updatePublicBookingSettings,
  updatePortalBranding,
  type PublicBookingSettingsDto,
  type UpdatePublicBookingRequest,
  type UpdatePortalBrandingRequest,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

/**
 * Public booking configuration for the active store (PR12 backend). `enabled` gates the call so
 * non-Service tenants never hit it. Consumed by the Portal settings page and the preset context
 * (which uses `publicBookingEnabled` to keep the Agenda discoverable for verticals without the
 * appointments capability once public booking is on).
 */
export function usePublicBookingSettings(enabled: boolean) {
  return useQuery<PublicBookingSettingsDto>({
    queryKey: serviceKeys.publicBooking(),
    queryFn: getPublicBookingSettings,
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

export function useUpdatePublicBookingSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePublicBookingRequest) => updatePublicBookingSettings(body),
    onSuccess: (data) => {
      // Seed the cache + invalidate so the sidebar/overview react immediately (Agenda visibility).
      qc.setQueryData(serviceKeys.publicBooking(), data);
      qc.invalidateQueries({ queryKey: serviceKeys.publicBooking() });
    },
  });
}

/** Updates the portal branding (separate endpoint so it never collides with the booking config). */
export function useUpdatePortalBranding() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePortalBrandingRequest) => updatePortalBranding(body),
    onSuccess: (data) => {
      qc.setQueryData(serviceKeys.publicBooking(), data);
      qc.invalidateQueries({ queryKey: serviceKeys.publicBooking() });
    },
  });
}
