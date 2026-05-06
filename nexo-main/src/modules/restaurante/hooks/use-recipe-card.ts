import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchRecipeCardByProduct,
  createRecipeCard,
  updateRecipeCard,
  addIngredient,
  removeIngredient,
  uploadRecipeImage,
} from "../api/recipe-card.api";
import type {
  CreateRecipeCardPayload,
  UpdateRecipeCardPayload,
  AddIngredientPayload,
} from "../types/recipe-card.types";

export const RECIPE_CARD_KEY = (productId: string) =>
  ["recipe-card", "product", productId] as const;

export function useRecipeCardByProduct(productId: string) {
  return useQuery({
    queryKey: RECIPE_CARD_KEY(productId),
    queryFn:  () => fetchRecipeCardByProduct(productId),
    retry:    false,
  });
}

export function useCreateRecipeCard(productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateRecipeCardPayload) => createRecipeCard(payload),
    onSuccess:  () => qc.invalidateQueries({ queryKey: RECIPE_CARD_KEY(productId) }),
  });
}

export function useUpdateRecipeCard(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateRecipeCardPayload) => updateRecipeCard(cardId, payload),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useAddIngredient(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: AddIngredientPayload) => addIngredient(cardId, payload),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useRemoveIngredient(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ingredientId: string) => removeIngredient(cardId, ingredientId),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useUploadRecipeImage(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => uploadRecipeImage(cardId, file),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}
