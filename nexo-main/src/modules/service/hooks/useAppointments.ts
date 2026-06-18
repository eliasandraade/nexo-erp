import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  changeAppointmentStatus,
  createAppointment,
  fetchAppointments,
  updateAppointment,
  type SaveAppointmentRequest,
  type SvcAppointmentStatus,
} from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

export interface AppointmentsFilter {
  from?: string;
  to?: string;
  professionalId?: string;
  status?: SvcAppointmentStatus;
  customerId?: string;
  subjectId?: string;
}

export function useAppointments(filter: AppointmentsFilter = {}) {
  return useQuery({
    queryKey: serviceKeys.appointmentsList(filter),
    queryFn: () => fetchAppointments(filter),
  });
}

export function useCreateAppointment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveAppointmentRequest) => createAppointment(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.appointments() }),
  });
}

export function useUpdateAppointment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: SaveAppointmentRequest }) =>
      updateAppointment(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.appointments() }),
  });
}

export function useChangeAppointmentStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: SvcAppointmentStatus; reason?: string | null }) =>
      changeAppointmentStatus(id, { status, reason }),
    onSuccess: () => qc.invalidateQueries({ queryKey: serviceKeys.appointments() }),
  });
}
