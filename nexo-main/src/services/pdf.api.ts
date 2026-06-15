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

export async function downloadPdf(path: string, filename: string): Promise<void> {
  const token = getAccessToken();

  const headers: Record<string, string> = {};
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const response = await fetch(`${BASE_URL}${path}`, { headers });

  if (!response.ok) {
    throw new Error(`Falha ao gerar PDF: ${response.status}`);
  }

  const blob = await response.blob();
  const href = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = href;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(href);
}
