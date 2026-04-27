import { apiClient } from "@/services/api-client";
import type { StoreDto } from "../types";

export async function fetchMyStores(): Promise<StoreDto[]> {
  return apiClient.get<StoreDto[]>("/stores");
}

export async function setPublicSlug(
  storeId: string,
  publicSlug: string | null,
): Promise<{ publicSlug: string | null }> {
  return apiClient.patch<{ publicSlug: string | null }>(
    `/stores/${storeId}/public-slug`,
    { publicSlug },
  );
}

export interface SlugCheckResult {
  available: boolean;
  normalized: string;
  reason?: string;
}

export async function checkSlugAvailability(
  slug: string,
  excludeStoreId?: string,
): Promise<SlugCheckResult> {
  const params = new URLSearchParams({ slug });
  if (excludeStoreId) params.set("excludeStoreId", excludeStoreId);
  return apiClient.get<SlugCheckResult>(`/stores/check-slug?${params}`);
}
