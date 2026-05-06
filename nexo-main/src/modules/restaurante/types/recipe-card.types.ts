export interface PrepStepDto {
  order: number;
  description: string;
  durationMinutes: number | null;
}

export interface RecipeIngredientDto {
  id: string;
  ingredientProductId: string;
  ingredientName: string;
  ingredientCode: string;
  quantity: number;
  unit: string;
  currentCostPrice: number;
  lineCost: number;
}

export interface RecipeCardDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  salePrice: number;
  imageUrl: string | null;
  yield: number;
  yieldUnit: string;
  hasPrep: boolean;
  prepSteps: PrepStepDto[];
  totalPrepTimeMin: number | null;
  assemblyNotes: string | null;
  requiresPackaging: boolean;
  packagingProductId: string | null;
  packagingProductName: string | null;
  isActive: boolean;
  notes: string | null;
  ingredientCost: number;
  gasCost: number;
  laborCost: number;
  calculatedCost: number;
  cmvPercent: number;
  ingredients: RecipeIngredientDto[];
  createdAt: string;
}

export interface CreateRecipeCardPayload {
  productId: string;
  yield: number;
  yieldUnit: string;
  hasPrep?: boolean;
  notes?: string | null;
}

export interface UpdateRecipeCardPayload {
  yield: number;
  yieldUnit: string;
  hasPrep: boolean;
  prepSteps: PrepStepDto[];
  assemblyNotes: string | null;
  requiresPackaging: boolean;
  packagingProductId: string | null;
  notes?: string | null;
}

export interface AddIngredientPayload {
  ingredientProductId: string;
  quantity: number;
  unit: string;
}
