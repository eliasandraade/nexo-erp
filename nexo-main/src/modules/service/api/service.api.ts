import { apiClient } from "@/services/api-client";

/**
 * Orken Service — typed API client. Mirrors the backend DTOs (PR1–PR6); JSON is camelCase and
 * enums are string names (global JsonStringEnumConverter). Paths are relative to the `/api` base,
 * so Service routes are `/v1/service/...`. No mocks — every call hits the real backend.
 */

// ── Enums (string unions matching the backend) ─────────────────────────────────
export type SvcSubjectKind = "Pet" | "Vehicle" | "Student" | "Dependent" | "Other";
export type SvcRecordContextType = "Customer" | "Subject" | "Order" | "Appointment" | "Package";
export type SvcAppointmentStatus =
  | "Scheduled" | "Confirmed" | "InProgress" | "Completed" | "NoShow" | "Cancelled";
export type SvcOrderStatus = "Draft" | "Open" | "InProgress" | "Completed" | "Cancelled";
export type SvcCustomerPackageStatus = "Active" | "Consumed" | "Expired" | "Cancelled";
export type SvcPaymentMethod =
  | "Cash" | "Pix" | "DebitCard" | "CreditCard" | "BankTransfer" | "Other";
export type SvcPaymentStatus = "Paid" | "Voided";

// ── Preset (adaptation source) ─────────────────────────────────────────────────
export interface ServiceLabels {
  customer: string;
  professional: string;
  catalogItem: string;
  appointment: string;
  order: string;
  subject: string;
}
export interface ServiceCapabilities {
  appointments: boolean;
  orders: boolean;
  quotes: boolean;
  parts: boolean;
  packages: boolean;
  simpleRecord: boolean;
  commissions: boolean;
  recurrence: boolean;
  subjectKind: SvcSubjectKind | null;
}
export interface ServicePresetDto {
  key: string;
  displayName: string;
  labels: ServiceLabels;
  capabilities: ServiceCapabilities;
}

export const getServicePreset = () => apiClient.get<ServicePresetDto>("/v1/service/preset");

// ── Settings (per-store internal preset — v1.1 single-module model) ─────────────
export interface ServiceSettingsDto {
  isConfigured: boolean;
  presetKey: string | null;
}
export const getServiceSettings = () =>
  apiClient.get<ServiceSettingsDto>("/v1/service/settings");
export const setServicePreset = (presetKey: string) =>
  apiClient.put<ServiceSettingsDto>("/v1/service/settings/preset", { presetKey });

// ── Public booking settings (PR12 backend) ──────────────────────────────────────
export interface PublicBookingSettingsDto {
  isConfigured: boolean;
  publicBookingEnabled: boolean;
  bookingDaysAhead: number;
  minLeadMinutes: number;
  slotIntervalMinutes: number;
  showPrices: boolean;
  autoConfirmAppointments: boolean;
  timeZoneId: string;
  // Branding (PR16) — null until configured.
  displayName: string | null;
  description: string | null;
  logoUrl: string | null;
  coverImageUrl: string | null;
  brandColor: string | null;
  whatsApp: string | null;
  address: string | null;
}
export interface UpdatePublicBookingRequest {
  publicBookingEnabled: boolean;
  bookingDaysAhead: number;
  minLeadMinutes: number;
  slotIntervalMinutes: number;
  showPrices: boolean;
  autoConfirmAppointments: boolean;
  timeZoneId: string;
}
export interface UpdatePortalBrandingRequest {
  displayName: string | null;
  description: string | null;
  logoUrl: string | null;
  coverImageUrl: string | null;
  brandColor: string | null;
  whatsApp: string | null;
  address: string | null;
}
export const getPublicBookingSettings = () =>
  apiClient.get<PublicBookingSettingsDto>("/v1/service/settings/public-booking");
export const updatePublicBookingSettings = (body: UpdatePublicBookingRequest) =>
  apiClient.put<PublicBookingSettingsDto>("/v1/service/settings/public-booking", body);
export const updatePortalBranding = (body: UpdatePortalBrandingRequest) =>
  apiClient.put<PublicBookingSettingsDto>("/v1/service/settings/branding", body);

// ── Professionals (PR1) ────────────────────────────────────────────────────────
export interface SvcProfessionalDto {
  id: string;
  storeId: string;
  name: string;
  role: string | null;
  specialty: string | null;
  color: string | null;
  phone: string | null;
  email: string | null;
  defaultCommissionPercent: number | null;
  userId: string | null;
  /** Weekly availability for the public booking portal (PR12). null = no public hours. */
  workingHoursJson: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}
export interface SaveProfessionalRequest {
  name: string;
  role?: string | null;
  specialty?: string | null;
  color?: string | null;
  phone?: string | null;
  email?: string | null;
  defaultCommissionPercent?: number | null;
  workingHoursJson?: string | null;
}

export const fetchProfessionals = (onlyActive = false) =>
  apiClient.get<SvcProfessionalDto[]>(`/v1/service/professionals?onlyActive=${onlyActive}`);
export const fetchProfessional = (id: string) =>
  apiClient.get<SvcProfessionalDto>(`/v1/service/professionals/${id}`);
export const createProfessional = (body: SaveProfessionalRequest) =>
  apiClient.post<SvcProfessionalDto>("/v1/service/professionals", body);
export const updateProfessional = (id: string, body: SaveProfessionalRequest) =>
  apiClient.put<SvcProfessionalDto>(`/v1/service/professionals/${id}`, body);
export const activateProfessional = (id: string) =>
  apiClient.post<SvcProfessionalDto>(`/v1/service/professionals/${id}/activate`);
export const deactivateProfessional = (id: string) =>
  apiClient.post<SvcProfessionalDto>(`/v1/service/professionals/${id}/deactivate`);

// ── Catalog (PR1) ──────────────────────────────────────────────────────────────
export interface SvcCatalogItemDto {
  id: string;
  storeId: string;
  name: string;
  description: string | null;
  category: string | null;
  durationMinutes: number;
  price: number;
  commissionPercent: number | null;
  requiresSubject: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}
export interface CreateCatalogItemRequest {
  name: string;
  durationMinutes: number;
  price: number;
  description?: string | null;
  category?: string | null;
  commissionPercent?: number | null;
  requiresSubject?: boolean;
}
export interface UpdateCatalogItemRequest {
  name: string;
  durationMinutes: number;
  requiresSubject: boolean;
  description?: string | null;
  category?: string | null;
}

export const fetchCatalog = (onlyActive = false) =>
  apiClient.get<SvcCatalogItemDto[]>(`/v1/service/catalog?onlyActive=${onlyActive}`);
export const fetchCatalogItem = (id: string) =>
  apiClient.get<SvcCatalogItemDto>(`/v1/service/catalog/${id}`);
export const createCatalogItem = (body: CreateCatalogItemRequest) =>
  apiClient.post<SvcCatalogItemDto>("/v1/service/catalog", body);
export const updateCatalogItem = (id: string, body: UpdateCatalogItemRequest) =>
  apiClient.put<SvcCatalogItemDto>(`/v1/service/catalog/${id}`, body);
export const activateCatalogItem = (id: string) =>
  apiClient.post<SvcCatalogItemDto>(`/v1/service/catalog/${id}/activate`);
export const deactivateCatalogItem = (id: string) =>
  apiClient.post<SvcCatalogItemDto>(`/v1/service/catalog/${id}/deactivate`);

// ── Subjects (PR2) ─────────────────────────────────────────────────────────────
export interface SvcSubjectDto {
  id: string;
  customerId: string;
  kind: SvcSubjectKind;
  displayName: string;
  metadataJson: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}
export interface CreateSubjectRequest {
  customerId: string;
  kind: SvcSubjectKind;
  displayName: string;
  metadataJson?: string | null;
  notes?: string | null;
}
export interface UpdateSubjectRequest {
  kind: SvcSubjectKind;
  displayName: string;
  metadataJson?: string | null;
  notes?: string | null;
}

export const fetchSubjects = (params: { customerId?: string; kind?: SvcSubjectKind; active?: boolean } = {}) => {
  const q = new URLSearchParams();
  if (params.customerId) q.set("customerId", params.customerId);
  if (params.kind) q.set("kind", params.kind);
  if (params.active !== undefined) q.set("active", String(params.active));
  return apiClient.get<SvcSubjectDto[]>(`/v1/service/subjects?${q.toString()}`);
};
export const fetchSubject = (id: string) => apiClient.get<SvcSubjectDto>(`/v1/service/subjects/${id}`);
export const createSubject = (body: CreateSubjectRequest) =>
  apiClient.post<SvcSubjectDto>("/v1/service/subjects", body);
export const updateSubject = (id: string, body: UpdateSubjectRequest) =>
  apiClient.put<SvcSubjectDto>(`/v1/service/subjects/${id}`, body);
export const activateSubject = (id: string) =>
  apiClient.post<SvcSubjectDto>(`/v1/service/subjects/${id}/activate`);
export const deactivateSubject = (id: string) =>
  apiClient.post<SvcSubjectDto>(`/v1/service/subjects/${id}/deactivate`);

// ── Records (PR2 + Order context PR4) ──────────────────────────────────────────
export interface SvcRecordAttachmentDto {
  storageKey: string;
  fileName: string | null;
  contentType: string | null;
  sizeBytes: number | null;
  caption: string | null;
  url: string | null;
}
export interface SvcRecordEntryDto {
  id: string;
  storeId: string;
  contextType: SvcRecordContextType;
  contextId: string;
  authorUserId: string;
  text: string | null;
  attachments: SvcRecordAttachmentDto[];
  createdAt: string;
}
export interface CreateRecordRequest {
  contextType: SvcRecordContextType; // Customer | Subject | Order
  contextId: string;
  text?: string | null;
  attachments?: Array<{
    storageKey: string;
    fileName?: string | null;
    contentType?: string | null;
    sizeBytes?: number | null;
    caption?: string | null;
  }>;
}

export const fetchRecords = (contextType: SvcRecordContextType, contextId: string) =>
  apiClient.get<SvcRecordEntryDto[]>(`/v1/service/records?contextType=${contextType}&contextId=${contextId}`);
export const fetchRecord = (id: string) => apiClient.get<SvcRecordEntryDto>(`/v1/service/records/${id}`);
export const createRecord = (body: CreateRecordRequest) =>
  apiClient.post<SvcRecordEntryDto>("/v1/service/records", body);
export const deleteRecord = (id: string) => apiClient.delete<void>(`/v1/service/records/${id}`);

// ── Appointments (PR3) ─────────────────────────────────────────────────────────
export interface SvcAppointmentDto {
  id: string;
  storeId: string;
  customerId: string;
  professionalId: string;
  catalogItemId: string;
  subjectId: string | null;
  startsAt: string;
  endsAt: string;
  status: SvcAppointmentStatus;
  notes: string | null;
  cancellationReason: string | null;
  priceSnapshot: number;
  createdAt: string;
  updatedAt: string;
}
export interface SaveAppointmentRequest {
  customerId: string;
  professionalId: string;
  catalogItemId: string;
  startsAt: string; // UTC ISO (...Z)
  endsAt: string;   // UTC ISO (...Z)
  subjectId?: string | null;
  notes?: string | null;
}
export interface ChangeAppointmentStatusRequest {
  status: SvcAppointmentStatus;
  reason?: string | null;
}

export const fetchAppointments = (params: {
  from?: string; to?: string; professionalId?: string; status?: SvcAppointmentStatus;
  customerId?: string; subjectId?: string;
} = {}) => {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v && q.set(k, String(v)));
  return apiClient.get<SvcAppointmentDto[]>(`/v1/service/appointments?${q.toString()}`);
};
export const fetchAppointment = (id: string) =>
  apiClient.get<SvcAppointmentDto>(`/v1/service/appointments/${id}`);
export const createAppointment = (body: SaveAppointmentRequest) =>
  apiClient.post<SvcAppointmentDto>("/v1/service/appointments", body);
export const updateAppointment = (id: string, body: SaveAppointmentRequest) =>
  apiClient.put<SvcAppointmentDto>(`/v1/service/appointments/${id}`, body);
export const changeAppointmentStatus = (id: string, body: ChangeAppointmentStatusRequest) =>
  apiClient.patch<SvcAppointmentDto>(`/v1/service/appointments/${id}/status`, body);

// ── Orders / OS (PR4) ──────────────────────────────────────────────────────────
export interface SvcOrderItemDto {
  id: string;
  orderId: string;
  catalogItemId: string;
  professionalId: string | null;
  nameSnapshot: string;
  descriptionSnapshot: string | null;
  quantity: number;
  unitPriceSnapshot: number;
  commissionPercentSnapshot: number | null;
  totalAmount: number;
  createdAt: string;
  updatedAt: string;
}
export interface SvcOrderDto {
  id: string;
  storeId: string;
  code: string;
  customerId: string;
  subjectId: string | null;
  professionalId: string | null;
  appointmentId: string | null;
  status: SvcOrderStatus;
  notes: string | null;
  cancellationReason: string | null;
  totalAmount: number;
  items: SvcOrderItemDto[];
  createdAt: string;
  updatedAt: string;
}
export interface CreateOrderRequest {
  customerId: string;
  subjectId?: string | null;
  professionalId?: string | null;
  notes?: string | null;
}
export interface UpdateOrderRequest {
  subjectId?: string | null;
  professionalId?: string | null;
  notes?: string | null;
}
export interface ChangeOrderStatusRequest { status: SvcOrderStatus; reason?: string | null; }
export interface AddOrderItemRequest { catalogItemId: string; quantity: number; professionalId?: string | null; }
export interface UpdateOrderItemRequest { quantity: number; professionalId?: string | null; }

export const fetchOrders = (params: {
  status?: SvcOrderStatus; customerId?: string; subjectId?: string; professionalId?: string; appointmentId?: string;
} = {}) => {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v && q.set(k, String(v)));
  return apiClient.get<SvcOrderDto[]>(`/v1/service/orders?${q.toString()}`);
};
export const fetchOrder = (id: string) => apiClient.get<SvcOrderDto>(`/v1/service/orders/${id}`);
export const createOrder = (body: CreateOrderRequest) => apiClient.post<SvcOrderDto>("/v1/service/orders", body);
export const createOrderFromAppointment = (appointmentId: string) =>
  apiClient.post<SvcOrderDto>(`/v1/service/orders/from-appointment/${appointmentId}`);
export const updateOrder = (id: string, body: UpdateOrderRequest) =>
  apiClient.put<SvcOrderDto>(`/v1/service/orders/${id}`, body);
export const changeOrderStatus = (id: string, body: ChangeOrderStatusRequest) =>
  apiClient.patch<SvcOrderDto>(`/v1/service/orders/${id}/status`, body);
export const addOrderItem = (id: string, body: AddOrderItemRequest) =>
  apiClient.post<SvcOrderDto>(`/v1/service/orders/${id}/items`, body);
export const updateOrderItem = (id: string, itemId: string, body: UpdateOrderItemRequest) =>
  apiClient.put<SvcOrderDto>(`/v1/service/orders/${id}/items/${itemId}`, body);
export const removeOrderItem = (id: string, itemId: string) =>
  apiClient.delete<SvcOrderDto>(`/v1/service/orders/${id}/items/${itemId}`);

// ── Packages (PR5) ─────────────────────────────────────────────────────────────
export interface SvcPackageItemDto {
  id: string;
  packageId: string;
  catalogItemId: string;
  nameSnapshot: string;
  includedQuantity: number;
  createdAt: string;
  updatedAt: string;
}
export interface SvcPackageDto {
  id: string;
  storeId: string;
  name: string;
  description: string | null;
  price: number;
  validityDays: number | null;
  isActive: boolean;
  items: SvcPackageItemDto[];
  createdAt: string;
  updatedAt: string;
}
export interface CreatePackageRequest { name: string; price: number; description?: string | null; validityDays?: number | null; }
export interface UpdatePackageRequest { name: string; description?: string | null; validityDays?: number | null; }
export interface AddPackageItemRequest { catalogItemId: string; includedQuantity: number; }

export const fetchPackages = (active?: boolean) =>
  apiClient.get<SvcPackageDto[]>(`/v1/service/packages${active === undefined ? "" : `?active=${active}`}`);
export const fetchPackage = (id: string) => apiClient.get<SvcPackageDto>(`/v1/service/packages/${id}`);
export const createPackage = (body: CreatePackageRequest) =>
  apiClient.post<SvcPackageDto>("/v1/service/packages", body);
export const updatePackage = (id: string, body: UpdatePackageRequest) =>
  apiClient.put<SvcPackageDto>(`/v1/service/packages/${id}`, body);
export const updatePackagePrice = (id: string, price: number) =>
  apiClient.put<SvcPackageDto>(`/v1/service/packages/${id}/price`, { price });
export const activatePackage = (id: string) => apiClient.post<SvcPackageDto>(`/v1/service/packages/${id}/activate`);
export const deactivatePackage = (id: string) => apiClient.post<SvcPackageDto>(`/v1/service/packages/${id}/deactivate`);
export const addPackageItem = (id: string, body: AddPackageItemRequest) =>
  apiClient.post<SvcPackageDto>(`/v1/service/packages/${id}/items`, body);
export const updatePackageItem = (id: string, itemId: string, includedQuantity: number) =>
  apiClient.put<SvcPackageDto>(`/v1/service/packages/${id}/items/${itemId}`, { includedQuantity });
export const removePackageItem = (id: string, itemId: string) =>
  apiClient.delete<SvcPackageDto>(`/v1/service/packages/${id}/items/${itemId}`);

// ── Customer packages (PR5) ────────────────────────────────────────────────────
export interface SvcCustomerPackageItemDto {
  id: string;
  customerPackageId: string;
  catalogItemId: string;
  nameSnapshot: string;
  totalQuantity: number;
  remainingQuantity: number;
  createdAt: string;
  updatedAt: string;
}
export interface SvcPackageUsageDto {
  id: string;
  customerPackageId: string;
  customerPackageItemId: string;
  catalogItemId: string;
  orderId: string | null;
  orderItemId: string | null;
  quantity: number;
  notes: string | null;
  createdAt: string;
}
export interface SvcCustomerPackageDto {
  id: string;
  storeId: string;
  code: string;
  packageId: string;
  customerId: string;
  subjectId: string | null;
  status: SvcCustomerPackageStatus;
  startsAt: string;
  expiresAt: string | null;
  priceSnapshot: number;
  notes: string | null;
  items: SvcCustomerPackageItemDto[];
  usages: SvcPackageUsageDto[];
  createdAt: string;
  updatedAt: string;
}
export interface AssignCustomerPackageRequest {
  packageId: string;
  customerId: string;
  startsAt: string; // UTC ISO
  subjectId?: string | null;
  notes?: string | null;
}
export interface ConsumePackageRequest {
  catalogItemId: string;
  quantity: number;
  orderId?: string | null;
  orderItemId?: string | null;
  notes?: string | null;
}

export const fetchCustomerPackages = (params: {
  customerId?: string; subjectId?: string; status?: SvcCustomerPackageStatus; packageId?: string;
} = {}) => {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v && q.set(k, String(v)));
  return apiClient.get<SvcCustomerPackageDto[]>(`/v1/service/customer-packages?${q.toString()}`);
};
export const fetchCustomerPackage = (id: string) =>
  apiClient.get<SvcCustomerPackageDto>(`/v1/service/customer-packages/${id}`);
export const assignCustomerPackage = (body: AssignCustomerPackageRequest) =>
  apiClient.post<SvcCustomerPackageDto>("/v1/service/customer-packages", body);
export const cancelCustomerPackage = (id: string) =>
  apiClient.post<SvcCustomerPackageDto>(`/v1/service/customer-packages/${id}/cancel`);
export const consumeCustomerPackage = (id: string, body: ConsumePackageRequest) =>
  apiClient.post<SvcCustomerPackageDto>(`/v1/service/customer-packages/${id}/consume`, body);
export const fetchCustomerPackageUsages = (id: string) =>
  apiClient.get<SvcPackageUsageDto[]>(`/v1/service/customer-packages/${id}/usages`);

// ── Payments (PR6) ─────────────────────────────────────────────────────────────
export interface SvcPaymentDto {
  id: string;
  storeId: string;
  customerId: string;
  orderId: string | null;
  customerPackageId: string | null;
  amount: number;
  method: SvcPaymentMethod;
  status: SvcPaymentStatus;
  paidAt: string;
  externalReference: string | null;
  notes: string | null;
  voidReason: string | null;
  voidedAt: string | null;
  createdAt: string;
  updatedAt: string;
}
export interface SvcPaymentSummaryDto {
  targetId: string;
  targetType: "Order" | "CustomerPackage";
  totalAmount: number;
  paidAmount: number;
  voidedAmount: number;
  remainingAmount: number;
  isFullyPaid: boolean;
}
export interface CreatePaymentRequest {
  amount: number;
  method: SvcPaymentMethod;
  paidAt: string; // UTC ISO
  orderId?: string | null;
  customerPackageId?: string | null;
  externalReference?: string | null;
  notes?: string | null;
}

export const fetchPayments = (params: {
  customerId?: string; orderId?: string; customerPackageId?: string;
  method?: SvcPaymentMethod; status?: SvcPaymentStatus; from?: string; to?: string;
} = {}) => {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v && q.set(k, String(v)));
  return apiClient.get<SvcPaymentDto[]>(`/v1/service/payments?${q.toString()}`);
};
export const fetchPayment = (id: string) => apiClient.get<SvcPaymentDto>(`/v1/service/payments/${id}`);
export const createPayment = (body: CreatePaymentRequest) =>
  apiClient.post<SvcPaymentDto>("/v1/service/payments", body);
export const voidPayment = (id: string, reason?: string | null) =>
  apiClient.post<SvcPaymentDto>(`/v1/service/payments/${id}/void`, { reason });
export const fetchOrderPaymentSummary = (orderId: string) =>
  apiClient.get<SvcPaymentSummaryDto>(`/v1/service/payments/order/${orderId}/summary`);
export const fetchCustomerPackagePaymentSummary = (customerPackageId: string) =>
  apiClient.get<SvcPaymentSummaryDto>(`/v1/service/payments/customer-package/${customerPackageId}/summary`);
