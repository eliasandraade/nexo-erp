// nexo-main/src/modules/restaurante/hooks/useKitchenSocket.ts
import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getFreshAccessToken, hasSession, isCookieSessionMode } from "@/services/api-client";
import { KITCHEN_KEY } from "./useKitchenItems";
import { TABLES_KEY } from "./useRestauranteTables";
import { ORDERS_KEY } from "./useActiveOrder";
import type { ConnectionMode, KitchenItem } from "../types";

const RECONNECT_DELAYS = [0, 2000, 5000, 10_000];
const POLLING_INTERVAL = 10_000;

// Delays between manual retry attempts before giving up and switching to polling.
// Total wait before fallback: 1s + 3s + 5s = 9s.
const INITIAL_RETRY_DELAYS_MS = [1_000, 3_000, 5_000];

/**
 * Custom SignalR logger that suppresses the "stopped during negotiation" message.
 *
 * When the component unmounts while start() is still in progress, the cleanup
 * calls stop(), which aborts the in-flight negotiation request. SignalR logs
 * "Failed to start the connection: Error: The connection was stopped during
 * negotiation." internally (before rejecting the Promise), producing console
 * noise. Our cancelled-flag in attemptConnect already handles this correctly
 * (no retry, no polling) — we just need to silence the log.
 */
const silentNegotiationLogger: signalR.ILogger = {
  log(level: signalR.LogLevel, message: string) {
    if (message.includes("stopped during negotiation")) return;
    if (level >= signalR.LogLevel.Error)   console.error(`[SignalR] ${message}`);
    else if (level >= signalR.LogLevel.Warning) console.warn(`[SignalR] ${message}`);
  },
};

// VITE_API_BASE_URL ends with "/api" (e.g. "https://api.example.com/api").
// SignalR hubs live at the root, so strip the "/api" suffix.
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api").replace(/\/api$/, "");

interface UseKitchenSocketOptions {
  onItemReady?: (tableNumber: string | null, productName: string) => void;
  /** No connection, no retries and no polling: the company has no access now. Turning it off connects again from scratch. */
  paused?: boolean;
}

/**
 * Manages a SignalR connection to /hubs/restaurant for real-time kitchen updates.
 *
 * Token is read via getFreshAccessToken() inside accessTokenFactory — NOT captured from
 * component props. This prevents the useEffect from re-running when the token
 * refreshes mid-negotiation, which would cause "connection stopped during negotiation".
 *
 * On initial connect failure, retries up to 3 times with backoff before switching
 * to polling mode. Polling invalidates React Query caches every 10s.
 *
 * The server closes the connection in two ways:
 *  - when the token expires, allowing a reconnect: the client comes back by itself with a renewed token, about once per token.
 *  - when the session ended (role, status or password changed, sessions revoked, company suspended), for good: no reconnect, and
 *    the data is asked for at once, so the 401 of that call takes the person out through the same path as any other request.
 * Without a session nothing is retried: it is over and the app is leaving the page.
 *
 * Guard: only connects when storeId is non-empty (user is authenticated + store resolved)
 * AND there is a session. In the cookie mode a tab that just reloaded has no access token yet: the factory below fetches the first one.
 */
export function useKitchenSocket(
  storeId: string,
  options: UseKitchenSocketOptions = {}
) {
  const { onItemReady, paused = false } = options;
  const qc               = useQueryClient();
  const connectionRef    = useRef<signalR.HubConnection | null>(null);
  const retryCountRef    = useRef(0);
  const [mode, setMode]  = useState<ConnectionMode>("realtime");
  const pollingTimerRef  = useRef<ReturnType<typeof setInterval> | null>(null);
  const onItemReadyRef   = useRef(onItemReady);
  onItemReadyRef.current = onItemReady;

  const invalidateAll = useCallback(() => {
    qc.invalidateQueries({ queryKey: KITCHEN_KEY(storeId) });
    qc.invalidateQueries({ queryKey: TABLES_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ORDERS_KEY(storeId) });
  }, [qc, storeId]);

  const startPolling = useCallback(() => {
    if (pollingTimerRef.current) return;
    setMode("polling");
    pollingTimerRef.current = setInterval(invalidateAll, POLLING_INTERVAL);
  }, [invalidateAll]);

  const stopPolling = useCallback(() => {
    if (pollingTimerRef.current) {
      clearInterval(pollingTimerRef.current);
      pollingTimerRef.current = null;
    }
  }, []);

  useEffect(() => {
    // Only connect when a store is resolved (implies authenticated).
    if (!storeId || paused) return;

    // Do not attempt connection if there is no session at hook mount time.
    if (!hasSession()) return;

    // Local cancellation flag — each effect run gets its own copy.
    // Avoids race conditions if storeId changes while a retry delay is in flight.
    let cancelled = false;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/restaurant`, {
        // Always reads the latest token at request time and renews one about to expire.
        accessTokenFactory: async () => (await getFreshAccessToken()) ?? "",
        // Allow both transports so Railway's reverse proxy has a fallback
        // if WebSocket upgrades are not available.
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
        // The client sends credentials by default. In the cookie mode only the session calls do: the hub authenticates with the token.
        withCredentials: !isCookieSessionMode(),
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
          hasSession() ? RECONNECT_DELAYS[previousRetryCount] ?? null : null,
      })
      .configureLogging(silentNegotiationLogger)
      .build();

    connectionRef.current = connection;

    connection.on("NewItemAdded", () => invalidateAll());
    connection.on("OrderItemStatusChanged", (_orderId: string, itemId: string, status: string) => {
      // Fire the onItemReady callback BEFORE invalidating so we read current cache data
      if (status === "Ready" && onItemReadyRef.current) {
        const cached = qc.getQueryData<KitchenItem[]>(KITCHEN_KEY(storeId));
        const item   = cached?.find(i => i.itemId === itemId);
        onItemReadyRef.current(item?.tableNumber ?? null, item?.productName ?? "Item");
      }
      invalidateAll();
    });
    connection.on("OrderStatusChanged", () => invalidateAll());
    connection.on("TableStatusChanged", () => invalidateAll());

    connection.onreconnecting(() => {
      retryCountRef.current += 1;
      if (retryCountRef.current >= RECONNECT_DELAYS.length) {
        startPolling();
      }
    });

    connection.onreconnected(async () => {
      retryCountRef.current = 0;
      // A reconnect is a new server connection: it is in no group until it joins the store again
      try {
        await connection.invoke("JoinStore", storeId);
      } catch (err) {
        if (cancelled) return;   // the component is gone: don't leave a poller behind
        console.warn("[SignalR] Rejoin after reconnect failed — falling back to polling:", err);
        startPolling();
        return;
      }
      stopPolling();
      setMode("realtime");
      invalidateAll();
    });

    connection.onclose(() => {
      if (cancelled || !hasSession()) return;
      startPolling();
      // Now, not at the next tick of the polling: if the server closed because the session ended, this call is what says so.
      invalidateAll();
    });

    // Attempt initial connection with manual retry before falling back to polling.
    // `cancelled` is captured in each effect's closure, so cleanup sets its own copy.
    const attemptConnect = async (attempt: number): Promise<void> => {
      if (cancelled || !hasSession()) return;

      try {
        await connection.start();
        if (cancelled) return;

        retryCountRef.current = 0;
        setMode("realtime");
        stopPolling();
        await connection.invoke("JoinStore", storeId);
      } catch (err) {
        if (cancelled || !hasSession()) return;  // cleanup already ran or the session is over — don't retry, don't poll

        const nextDelay = INITIAL_RETRY_DELAYS_MS[attempt];
        if (nextDelay !== undefined) {
          console.warn(
            `[SignalR] Connect attempt ${attempt + 1} failed, retrying in ${nextDelay}ms:`,
            err
          );
          await new Promise<void>((resolve) => setTimeout(resolve, nextDelay));
          return attemptConnect(attempt + 1);
        }

        // All retries exhausted — fall back to polling.
        console.warn("[SignalR] All connection attempts failed, switching to polling:", err);
        startPolling();
      }
    };

    attemptConnect(0);

    return () => {
      cancelled = true;
      stopPolling();
      connection.stop();
    };
  }, [storeId, paused, invalidateAll, startPolling, stopPolling]);

  return { connectionMode: mode };
}
