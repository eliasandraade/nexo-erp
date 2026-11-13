import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getAccessToken, getRefreshToken, onSessionEnded, TOKEN_KEYS, type SessionEndReason } from "@/services/api-client";
import KitchenPage from "./KitchenPage";

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

const fake = vi.hoisted(() => ({ connections: [] as unknown[], next: null as (() => unknown) | null }));

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() {
      const connection = fake.next?.();
      fake.connections.push(connection);
      return connection;
    }
  }
  return { HubConnectionBuilder, HttpTransportType: { WebSockets: 1, LongPolling: 4 }, LogLevel: { Warning: 3, Error: 4 } };
});

vi.mock("@/modules/auth/context/AuthContext", () => ({
  useAuth: () => ({ session: { storeId: "store-1", type: "tenant", role: "cozinha", modules: ["restaurante"] } }),
}));
vi.mock("@/components/shared/UserDropdown", () => ({ UserDropdown: () => <div>menu do usuário</div> }));
vi.mock("../components/RestauranteBreadcrumb", () => ({ RestauranteBreadcrumb: () => <div>Orken Menu</div> }));

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const SUSPENDED = { error: "Tenant account is suspended.", code: "tenant_suspended" };

const order = {
  id: "o1", orderNumber: 7, tableNumber: "3", orderType: "Table",
  items: [{ id: "i1", productName: "Moqueca", quantity: 1, notes: null, modifiers: [], status: "Pending", sentToKitchenAt: null }],
};

/** The app's token, with only the expiry the client reads. The signature is the server's business. */
const tokenExpiringIn = (seconds: number) => {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + seconds }))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `header.${payload}.signature`;
};

const signIn = (accessToken: string) => {
  localStorage.setItem(TOKEN_KEYS.access, accessToken);
  localStorage.setItem(TOKEN_KEYS.refresh, "refresh-1");
};

describe("the kitchen screen when the company is suspended", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let ended: SessionEndReason[];
  let unsubscribe: () => void;
  let connection: FakeConnection;
  let answer: () => Response;

  const kitchenCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).includes("/restaurante/orders")).length;
  const refreshCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).includes("/auth/refresh")).length;
  // the extra step lets what a finished call scheduled (the query's notifications) run too
  const advance = (ms: number) => act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
    await vi.advanceTimersByTimeAsync(1);
  });

  const open = () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(<QueryClientProvider client={client}><KitchenPage /></QueryClientProvider>);
  };

  /** The connection is alive and the order is on the board, and then the server closes it for good. */
  const suspendWhileOpen = async () => {
    open();
    await advance(0);
    expect(screen.getByText(/Moqueca/)).toBeInTheDocument();
    answer = () => json(SUSPENDED, 403);
    await act(async () => connection.close());
    await advance(0);
  };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
    localStorage.clear();
    sessionStorage.clear();
    answer = () => json([order]);
    fetchMock = vi.fn(async () => answer());
    vi.stubGlobal("fetch", fetchMock);
    ended = [];
    unsubscribe = onSessionEnded(reason => ended.push(reason));
    connection = new FakeConnection();
    fake.connections = [];
    fake.next = () => connection;
    Object.defineProperty(document, "fullscreenEnabled", { value: false, configurable: true });
    signIn(tokenExpiringIn(900));
  });

  afterEach(() => {
    cleanup();
    unsubscribe();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("shows nothing about it while the company is active", async () => {
    open();
    await advance(0);

    expect(screen.getByText(/Moqueca/)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("button", { name: "Iniciar" })).toBeEnabled();
  });

  it("tells the kitchen staff, in plain words, and keeps the session", async () => {
    await suspendWhileOpen();

    const notice = screen.getByRole("alert");
    expect(notice.textContent).toMatch(/acesso da empresa.*suspenso/i);
    expect(notice.textContent).toMatch(/novos pedidos não estão chegando/i);
    expect(notice.textContent).toMatch(/avise o responsável/i);
    expect(notice.textContent).not.toMatch(/Tenant account/);
    expect(ended).toEqual([]);
    expect(getAccessToken()).not.toBeNull();
    expect(getRefreshToken()).toBe("refresh-1");
    expect(refreshCalls()).toBe(0);
    expect(screen.getByText("menu do usuário")).toBeInTheDocument();
  });

  it("keeps the last orders on screen and stops offering what would be refused", async () => {
    await suspendWhileOpen();

    expect(screen.getByText(/Moqueca/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Iniciar" })).toBeDisabled();
    expect(screen.queryByText(/Tempo real|Atualizando/)).toBeNull();
  });

  it("stops the 10 second polling and checks once a minute", async () => {
    await suspendWhileOpen();
    const callsAfterTheRefusal = kitchenCalls();

    await advance(50_000);
    expect(kitchenCalls()).toBe(callsAfterTheRefusal);

    await advance(10_000);
    expect(kitchenCalls()).toBe(callsAfterTheRefusal + 1);

    await advance(60_000);
    expect(kitchenCalls()).toBe(callsAfterTheRefusal + 2);
  });

  it("does not try to connect to the hub while suspended", async () => {
    await suspendWhileOpen();
    expect(connection.stop).toHaveBeenCalled();
    const connectionsSoFar = fake.connections.length;

    await advance(180_000);

    expect(fake.connections.length).toBe(connectionsSoFar);
  });

  it("comes back by itself when access returns: notice gone, hub connected again, buttons back", async () => {
    await suspendWhileOpen();
    const second = new FakeConnection();
    fake.next = () => second;
    answer = () => json([order]);

    await advance(60_000);

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("button", { name: "Iniciar" })).toBeEnabled();
    expect(second.start).toHaveBeenCalledTimes(1);
    expect(second.invoke).toHaveBeenCalledWith("JoinStore", "store-1");
    expect(screen.getByText("Tempo real")).toBeInTheDocument();
    expect(ended).toEqual([]);
  });

  it("a network failure during a check says nothing about the company: the notice stays and the next check still goes out", async () => {
    await suspendWhileOpen();
    const connectionsSoFar = fake.connections.length;
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

    await advance(60_000);

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(fake.connections.length).toBe(connectionsSoFar);
    expect(ended).toEqual([]);

    const callsSoFar = kitchenCalls();
    await advance(60_000);
    expect(kitchenCalls()).toBe(callsSoFar + 1);
  });

  it("does not mistake another 403 for a suspension: no notice, and the polling goes on", async () => {
    open();
    await advance(0);
    answer = () => json({ error: "Sem permissão." }, 403);
    await act(async () => connection.close());
    await advance(0);
    const calls = kitchenCalls();

    expect(screen.queryByRole("alert")).toBeNull();
    await advance(10_000);
    expect(kitchenCalls()).toBeGreaterThan(calls);
  });

  it("opened while already suspended, with nothing to show yet, stays stable across the checks", async () => {
    answer = () => json(SUSPENDED, 403);
    open();
    await advance(0);
    expect(screen.getByRole("alert")).toBeInTheDocument();
    const connectionsSoFar = fake.connections.length;

    await advance(60_000);
    expect(screen.getByRole("alert")).toBeInTheDocument();
    await advance(120_000);

    expect(fake.connections.length).toBe(connectionsSoFar);
    expect(ended).toEqual([]);
  });

  describe("when the token runs out during the suspension", () => {
    // The server refuses to renew a token of a suspended company, and the client drops the session when a renewal is refused. So
    // the checks stop before the token expires: nothing automatic ends the session.
    beforeEach(() => signIn(tokenExpiringIn(200)));

    it("stops checking before the token expires, with no renewal attempt and the session intact", async () => {
      await suspendWhileOpen();

      await advance(30 * 60_000);

      const lastCheckAt = kitchenCalls();
      await advance(30 * 60_000);
      expect(kitchenCalls()).toBe(lastCheckAt);
      expect(refreshCalls()).toBe(0);
      expect(ended).toEqual([]);
      expect(getRefreshToken()).toBe("refresh-1");
      expect(getAccessToken()).not.toBeNull();
      expect(screen.getByRole("alert").textContent).toMatch(/verificar de novo/i);
    });

    it("a check the person asks for works, and the screen resumes when access is back", async () => {
      await suspendWhileOpen();
      await advance(30 * 60_000);
      const second = new FakeConnection();
      fake.next = () => second;
      const renewed = tokenExpiringIn(900);
      fetchMock.mockImplementation(async (url: unknown) => (String(url).includes("/auth/refresh")
        ? json({ accessToken: renewed, refreshToken: "refresh-2" })
        : getAccessToken() === renewed ? json([order]) : json({ error: "Token expirado." }, 401)));

      fireEvent.click(screen.getByRole("button", { name: /verificar de novo/i }));
      // three calls in a row (the refusal of the old token, the renewal, the call again), each with a body to read
      await vi.waitFor(async () => {
        await advance(1);
        expect(screen.queryByRole("alert")).toBeNull();
      });

      expect(second.start).toHaveBeenCalledTimes(1);
      expect(ended).toEqual([]);
    });
  });
});
