// ── Backend PersonType/DocumentType values ────────────────────────────────────
export type SupplierPersonType = "Individual" | "Company";

// ── Address stored as JSON in the backend ─────────────────────────────────────
export interface SupplierAddress {
  zipCode?: string;
  street?: string;
  number?: string;
  complement?: string;
  neighborhood?: string;
  city?: string;
  state?: string;
}

// ── Bank info stored as JSON in the backend ───────────────────────────────────
export interface SupplierBankInfo {
  bank?: string;
  agency?: string;
  account?: string;
  pixKey?: string;
}

// ── Backend DTO — shape returned by /api/suppliers ────────────────────────────
export interface SupplierDto {
  id: string;
  personType: string;           // "Individual" | "Company"
  name: string;
  tradeName: string | null;
  documentType: string;         // "CPF" | "CNPJ" | "Other"
  documentNumber: string;
  email: string | null;
  phone: string | null;
  contactName: string | null;
  addressJson: string | null;
  paymentTermsDays: number | null;
  bankInfoJson: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Local form state ──────────────────────────────────────────────────────────
export interface SupplierFormInput {
  personType: SupplierPersonType;
  name: string;
  tradeName: string;
  documentType: "CPF" | "CNPJ" | "Other";
  documentNumber: string;
  isActive: boolean;
  email: string;
  phone: string;
  contactName: string;
  // address
  zipCode: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
  // commercial
  paymentTermsDays: string;
  notes: string;
  // bank info
  bankName: string;
  bankAgency: string;
  bankAccount: string;
  pixKey: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

export function parseAddress(json: string | null): SupplierAddress {
  if (!json) return {};
  try { return JSON.parse(json) as SupplierAddress; }
  catch { return {}; }
}

export function serializeAddress(form: SupplierFormInput): string | null {
  const addr: SupplierAddress = {
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

export function parseBankInfo(json: string | null): SupplierBankInfo {
  if (!json) return {};
  try { return JSON.parse(json) as SupplierBankInfo; }
  catch { return {}; }
}

export function serializeBankInfo(form: SupplierFormInput): string | null {
  const info: SupplierBankInfo = {
    bank:    form.bankName || undefined,
    agency:  form.bankAgency || undefined,
    account: form.bankAccount || undefined,
    pixKey:  form.pixKey || undefined,
  };
  const hasData = Object.values(info).some(Boolean);
  return hasData ? JSON.stringify(info) : null;
}

export function dtoToFormInput(dto: SupplierDto): SupplierFormInput {
  const addr = parseAddress(dto.addressJson);
  const bank = parseBankInfo(dto.bankInfoJson);
  return {
    personType:    (dto.personType as SupplierPersonType) ?? "Company",
    name:          dto.name,
    tradeName:     dto.tradeName ?? "",
    documentType:  (dto.documentType as SupplierFormInput["documentType"]) ?? "CNPJ",
    documentNumber: dto.documentNumber,
    isActive:      dto.isActive,
    email:         dto.email ?? "",
    phone:         dto.phone ?? "",
    contactName:   dto.contactName ?? "",
    zipCode:       addr.zipCode ?? "",
    street:        addr.street ?? "",
    number:        addr.number ?? "",
    complement:    addr.complement ?? "",
    neighborhood:  addr.neighborhood ?? "",
    city:          addr.city ?? "",
    state:         addr.state ?? "",
    paymentTermsDays: dto.paymentTermsDays?.toString() ?? "",
    notes:         dto.notes ?? "",
    bankName:      bank.bank ?? "",
    bankAgency:    bank.agency ?? "",
    bankAccount:   bank.account ?? "",
    pixKey:        bank.pixKey ?? "",
  };
}

export const emptySupplierForm: SupplierFormInput = {
  personType:    "Company",
  name:          "",
  tradeName:     "",
  documentType:  "CNPJ",
  documentNumber: "",
  isActive:      true,
  email:         "",
  phone:         "",
  contactName:   "",
  zipCode:       "",
  street:        "",
  number:        "",
  complement:    "",
  neighborhood:  "",
  city:          "",
  state:         "",
  paymentTermsDays: "",
  notes:         "",
  bankName:      "",
  bankAgency:    "",
  bankAccount:   "",
  pixKey:        "",
};
