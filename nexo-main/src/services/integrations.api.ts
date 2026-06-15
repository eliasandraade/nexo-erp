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
