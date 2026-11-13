import { ApiError } from "@/services/api-client";

/**
 * What to tell the person when an upload fails. "Too large" and "too many at once" come with the server's own words (the limit, how
 * long to wait); anything else keeps the screen's message.
 */
export function uploadErrorMessage(e: unknown, fallback: string, unavailable?: string): string {
  if (e instanceof ApiError) {
    if (e.status === 404 && unavailable) return unavailable;
    if (e.status === 413 || e.status === 429) return e.message;
  }
  return fallback;
}
