/**
 * PDF download helper.
 *
 * Uses the same auth token mechanism as api-client.ts: reads the in-memory
 * token first via getAccessToken(), which falls back to localStorage
 * "nexo:access_token" on a hard reload.
 */

import { getAccessToken } from "./api-client";

const BASE_URL =
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5000/api";

export function downloadPdf(path: string, filename: string): Promise<void> {
  return downloadFile(path, filename, "Falha ao gerar PDF");
}

/** Fetches an authenticated file (PDF, CSV…) and hands it to the browser as a download. */
export async function downloadFile(path: string, filename: string, failure: string): Promise<void> {
  const token = getAccessToken();

  const headers: Record<string, string> = {};
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const response = await fetch(`${BASE_URL}${path}`, { headers });

  if (!response.ok) {
    let reason = String(response.status);
    try {
      const err = await response.json();
      reason = err?.error ?? reason;
    } catch {
      // empty or non-JSON body
    }
    throw new Error(`${failure}: ${reason}`);
  }

  const blob = await response.blob();
  const href = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = href;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(href);
}
