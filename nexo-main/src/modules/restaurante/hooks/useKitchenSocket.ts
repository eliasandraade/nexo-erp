// nexo-main/src/modules/restaurante/hooks/useKitchenSocket.ts
import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { KITCHEN_KEY } from "./useKitchenItems";
import { TABLES_KEY } from "./useRestauranteTables";
import { ORDERS_KEY } from "./useActiveOrder";
import type { ConnectionMode } from "../types";

const RECONNECT_DELAYS = [0, 2000, 5000];
const POLLING_INTERVAL = 10_000;
const API_BASE = import.meta.env.VITE_API_URL ?? "http://localhost:5000";

export function useKitchenSocket(storeId: string, token: string | undefined) {
  const qc              = useQueryClient();
  const connectionRef   = useRef<signalR.HubConnection | null>(null);
  const retryCountRef   = useRef(0);
  const [mode, setMode] = useState<ConnectionMode>("realtime");
  const pollingTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

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
    if (!token || !storeId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/restaurant`, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .build();

    connectionRef.current = connection;

    connection.on("NewItemAdded", () => invalidateAll());
    connection.on("OrderItemStatusChanged", () => invalidateAll());
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
  }, [token, storeId, invalidateAll, startPolling, stopPolling]);

  return { connectionMode: mode };
}
