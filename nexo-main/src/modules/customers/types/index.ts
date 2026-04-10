// ── Backend PersonType values ─────────────────────────────────────────────────
// "Individual" = pessoa física, "Company" = pessoa jurídica
export type CustomerPersonType = "Individual" | "Company";

// ── Address stored as JSON in the backend ─────────────────────────────────────
export interface CustomerAddress {
  zipCode?: string;
  street?: string;
  number?: string;
  complement?: string;
  neighborhood?: string;
  city?: string;
  state?: string;
}

// ── Backend DTO — shape returned by /api/customers ────────────────────────────
export interface CustomerDto {
  id: string;
  personType: string;       // "Individual" | "Company"
  name: string;
  tradeName: string | null;
  documentType: string;     // "CPF" | "CNPJ" | "Other"
  documentNumber: string;
  email: string | null;
  phone: string | null;
  whatsApp: string | null;
  addressJson: string | null;
  creditLimit: number | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Parsed customer with structured address (for display) ─────────────────────
export interface Customer extends Omit<CustomerDto, "addressJson"> {
  address: CustomerAddress;
}

// ── Local form state ──────────────────────────────────────────────────────────
export interface CustomerFormInput {
  personType: CustomerPersonType;
  name: string;
  tradeName: string;
  documentType: "CPF" | "CNPJ" | "Other";
  documentNumber: string;
  email: string;
  phone: string;
  whatsApp: string;
  zipCode: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
  creditLimit: string;
  notes: string;
  isActive: boolean;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

export function parseAddress(json: string | null): CustomerAddress {
  if (!json) return {};
  try { return JSON.parse(json) as CustomerAddress; }
  catch { return {}; }
}

export function serializeAddress(form: CustomerFormInput): string | null {
  const addr: CustomerAddress = {
    zipCode:      form.zipCode || undefined,
    street:       form.street || undefined,
    number:       form.number || undefined,
    complement:   form.complement || undefined,
    neighborhood: form.neighborhood || undefined,
    city:         form.city || undefined,
    state:        form.state || undefined,
  };
  const hasData = Object.values(addr).some(Boolean);
  return hasData ? JSON.stringify(addr) : null;
}

export function dtoToCustomer(dto: CustomerDto): Customer {
  return { ...dto, address: parseAddress(dto.addressJson) };
}

export function dtoToFormInput(dto: CustomerDto): CustomerFormInput {
  const addr = parseAddress(dto.addressJson);
  return {
    personType:   (dto.personType as CustomerPersonType) ?? "Individual",
    name:         dto.name,
    tradeName:    dto.tradeName ?? "",
    documentType: (dto.documentType as CustomerFormInput["documentType"]) ?? "CPF",
    documentNumber: dto.documentNumber,
    email:        dto.email ?? "",
    phone:        dto.phone ?? "",
    whatsApp:     dto.whatsApp ?? "",
    zipCode:      addr.zipCode ?? "",
    street:       addr.street ?? "",
    number:       addr.number ?? "",
    complement:   addr.complement ?? "",
    neighborhood: addr.neighborhood ?? "",
    city:         addr.city ?? "",
    state:        addr.state ?? "",
    creditLimit:  dto.creditLimit?.toString() ?? "",
    notes:        dto.notes ?? "",
    isActive:     dto.isActive,
  };
}

export const emptyCustomerForm: CustomerFormInput = {
  personType:    "Individual",
  name:          "",
  tradeName:     "",
  documentType:  "CPF",
  documentNumber: "",
  email:         "",
  phone:         "",
  whatsApp:      "",
  zipCode:       "",
  street:        "",
  number:        "",
  complement:    "",
  neighborhood:  "",
  city:          "",
  state:         "",
  creditLimit:   "",
  notes:         "",
  isActive:      true,
};
