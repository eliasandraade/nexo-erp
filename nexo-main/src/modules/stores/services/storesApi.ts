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
