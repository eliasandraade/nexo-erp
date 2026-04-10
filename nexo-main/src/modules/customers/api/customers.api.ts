import { apiClient } from "@/services/api-client";
import type { CustomerDto, CustomerFormInput } from "../types";
import { serializeAddress } from "../types";

export function fetchCustomers(includeInactive = false): Promise<CustomerDto[]> {
  return apiClient.get<CustomerDto[]>(
    `/customers${includeInactive ? "?includeInactive=true" : ""}`
  );
}

export function fetchCustomerById(id: string): Promise<CustomerDto> {
  return apiClient.get<CustomerDto>(`/customers/${id}`);
}

export function createCustomer(form: CustomerFormInput): Promise<CustomerDto> {
  return apiClient.post<CustomerDto>("/customers", {
    personType:     form.personType,
    name:           form.name,
    documentType:   form.documentType,
    documentNumber: form.documentNumber,
    tradeName:      form.tradeName || null,
    email:          form.email || null,
    phone:          form.phone || null,
    whatsApp:       form.whatsApp || null,
    addressJson:    serializeAddress(form),
    creditLimit:    form.creditLimit ? parseFloat(form.creditLimit) : null,
    notes:          form.notes || null,
  });
}

export function updateCustomer(id: string, form: CustomerFormInput): Promise<CustomerDto> {
  return apiClient.put<CustomerDto>(`/customers/${id}`, {
    name:        form.name,
    tradeName:   form.tradeName || null,
    email:       form.email || null,
    phone:       form.phone || null,
    whatsApp:    form.whatsApp || null,
    addressJson: serializeAddress(form),
    creditLimit: form.creditLimit ? parseFloat(form.creditLimit) : null,
    notes:       form.notes || null,
  });
}

export function activateCustomer(id: string): Promise<void> {
  return apiClient.post<void>(`/customers/${id}/activate`);
}

export function deactivateCustomer(id: string): Promise<void> {
  return apiClient.post<void>(`/customers/${id}/deactivate`);
}
