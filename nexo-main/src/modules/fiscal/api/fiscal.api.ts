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
  operationNature: string | null;
  nfceSeries: number | null;
  nfceNextNumber: number | null;
  nfeSeries: number | null;
  nfeNextNumber: number | null;
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
  taxUnit: string | null;
  /** The product's own unit (used when there is no tax unit). */
  unit: string | null;
}

export type ProductFiscalInput = Pick<ProductFiscalDto, "ncm" | "cest" | "origin" | "cfop" | "taxSituation" | "taxUnit">;

export type TaxpayerIndicator = "ContribuinteIcms" | "Isento" | "NaoContribuinte";

export const TAXPAYER_INDICATORS: { value: TaxpayerIndicator; label: string }[] = [
  { value: "ContribuinteIcms", label: "Contribuinte do ICMS (tem IE)" },
  { value: "Isento", label: "Isento de inscrição estadual" },
  { value: "NaoContribuinte", label: "Não contribuinte (consumidor)" },
];

export interface CustomerFiscalDto {
  customerId: string;
  name: string;
  documentType: string;
  documentNumber: string;
  documentValid: boolean;
  stateRegistration: string | null;
  taxpayerIndicator: TaxpayerIndicator | null;
  cityIbgeCode: string | null;
}

export type CustomerFiscalInput = Pick<CustomerFiscalDto, "stateRegistration" | "taxpayerIndicator" | "cityIbgeCode">;

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
  provider: string;
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
export const getCustomerFiscal = (id: string) => apiClient.get<CustomerFiscalDto>(`/fiscal/customers/${id}`);
export const saveCustomerFiscal = (id: string, input: CustomerFiscalInput) => apiClient.put<CustomerFiscalDto>(`/fiscal/customers/${id}`, input);
