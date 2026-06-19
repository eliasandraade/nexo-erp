import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getPortal,
  getCatalog,
  getProfessionals,
  createAppointment,
  type CreatePublicAppointmentRequest,
} from "../api/booking.api";

const FIVE_MIN = 5 * 60 * 1000;

export function usePortal(slug: string) {
  return useQuery({
    queryKey:  ["svc-portal", slug],
    queryFn:   () => getPortal(slug),
    staleTime: FIVE_MIN,
    retry:     false,
  });
}

export function useCatalog(slug: string, enabled: boolean) {
  return useQuery({
    queryKey:  ["svc-portal-catalog", slug],
    queryFn:   () => getCatalog(slug),
    staleTime: FIVE_MIN,
    enabled,
    retry:     false,
  });
}

export function useProfessionals(slug: string, enabled: boolean) {
  return useQuery({
    queryKey:  ["svc-portal-professionals", slug],
    queryFn:   () => getProfessionals(slug),
    staleTime: FIVE_MIN,
    enabled,
    retry:     false,
  });
}

// Availability now lives in usePortalAvailability (per-professional + "no preference" union).

export function useCreateAppointment(slug: string) {
  return useMutation({
    mutationFn: (req: CreatePublicAppointmentRequest) => createAppointment(slug, req),
  });
}
