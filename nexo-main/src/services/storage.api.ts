import { apiClient } from "@/services/api-client";

/** Files shown on public pages (branding, product images): they get a permanent public URL. */
export type StorageContext =
  | "product-image"
  | "restaurant-logo"
  | "restaurant-cover"
  | "service-portal-logo"
  | "service-portal-cover"
  | "build-portal-logo"
  | "build-portal-cover";

/** Private files (service records, site photos): only short-lived signed URLs, never a public one. */
export type PrivateStorageContext =
  | "build-daily-log"
  | "build-selection-option"
  | "build-quality"
  | "build-handover"
  | "build-assistance"
  | "service-record";

export interface StorageUploadResult {
  key: string;
  publicUrl: string;
}

export interface PrivateUploadResult {
  key: string;
  /** Signed URL, valid for about 15 minutes — store the key, never this URL. */
  url: string | null;
}

function form(file: File, context: string) {
  const data = new FormData();
  data.append("file", file);
  data.append("context", context);
  return data;
}

/**
 * Uploads a file via backend storage endpoint.
 * Never calls R2/external storage directly.
 */
export async function uploadFile(file: File, context: StorageContext): Promise<StorageUploadResult> {
  return apiClient.postForm<StorageUploadResult>("/integrations/storage/upload", form(file, context));
}

/** Uploads a private file; keep the returned key on the record and ask for fresh URLs when showing it. */
export async function uploadPrivateFile(file: File, context: PrivateStorageContext): Promise<PrivateUploadResult> {
  return apiClient.postForm<PrivateUploadResult>("/integrations/storage/upload", form(file, context));
}

/** A fresh signed URL (about 15 minutes) for one of the company's private files. */
export async function getSignedUrl(key: string): Promise<PrivateUploadResult> {
  return apiClient.get<PrivateUploadResult>(`/integrations/storage/signed-url?key=${encodeURIComponent(key)}`);
}

export async function deleteFile(key: string): Promise<void> {
  // one segment at a time: an encoded "/" (%2F) is not decoded in the path and the key would not match
  return apiClient.delete<void>(`/integrations/storage/${key.split("/").map(encodeURIComponent).join("/")}`);
}
