import { useQueries } from "@tanstack/react-query";
import { getAvailability, type PublicAvailabilitySlot } from "../api/booking.api";

export interface AggregatedAvailability {
  /** Union of free slots (ascending), deduped across professionals. */
  slots: PublicAvailabilitySlot[];
  /** First professional free at a given slot start — used to book a "no preference" choice. */
  ownerOf: (startsAt: string) => string | null;
  isLoading: boolean;
  isError: boolean;
}

/**
 * Availability for the chosen service across one or many professionals.
 *
 *  - specific professional → pass `[proId]`; slots = that professional's, ownerOf = proId.
 *  - "no preference"       → pass every active professional id; slots = the UNION, and ownerOf
 *    resolves the first professional free at each slot so the booking can pick one automatically.
 *
 * One real availability call per professional (the backend endpoint is per professional); no
 * fabricated slots — only the union of what each professional genuinely has open.
 */
export function usePortalAvailability(
  slug: string,
  catalogItemId: string | null,
  professionalIds: string[],
): AggregatedAvailability {
  const results = useQueries({
    queries: professionalIds.map((pid) => ({
      queryKey: ["svc-portal-availability", slug, catalogItemId, pid] as const,
      queryFn: () => getAvailability(slug, catalogItemId!, pid),
      enabled: Boolean(slug && catalogItemId && pid),
      staleTime: 0,
      retry: false,
    })),
  });

  const isLoading = professionalIds.length > 0 && results.some((r) => r.isLoading);
  const isError = professionalIds.length > 0 && results.every((r) => r.isError);

  // startsAt → { slot, ownersInArrivalOrder }
  const byStart = new Map<string, { slot: PublicAvailabilitySlot; owners: string[] }>();
  results.forEach((r, i) => {
    const pid = professionalIds[i];
    for (const slot of r.data?.slots ?? []) {
      const entry = byStart.get(slot.startsAt);
      if (entry) entry.owners.push(pid);
      else byStart.set(slot.startsAt, { slot, owners: [pid] });
    }
  });

  const merged = [...byStart.values()].sort((a, b) => a.slot.startsAt.localeCompare(b.slot.startsAt));

  return {
    slots: merged.map((m) => m.slot),
    ownerOf: (startsAt) => byStart.get(startsAt)?.owners[0] ?? null,
    isLoading,
    isError,
  };
}
