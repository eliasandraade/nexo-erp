import { apiClient } from "@/services/api-client";
import type { SupplierDto, SupplierFormInput } from "../types";
import { serializeAddress, serializeBankInfo } from "../types";

export function fetchSuppliers(includeInactive = false): Promise<SupplierDto[]> {
  return apiClient.get<SupplierDto[]>(
    `/suppliers${includeInactive ? "?includeInactive=true" : ""}`
  );
}

export function fetchSupplierById(id: string): Promise<SupplierDto> {
  return apiClient.get<SupplierDto>(`/suppliers/${id}`);
}

export function createSupplier(form: SupplierFormInput): Promise<SupplierDto> {
  return apiClient.post<SupplierDto>("/suppliers", {
    personType:      form.personType,
    name:            form.name,
    documentType:    form.documentType,
    documentNumber:  form.documentNumber,
    tradeName:       form.tradeName || null,
    email:           form.email || null,
    phone:           form.phone || null,
    contactName:     form.contactName || null,
    addressJson:     serializeAddress(form),
    paymentTermsDays: form.paymentTermsDays ? parseInt(form.paymentTermsDays) : null,
    bankInfoJson:    serializeBankInfo(form),
    notes:           form.notes || null,
  });
}

export function updateSupplier(id: string, form: SupplierFormInput): Promise<SupplierDto> {
  return apiClient.put<SupplierDto>(`/suppliers/${id}`, {
    name:            form.name,
    tradeName:       form.tradeName || null,
    email:           form.email || null,
    phone:           form.phone || null,
    contactName:     form.contactName || null,
    addressJson:     serializeAddress(form),
    paymentTermsDays: form.paymentTermsDays ? parseInt(form.paymentTermsDays) : null,
    bankInfoJson:    serializeBankInfo(form),
    notes:           form.notes || null,
  });
}

export function activateSupplier(id: string): Promise<void> {
  return apiClient.post<void>(`/suppliers/${id}/activate`);
}

export function deactivateSupplier(id: string): Promise<void> {
  return apiClient.post<void>(`/suppliers/${id}/deactivate`);
}
