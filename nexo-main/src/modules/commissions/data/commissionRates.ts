/**
 * Per-product commission rates for POS products.
 * These mirror the `defaultCommission` values from the Products module.
 *
 * Rates are expressed as decimals (e.g., 0.05 = 5%).
 *
 * When the backend is integrated, this will be replaced by a live lookup
 * against the product catalog's `defaultCommission` field.
 */
export const DEFAULT_COMMISSION_RATE = 0.05;

export const productCommissionRates: Record<string, number> = {
  "p-1":  0.05,  // Camiseta Branca M — 5%
  "p-2":  0.05,  // Bermuda Cargo G — 5%
  "p-3":  0.08,  // Tênis Runner Pro — 8%
  "p-4":  0.03,  // Meia Esportiva — 3%
  "p-5":  0.04,  // Boné Preto — 4%
  "p-6":  0.07,  // Jaqueta Corta-Vento — 7%
  "p-7":  0.06,  // Calça Moletom P — 6%
  "p-8":  0.04,  // Chinelo Slide — 4%
  "p-9":  0.08,  // Óculos de Sol Sport — 8%
  "p-10": 0.05,  // Garrafa Térmica 500ml — 5%
  "p-11": 0.05,  // Camiseta Polo Azul — 5%
  "p-12": 0.05,  // Shorts Tactel M — 5%
  "p-13": 0.07,  // Mochila Esportiva 30L — 7%
  "p-14": 0.04,  // Luva de Treino — 4%
  "p-15": 0.04,  // Cinto Casual Preto — 4%
};

export function getCommissionRate(productId: string): number {
  return productCommissionRates[productId] ?? DEFAULT_COMMISSION_RATE;
}
