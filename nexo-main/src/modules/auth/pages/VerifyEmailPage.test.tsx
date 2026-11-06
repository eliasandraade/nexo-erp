import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AuthProvider } from "../context/AuthContext";
import VerifyEmailPage from "./VerifyEmailPage";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const SESSION = {
  userId: "u1", tenantId: "t1", name: "Ana", role: "vendedor", login: "ana", email: "ana@x.dev",
  activeModules: ["varejo"], storeIds: ["s1"], type: "tenant",
};

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <Routes>
            <Route path="/verify-email" element={<VerifyEmailPage />} />
            <Route path="*" element={<p data-testid="destino">entrou</p>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("VerifyEmailPage", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("with no token in the link it says so, and asks the server nothing", async () => {
    renderAt("/verify-email");

    expect(await screen.findByText("Link expirado ou inválido")).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("a valid link confirms, forgets the pending address and enters the system", async () => {
    localStorage.setItem("nexo:pending_email", "ana@x.dev");
    fetchMock.mockResolvedValueOnce(json({ accessToken: "a", refreshToken: "r", session: SESSION }));

    renderAt("/verify-email?token=abc");

    expect(await screen.findByText("Conta verificada!")).toBeTruthy();
    expect(localStorage.getItem("nexo:pending_email")).toBeNull();
    expect(await screen.findByTestId("destino", undefined, { timeout: 3000 })).toBeTruthy();
  });

  it.each([
    ["the server refuses the link", () => Promise.resolve(json({ error: "Link inválido ou expirado." }, 400))],
    ["the connection fails", () => Promise.reject(new TypeError("Failed to fetch"))],
  ])("%s: it leaves the spinner for the error state, never stuck verifying", async (_name, answer) => {
    fetchMock.mockImplementationOnce(answer);

    renderAt("/verify-email?token=abc");

    expect(await screen.findByText("Link expirado ou inválido")).toBeTruthy();
    expect(screen.queryByText("Verificando sua conta...")).toBeNull();
  });

  describe("asking for a new link", () => {
    const refused = () => fetchMock.mockResolvedValueOnce(json({ error: "Link inválido ou expirado." }, 400));

    async function reachResend(email: string) {
      localStorage.setItem("nexo:pending_email", email);
      refused();
      renderAt("/verify-email?token=abc");
      await screen.findByText("Link expirado ou inválido");
    }

    const resendButton = () => screen.getByRole("button", { name: /Reenviar link/ });

    it("sends it to the address on the form and remembers it", async () => {
      await reachResend("ana@x.dev");
      fetchMock.mockResolvedValueOnce(json({ message: "ok" }));

      await act(async () => { fireEvent.click(resendButton()); });

      await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
      const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit];
      expect(url).toContain("/auth/resend-verification");
      expect(JSON.parse(init.body as string)).toEqual({ email: "ana@x.dev" });
      expect(localStorage.getItem("nexo:pending_email")).toBe("ana@x.dev");
    });

    it("says when it could not be sent, and the person can try again", async () => {
      await reachResend("ana@x.dev");
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await act(async () => { fireEvent.click(resendButton()); });

      await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
      expect(resendButton()).toBeTruthy();
      expect(screen.getByText("Erro ao reenviar. Tente novamente.")).toBeTruthy();
    });
  });
});
