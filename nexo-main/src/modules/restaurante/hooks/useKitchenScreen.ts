// nexo-main/src/modules/restaurante/hooks/useKitchenScreen.ts
import { useEffect, useState } from "react";
import { accessTokenLastsMoreThan, isTenantSuspended } from "@/services/tenant-suspended";
import { useKitchenItems } from "./useKitchenItems";
import { useKitchenSocket } from "./useKitchenSocket";

/** Polling is only active when SignalR has failed. */
const POLLING_MS = 10_000;
/** A suspension lasts: one check a minute is enough to notice that the access returned. */
const SUSPENDED_CHECK_MS = 60_000;
/** The token has to be good when the next check goes out, with room for that request. */
const TOKEN_MARGIN_MS = 10_000;

/**
 * What the kitchen screen shows and how it stays up to date. While the company is suspended the hub stays off, the 10 second
 * polling stops and one call a minute looks for the access to return, when everything resumes by itself.
 *
 * Those calls stop before the access token expires (see accessTokenLastsMoreThan): after that the person asks for the check, so
 * nothing automatic ends the session.
 */
export function useKitchenScreen(storeId: string) {
  const [suspended, setSuspended] = useState(false);
  const { connectionMode } = useKitchenSocket(storeId, { paused: suspended });

  const checksByItself = !suspended || accessTokenLastsMoreThan(SUSPENDED_CHECK_MS + TOKEN_MARGIN_MS);
  const pollingInterval = connectionMode === "polling" ? POLLING_MS : undefined;
  const { data, error, isFetching, refetch } = useKitchenItems(
    storeId,
    suspended ? SUSPENDED_CHECK_MS : pollingInterval,
    checksByItself,
  );

  // What a finished call says. During a call the query may have dropped the previous error, which is not news, and a network
  // failure says nothing about the company: neither one takes the screen out of this state.
  useEffect(() => {
    if (isFetching) return;
    if (isTenantSuspended(error)) setSuspended(true);
    else if (!error) setSuspended(false);
  }, [isFetching, error]);

  return {
    items: data ?? [],
    connectionMode,
    suspended,
    checksByItself,
    checking: isFetching,
    checkAgain: () => { void refetch(); },
  };
}
