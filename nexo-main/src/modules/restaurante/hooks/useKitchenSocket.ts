// nexo-main/src/modules/restaurante/hooks/useKitchenSocket.ts
import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getAccessToken } from "@/services/api-client";
import { KITCHEN_KEY } from "./useKitchenItems";
import { TABLES_KEY } from "./useRestauranteTables";
import { ORDERS_KEY } from "./useActiveOrder";
import type { ConnectionMode, KitchenItem } from "../types";

const RECONNECT_DELAYS = [0, 2000, 5000];
const POLLING_INTERVAL = 10_000;
// VITE_API_BASE_URL ends with "/api" (e.g. "https://api.example.com/api").
// SignalR hubs live at the root, so strip the "/api" suffix.
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api").replace(/\/api$/, "");

interface UseKitchenSocketOptions {
  onItemReady?: (tableNumber: string | null, productName: string) => void;
}

/**
 * Manages a SignalR connection to /hubs/restaurant for real-time kitchen updates.
 *
 * Token is read via getAccessToken() inside accessTokenFactory — NOT captured from
 * component props. This prevents the useEffect from re-running when the token
 * refreshes mid-negotiation, which would cause "connection stopped during negotiation".
 *
 * Guard: only connects when storeId is non-empty (user is authenticated + store resolved).
 */
export function useKitchenSocket(
  storeId: string,
  options: UseKitchenSocketOptions = {}
) {
  const { onItemReady } = options;
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
    // We do NOT use the token as a dependency — getAccessToken() is called
    // by SignalR's accessTokenFactory on each request, so token refreshes
    // are transparent without causing the effect to re-run.
    if (!storeId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/restaurant`, {
        // Always reads the latest token at request time — survives refreshes.
        accessTokenFactory: () => getAccessToken() ?? "",
      })
      .withAutomaticReconnect(RECONNECT_DELAYS)
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

    connection.onreconnected(() => {
      retryCountRef.current = 0;
      stopPolling();
      setMode("realtime");
      invalidateAll();
    });

    connection.onclose(() => {
      startPolling();
    });

    connection
      .start()
      .then(() => {
        retryCountRef.current = 0;
        setMode("realtime");
        stopPolling();
        return connection.invoke("JoinStore", storeId);
      })
      .catch(() => {
        startPolling();
      });

    return () => {
      stopPolling();
      connection.stop();
    };
  }, [storeId, invalidateAll, startPolling, stopPolling]);

  return { connectionMode: mode };
}
