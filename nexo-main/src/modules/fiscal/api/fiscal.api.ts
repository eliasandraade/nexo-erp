import { apiClient } from "@/services/api-client";

export type TaxRegime = "SimplesNacional" | "SimplesNacionalExcessoSublimite" | "Mei" | "LucroPresumido" | "LucroReal";

export const TAX_REGIMES: { value: TaxRegime; label: string }[] = [
  { value: "SimplesNacional", label: "Simples Nacional" },
  { value: "SimplesNacionalExcessoSublimite", label: "Simples Nacional — acima do sublimite" },
  { value: "Mei", label: "MEI" },
  { value: "LucroPresumido", label: "Lucro Presumido" },
  { value: "LucroReal", label: "Lucro Real" },
];

export interface FiscalProfileInput {
  legalName: string;
  tradeName: string | null;
  cnpj: string;
  stateRegistration: string;
  municipalRegistration: string | null;
  taxRegime: TaxRegime;
  street: string;
  number: string;
  complement: string | null;
  district: string;
  cityName: string;
  cityIbgeCode: string;
  state: string;
  zipCode: string;
}

export interface FiscalProfileDto extends FiscalProfileInput {
  crt: number;
  updatedAt: string;
}

export interface ProductFiscalDto {
  productId: string;
  code: string;
  name: string;
  barcode: string | null;
  ncm: string | null;
  cest: string | null;
  origin: number | null;
  cfop: string | null;
  taxSituation: string | null;
}

export type ProductFiscalInput = Pick<ProductFiscalDto, "ncm" | "cest" | "origin" | "cfop" | "taxSituation">;

export interface FiscalProductIssueDto {
  productId: string;
  code: string;
  name: string;
  barcode: string | null;
  ncm: string | null;
  problems: string[];
}

export interface FiscalReadinessDto {
  profileComplete: boolean;
  profileMissing: string[];
  activeProducts: number;
  productsWithoutNcm: number;
  productsWithInvalidBarcode: number;
  productsToFix: FiscalProductIssueDto[];
  emissionAvailable: boolean;
}

/** Origem da mercadoria (tabela da NF-e). */
export const ORIGINS: { value: number; label: string }[] = [
  { value: 0, label: "0 — Nacional" },
  { value: 1, label: "1 — Estrangeira, importação direta" },
  { value: 2, label: "2 — Estrangeira, adquirida no mercado interno" },
  { value: 3, label: "3 — Nacional, conteúdo de importação acima de 40% e até 70%" },
  { value: 4, label: "4 — Nacional, processos produtivos básicos" },
  { value: 5, label: "5 — Nacional, conteúdo de importação até 40%" },
  { value: 6, label: "6 — Estrangeira, importação direta, sem similar nacional (CAMEX)" },
  { value: 7, label: "7 — Estrangeira, mercado interno, sem similar nacional (CAMEX)" },
  { value: 8, label: "8 — Nacional, conteúdo de importação acima de 70%" },
];

export const FISCAL_KEY = ["fiscal"] as const;

/** null when the store has no issuer profile yet (204). */
export const getFiscalProfile = async () => (await apiClient.get<FiscalProfileDto | undefined>("/fiscal/profile")) ?? null;
export const saveFiscalProfile = (input: FiscalProfileInput) => apiClient.put<FiscalProfileDto>("/fiscal/profile", input);
export const getFiscalReadiness = () => apiClient.get<FiscalReadinessDto>("/fiscal/readiness");
export const getProductFiscal = (id: string) => apiClient.get<ProductFiscalDto>(`/fiscal/products/${id}`);
export const saveProductFiscal = (id: string, input: ProductFiscalInput) => apiClient.put<ProductFiscalDto>(`/fiscal/products/${id}`, input);
