// ── Unit ─────────────────────────────────────────────────────────────────────
// Values match the backend ProductUnit enum (case-sensitive, used in API calls)
export type ProductUnit = "Un" | "Kg" | "L" | "Cx" | "Pc" | "M";

export const productUnitLabels: Record<ProductUnit, string> = {
  Un: "Unidade",
  Kg: "Quilograma",
  L:  "Litro",
  Cx: "Caixa",
  Pc: "Pacote",
  M:  "Metro",
};

// ── Category ──────────────────────────────────────────────────────────────────
// Loaded from /api/categories at runtime. These are display-only fallbacks.
export interface CategoryDto {
  id: string;
  name: string;
  description: string | null;
  parentCategoryId: string | null;
  isActive: boolean;
}

// ── Backend DTO — shape returned by /api/products ─────────────────────────────
export interface ProductDto {
  id: string;
  code: string;
  barcode: string | null;
  name: string;
  description: string | null;
  categoryId: string | null;
  unit: string;
  costPrice: number;
  salePrice: number;
  trackStock: boolean;
  minStockQuantity: number | null;
  maxStockQuantity: number | null;
  isActive: boolean;
  isIngredient: boolean;
  createdAt: string;
  updatedAt: string;
  imageUrl: string | null;
}

// ── Form / display model ──────────────────────────────────────────────────────
// Used as local state in ProductFormPage. Field names mirror the backend DTO.
export interface Product {
  id: string;
  code: string;
  barcode: string;
  name: string;
  description: string;
  categoryId: string | null;
  unit: ProductUnit;
  isActive: boolean;
  isIngredient: boolean;
  costPrice: number;
  salePrice: number;
  trackStock: boolean;
  minStockQuantity: number;
  maxStockQuantity: number | null;
  createdAt: string;
  updatedAt: string;
  imageUrl: string | null;
}

export function dtoToProduct(dto: ProductDto): Product {
  return {
    id:               dto.id,
    code:             dto.code,
    barcode:          dto.barcode ?? "",
    name:             dto.name,
    description:      dto.description ?? "",
    categoryId:       dto.categoryId,
    unit:             (dto.unit as ProductUnit) ?? "Un",
    isActive:         dto.isActive,
    isIngredient:     dto.isIngredient,
    costPrice:        dto.costPrice,
    salePrice:        dto.salePrice,
    trackStock:       dto.trackStock,
    minStockQuantity: dto.minStockQuantity ?? 0,
    maxStockQuantity: dto.maxStockQuantity,
    createdAt:        dto.createdAt,
    updatedAt:        dto.updatedAt,
    imageUrl:         dto.imageUrl,
  };
}

export const emptyProduct: Omit<Product, "id" | "createdAt" | "updatedAt"> = {
  code:             "",
  barcode:          "",
  name:             "",
  description:      "",
  categoryId:       null,
  unit:             "Un",
  isActive:         true,
  isIngredient:     false,
  costPrice:        0,
  salePrice:        0,
  trackStock:       true,
  minStockQuantity: 0,
  maxStockQuantity: null,
  imageUrl:         null,
};
