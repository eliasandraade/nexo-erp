import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getAccessToken, getRefreshToken, setTokens } from "./api-client";
import { createCheckout, getCheckoutAvailability } from "./billing.api";

const API = "http://localhost:5000/api";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("billing.api", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    setTokens("tenant-access", "tenant-refresh");
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => vi.unstubAllGlobals());

  it("lê no servidor se a contratação está aberta", async () => {
    fetchMock.mockResolvedValueOnce(json({ newCheckoutsEnabled: false }));

    await expect(getCheckoutAvailability()).resolves.toEqual({ newCheckoutsEnabled: false });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${API}/billing/checkout-availability`);
    expect(init.method).toBe("GET");
  });

  it("o 403 checkout_disabled chega à tela com o código e a mensagem do servidor, e a sessão fica", async () => {
    const mensagem = "A contratação está indisponível. Fale com o suporte.";
    fetchMock.mockResolvedValueOnce(json({ error: mensagem, code: "checkout_disabled", requestId: "req-1" }, 403));

    const error = await createCheckout("build", "monthly").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 403, code: "checkout_disabled", message: mensagem, requestId: "req-1" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(getAccessToken()).toBe("tenant-access");
    expect(getRefreshToken()).toBe("tenant-refresh");
  });
});
