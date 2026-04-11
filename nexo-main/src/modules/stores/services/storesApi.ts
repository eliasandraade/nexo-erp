import { apiClient } from "@/services/api-client";
import type { StoreDto } from "../types";

export async function fetchMyStores(): Promise<StoreDto[]> {
  return apiClient.get<StoreDto[]>("/stores");
}
