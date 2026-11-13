import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  apiClient,
  beginImpersonation,
  getAccessToken,
  getRefreshToken,
  onSessionEnded,
  TOKEN_KEYS,
  type SessionEndReason,
} from "@/services/api-client";
import { ORDERS_KEY } from "./useActiveOrder";
import { KITCHEN_KEY } from "./useKitchenItems";
import { useKitchenSocket } from "./useKitchenSocket";
import { TABLES_KEY } from "./useRestauranteTables";

interface FakeOptions {
  accessTokenFactory: () => string | Promise<string>;
}

/** SignalR takes either form. */
type FakePolicy = number[] | { nextRetryDelayInMilliseconds: (context: { previousRetryCount: number }) => number | null };

class FakeConnection {
  closeHandlers: Array<(error?: Error) => void> = [];
  reconnectingHandlers: Array<() => void> = [];
  reconnectedHandlers: Array<() => Promise<void> | void> = [];
  start = vi.fn(async () => undefined);
  stop = vi.fn(async () => undefined);
  invoke = vi.fn(async () => undefined);
  on = vi.fn();
  onclose = (handler: (error?: Error) => void) => this.closeHandlers.push(handler);
  onreconnecting = (handler: () => void) => this.reconnectingHandlers.push(handler);
  onreconnected = (handler: () => Promise<void> | void) => this.reconnectedHandlers.push(handler);
  close = () => this.closeHandlers.forEach(handler => handler());
}

const fake = vi.hoisted(() => ({
  options: null as unknown,
  policy: null as unknown,
  connection: null as unknown,
  nextConnection: null as (() => unknown) | null,
}));

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl(_url: string, options: unknown) {
      fake.options = options;
      return this;
    }
    withAutomaticReconnect(policy: unknown) {
      fake.policy = policy;
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      fake.connection = fake.nextConnection?.();
      return fake.connection;
    }
  }
  return { HubConnectionBuilder, HttpTransportType: { WebSockets: 1, LongPolling: 4 }, LogLevel: { Warning: 3, Error: 4 } };
});

const API = "http://localhost:5000/api";
const STORE = "11111111-1111-1111-1111-111111111111";

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });

/** The token of the app, with only the expiry the client reads. The signature is the server's business. */
const tokenExpiringIn = (seconds: number) => {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + seconds }))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `header.${payload}.signature`;
};

const signIn = (accessToken: string) => {
  localStorage.setItem(TOKEN_KEYS.access, accessToken);
  localStorage.setItem(TOKEN_KEYS.refresh, "refresh-1");
};

describe("useKitchenSocket and the session", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let ended: SessionEndReason[];
  let unsubscribe: () => void;
  let client: QueryClient;
  let connection: FakeConnection;

  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const tokenOfTheConnection = () => Promise.resolve((fake.options as FakeOptions).accessTokenFactory());
  const delayAfter = (previousRetryCount: number) => {
    const policy = fake.policy as FakePolicy;
    return Array.isArray(policy) ? policy[previousRetryCount] ?? null : policy.nextRetryDelayInMilliseconds({ previousRetryCount });
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    ended = [];
    unsubscribe = onSessionEnded(reason => ended.push(reason));
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    connection = new FakeConnection();
    fake.nextConnection = () => connection;
  });

  afterEach(() => {
    unsubscribe();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  describe("the token of the connection", () => {
    it("renews a token about to expire before connecting: the server closes the connection when the token expires", async () => {
      signIn(tokenExpiringIn(5));
      const renewed = tokenExpiringIn(900);
      fetchMock.mockResolvedValueOnce(json({ accessToken: renewed, refreshToken: "refresh-2" }));
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(renewed);

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(fetchMock.mock.calls[0][0]).toBe(`${API}/auth/refresh`);
      expect(getAccessToken()).toBe(renewed);
      expect(ended).toEqual([]);
    });

    it("renews an expired token too, which is the normal case of a kitchen screen that makes no other request", async () => {
      signIn(tokenExpiringIn(-120));
      const renewed = tokenExpiringIn(900);
      fetchMock.mockResolvedValueOnce(json({ accessToken: renewed, refreshToken: "refresh-2" }));
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(renewed);
    });

    it("uses a token with time left as it is, with no request", async () => {
      const token = tokenExpiringIn(600);
      signIn(token);
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(token);

      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("several reconnects at the same time make one renewal", async () => {
      signIn(tokenExpiringIn(-120));
      const renewed = tokenExpiringIn(900);
      fetchMock.mockResolvedValueOnce(json({ accessToken: renewed, refreshToken: "refresh-2" }));
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      const tokens = await Promise.all([tokenOfTheConnection(), tokenOfTheConnection()]);

      expect(tokens).toEqual([renewed, renewed]);
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("a refused renewal ends the session once and leaves nothing to retry", async () => {
      signIn(tokenExpiringIn(-120));
      fetchMock.mockResolvedValueOnce(json({ error: "Sessão encerrada", code: "refresh_invalid" }, 401));
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe("");

      expect(ended).toEqual(["session_ended"]);
      expect(getAccessToken()).toBeNull();
      expect(delayAfter(0)).toBeNull();
    });

    it("a network failure during the renewal does not end the session: the next attempt tries again", async () => {
      const expired = tokenExpiringIn(-120);
      signIn(expired);
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));
      vi.spyOn(console, "error").mockImplementation(() => undefined);
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(expired);

      expect(ended).toEqual([]);
      expect(getRefreshToken()).toBe("refresh-1");
      expect(delayAfter(0)).toBe(0);
    });

    it.each([
      ["a 429", () => json({ code: "rate_limited" }, 429, { "Retry-After": "600" })],
      ["a 503", () => json({ code: "session_renewal_unavailable" }, 503, { "Retry-After": "15" })],
      ["a 502 of the edge", () => new Response("Bad gateway", { status: 502 })],
    ])("%s during the renewal does not end the session, and the attempts that follow do not ask again during the wait", async (_, answer) => {
      const expired = tokenExpiringIn(-120);
      signIn(expired);
      fetchMock.mockResolvedValueOnce(answer());
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(expired);
      await expect(tokenOfTheConnection()).resolves.toBe(expired);

      expect(ended).toEqual([]);
      expect(getRefreshToken()).toBe("refresh-1");
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(delayAfter(0)).toBe(0);
    });

    it("a platform access tab never renews: its token is sent as it is and the server decides", async () => {
      const token = tokenExpiringIn(-120);
      beginImpersonation(token, JSON.stringify({ type: "tenant", name: "Ana" }), { tenant: "Padaria", user: "Ana", expiresAt: "2026-11-05T12:30:00Z" });
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe(token);

      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("a token the client cannot read is sent as it is: the server decides", async () => {
      signIn("not-a-jwt");
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      await expect(tokenOfTheConnection()).resolves.toBe("not-a-jwt");

      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe("how many times it tries", () => {
    it("reconnects a limited number of times and stops", () => {
      signIn(tokenExpiringIn(600));
      renderHook(() => useKitchenSocket(STORE), { wrapper });

      const delays = [0, 1, 2, 3, 4].map(delayAfter);

      expect(delays).toEqual([0, 2000, 5000, 10_000, null]);
    });

    it("with the renewal over its limit the first attempts end in polling, with one renewal and the session intact", async () => {
      vi.useFakeTimers();
      const expired = tokenExpiringIn(-120);
      signIn(expired);
      fetchMock.mockResolvedValue(json({ code: "rate_limited" }, 429, { "Retry-After": "600" }));
      connection.start.mockImplementation(async () => {
        await (fake.options as FakeOptions).accessTokenFactory();
        throw new Error("401");
      });
      vi.spyOn(console, "warn").mockImplementation(() => undefined);
      const { result } = renderHook(() => useKitchenSocket(STORE), { wrapper });

      await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

      expect(connection.start).toHaveBeenCalledTimes(4);
      expect(result.current.connectionMode).toBe("polling");
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(ended).toEqual([]);
      expect(getAccessToken()).toBe(expired);
    });

    it("stops the first connection attempts as soon as the session is over", async () => {
      vi.useFakeTimers();
      signIn(tokenExpiringIn(600));
      connection.start.mockRejectedValue(new Error("401"));
      vi.spyOn(console, "warn").mockImplementation(() => undefined);
      renderHook(() => useKitchenSocket(STORE), { wrapper });
      await vi.advanceTimersByTimeAsync(0);
      expect(connection.start).toHaveBeenCalledTimes(1);

      localStorage.removeItem(TOKEN_KEYS.access);
      await vi.advanceTimersByTimeAsync(60_000);

      expect(connection.start).toHaveBeenCalledTimes(1);
    });
  });

  describe("when the server closes for good", () => {
    it("a revoked session finds out at once: the next call answers 401 and the person leaves, with no new connection", async () => {
      signIn(tokenExpiringIn(600));
      let revoked = false;
      fetchMock.mockImplementation(async () => (revoked
        ? json({ error: "Sua sessão foi encerrada. Entre de novo.", code: "session_revoked" }, 401)
        : json([])));
      renderHook(() => {
        // what the kitchen and the floor pages have on screen
        const kitchen = useQuery({ queryKey: KITCHEN_KEY(STORE), queryFn: () => apiClient.get("/restaurante/kitchen") });
        useKitchenSocket(STORE);
        return kitchen;
      }, { wrapper });
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
      revoked = true;

      await act(async () => connection.close());

      await waitFor(() => expect(ended).toEqual(["session_revoked"]));
      expect(getAccessToken()).toBeNull();
      expect(connection.start).toHaveBeenCalledTimes(1);
    });

    it("with the session still valid it falls back to polling and asks for the data at once", async () => {
      signIn(tokenExpiringIn(600));
      const { result } = renderHook(() => useKitchenSocket(STORE), { wrapper });
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      const invalidate = vi.spyOn(client, "invalidateQueries");

      await act(async () => connection.close());

      expect(result.current.connectionMode).toBe("polling");
      expect(invalidate).toHaveBeenCalled();
    });

    it("with the session already over it starts nothing: the app is leaving the page", async () => {
      signIn(tokenExpiringIn(600));
      const { result } = renderHook(() => useKitchenSocket(STORE), { wrapper });
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      const invalidate = vi.spyOn(client, "invalidateQueries");
      localStorage.removeItem(TOKEN_KEYS.access);

      await act(async () => connection.close());

      expect(result.current.connectionMode).toBe("realtime");
      expect(invalidate).not.toHaveBeenCalled();
    });
  });

  describe("while paused: the company has no access, so every call would be refused", () => {
    it("does not connect", async () => {
      signIn(tokenExpiringIn(600));
      renderHook(() => useKitchenSocket(STORE, { paused: true }), { wrapper });

      await act(async () => undefined);

      expect(connection.start).not.toHaveBeenCalled();
    });

    it("stops the connection and the polling timer, and asks for nothing", async () => {
      vi.useFakeTimers();
      signIn(tokenExpiringIn(600));
      const { rerender } = renderHook(({ paused }) => useKitchenSocket(STORE, { paused }), { wrapper, initialProps: { paused: false } });
      await vi.advanceTimersByTimeAsync(0);
      await act(async () => connection.close());
      const invalidate = vi.spyOn(client, "invalidateQueries");

      rerender({ paused: true });
      await vi.advanceTimersByTimeAsync(60_000);

      expect(connection.stop).toHaveBeenCalled();
      expect(invalidate).not.toHaveBeenCalled();
    });

    it("connects again with a new connection when it resumes", async () => {
      signIn(tokenExpiringIn(600));
      const { result, rerender } = renderHook(({ paused }) => useKitchenSocket(STORE, { paused }), { wrapper, initialProps: { paused: true } });
      expect(connection.start).not.toHaveBeenCalled();

      rerender({ paused: false });

      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      expect(connection.start).toHaveBeenCalledTimes(1);
      expect(result.current.connectionMode).toBe("realtime");
    });
  });

  describe("events from the kitchen hub", () => {
    // O que a cozinha e o salão têm na tela: os itens da cozinha, as mesas e as comandas da loja.
    const screens = () => {
      const fetches = { kitchen: vi.fn(), tables: vi.fn(), orders: vi.fn() };
      fetches.kitchen.mockResolvedValue([
        { itemId: "it-7", tableNumber: "4", productName: "Moqueca" },
      ]);
      fetches.tables.mockResolvedValue([]);
      fetches.orders.mockResolvedValue([]);
      return fetches;
    };
    const handlerOf = (event: string) =>
      connection.on.mock.calls.find(([name]) => name === event)?.[1] as (...args: unknown[]) => void;

    function renderScreens(fetches: ReturnType<typeof screens>, onItemReady?: (table: string | null, product: string) => void) {
      return renderHook(() => {
        useQuery({ queryKey: KITCHEN_KEY(STORE), queryFn: fetches.kitchen });
        useQuery({ queryKey: TABLES_KEY(STORE), queryFn: fetches.tables });
        useQuery({ queryKey: ORDERS_KEY(STORE), queryFn: fetches.orders });
        useKitchenSocket(STORE, { onItemReady });
      }, { wrapper });
    }

    it.each(["NewItemAdded", "OrderStatusChanged", "TableStatusChanged"])(
      "%s makes the kitchen, the tables and the orders ask for their data again",
      async (event) => {
        signIn(tokenExpiringIn(600));
        const fetches = screens();
        renderScreens(fetches);
        await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
        await waitFor(() => expect(fetches.orders).toHaveBeenCalledTimes(1));

        await act(async () => handlerOf(event)());

        await waitFor(() => expect(fetches.kitchen).toHaveBeenCalledTimes(2));
        await waitFor(() => expect(fetches.tables).toHaveBeenCalledTimes(2));
        await waitFor(() => expect(fetches.orders).toHaveBeenCalledTimes(2));
      },
    );

    it("an item marked ready is announced with the table and the dish on screen, then everything refreshes", async () => {
      signIn(tokenExpiringIn(600));
      const fetches = screens();
      const onItemReady = vi.fn();
      renderScreens(fetches, onItemReady);
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      await waitFor(() => expect(client.getQueryData(KITCHEN_KEY(STORE))).toHaveLength(1));

      await act(async () => handlerOf("OrderItemStatusChanged")("cmd-1", "it-7", "Ready"));

      expect(onItemReady).toHaveBeenCalledWith("4", "Moqueca");
      await waitFor(() => expect(fetches.orders).toHaveBeenCalledTimes(2));
      expect(fetches.kitchen).toHaveBeenCalledTimes(2);
    });

    it("an item that only changed to preparing is not announced, but the screens still refresh", async () => {
      signIn(tokenExpiringIn(600));
      const fetches = screens();
      const onItemReady = vi.fn();
      renderScreens(fetches, onItemReady);
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      await waitFor(() => expect(fetches.tables).toHaveBeenCalledTimes(1));

      await act(async () => handlerOf("OrderItemStatusChanged")("cmd-1", "it-7", "Preparing"));

      expect(onItemReady).not.toHaveBeenCalled();
      await waitFor(() => expect(fetches.tables).toHaveBeenCalledTimes(2));
    });

    it("leaving the screen closes the connection", async () => {
      signIn(tokenExpiringIn(600));
      const { unmount } = renderHook(() => useKitchenSocket(STORE), { wrapper });
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));

      unmount();

      expect(connection.stop).toHaveBeenCalledTimes(1);
    });
  });

  describe("reconnecting at the token expiry", () => {
    it("joins the store again and refreshes the data, without leaving realtime", async () => {
      signIn(tokenExpiringIn(600));
      const { result } = renderHook(() => useKitchenSocket(STORE), { wrapper });
      await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE));
      const invalidate = vi.spyOn(client, "invalidateQueries");
      connection.invoke.mockClear();

      await act(async () => {
        connection.reconnectingHandlers.forEach(handler => handler());
        await Promise.all(connection.reconnectedHandlers.map(handler => handler()));
      });

      expect(connection.invoke).toHaveBeenCalledWith("JoinStore", STORE);
      expect(invalidate).toHaveBeenCalled();
      expect(result.current.connectionMode).toBe("realtime");
    });
  });
});
