import { apiClient } from "@/services/api-client";

export type StorageContext =
  | "product-image"
  | "restaurant-logo"
  | "restaurant-cover"
  | "build-daily-log";

export interface StorageUploadResult {
  key: string;
  publicUrl: string;
}

/**
 * Uploads a file via backend storage endpoint.
 * Never calls R2/external storage directly.
 */
export async function uploadFile(
  file: File,
  context: StorageContext
): Promise<StorageUploadResult> {
  const form = new FormData();
  form.append("file", file);
  form.append("context", context);
  return apiClient.postForm<StorageUploadResult>("/integrations/storage/upload", form);
}

export async function deleteFile(key: string): Promise<void> {
  return apiClient.delete<void>(`/integrations/storage/${encodeURIComponent(key)}`);
}
