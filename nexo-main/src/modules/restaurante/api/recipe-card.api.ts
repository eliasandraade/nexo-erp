import { apiClient } from "@/services/api-client";
import type {
  RecipeCardDto,
  CreateRecipeCardPayload,
  UpdateRecipeCardPayload,
  AddIngredientPayload,
} from "../types/recipe-card.types";

const BASE = "/restaurante/recipe-cards";

export const fetchRecipeCardByProduct = (productId: string): Promise<RecipeCardDto> =>
  apiClient.get<RecipeCardDto>(`${BASE}/product/${productId}`);

export const fetchRecipeCardById = (id: string): Promise<RecipeCardDto> =>
  apiClient.get<RecipeCardDto>(`${BASE}/${id}`);

export const createRecipeCard = (payload: CreateRecipeCardPayload): Promise<RecipeCardDto> =>
  apiClient.post<RecipeCardDto>(BASE, payload);

export const updateRecipeCard = (id: string, payload: UpdateRecipeCardPayload): Promise<RecipeCardDto> =>
  apiClient.put<RecipeCardDto>(`${BASE}/${id}`, payload);

export const addIngredient = (id: string, payload: AddIngredientPayload): Promise<RecipeCardDto> =>
  apiClient.post<RecipeCardDto>(`${BASE}/${id}/ingredients`, payload);

export const removeIngredient = (cardId: string, ingredientId: string): Promise<RecipeCardDto> =>
  apiClient.delete<RecipeCardDto>(`${BASE}/${cardId}/ingredients/${ingredientId}`);

export const uploadRecipeImage = (cardId: string, file: File): Promise<RecipeCardDto> => {
  const form = new FormData();
  form.append("file", file);
  return apiClient.postForm<RecipeCardDto>(`${BASE}/${cardId}/image`, form);
};
