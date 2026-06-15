import { apiClient } from "@/services/api-client";

export interface CepLookupResult {
  cep: string;
  street: string;
  neighborhood: string;
  city: string;
  state: string;
  ibgeCode?: string;
  provider: string;
}

export interface CnpjLookupResult {
  cnpj: string;
  companyName: string;
  tradeName?: string;
  status?: string;
  activityCode?: string;
  activityDescription?: string;
  address?: CepLookupResult;
  provider: string;
}

interface LookupResponse<T> {
  found: boolean;
  data: T | null;
  unavailable?: boolean;
}

export async function lookupCep(cep: string): Promise<CepLookupResult | null> {
  const digits = cep.replace(/\D/g, "");
  if (digits.length !== 8) return null;
  const response = await apiClient.get<LookupResponse<CepLookupResult>>(
    `/integrations/cep/${digits}`
  );
  if (!response.found || !response.data) return null;
  return response.data;
}

export async function lookupCnpj(cnpj: string): Promise<CnpjLookupResult | null> {
  const digits = cnpj.replace(/\D/g, "");
  if (digits.length !== 14) return null;
  const response = await apiClient.get<LookupResponse<CnpjLookupResult>>(
    `/integrations/cnpj/${digits}`
  );
  if (!response.found || !response.data) return null;
  return response.data;
}

export interface BarcodeLookupResult {
  barcode: string;
  name: string;
  brand?: string | null;
  imageUrl?: string | null;
  category?: string | null;
  quantity?: string | null;
  unit?: string | null;
  sourceProvider: string;
  confidence?: number | null;
}

export async function lookupBarcodeProduct(
  barcode: string
): Promise<{ found: boolean; data: BarcodeLookupResult | null; unavailable?: boolean }> {
  const digits = barcode.replace(/\D/g, "");
  if (digits.length < 8 || digits.length > 14) {
    return { found: false, data: null };
  }
  return apiClient.get(`/integrations/barcode/${digits}`);
}
