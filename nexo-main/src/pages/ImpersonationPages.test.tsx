import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ImpersonationEndedPage from "./ImpersonationEndedPage";
import ImpersonatePage from "./ImpersonatePage";
import * as handoff from "@/services/impersonation-handoff";
import * as apiClient from "@/services/api-client";

const setSessionFromVerify = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ setSessionFromVerify }) }));
vi.mock("@/modules/auth/services/authService", () => ({
  startImpersonationSession: vi.fn(() => ({ type: "tenant", activeModules: [], role: "diretoria" })),
}));
vi.mock("@/modules/workspace/resolvePostLogin", () => ({ resolvePostLogin: () => "/dashboard" }));
vi.mock("@/modules/workspace/persistence", () => ({ readLastWorkspace: () => null }));

describe("ImpersonationEndedPage", () => {
  afterEach(cleanup);

  it("explains that the access ended and goes back to the platform without reusing the tenant session", () => {
    const leave = vi.spyOn(apiClient, "leaveImpersonation").mockImplementation(() => {});
    const assign = vi.fn();
    vi.stubGlobal("location", { ...window.location, assign });
    render(<MemoryRouter initialEntries={["/acesso-encerrado"]}><ImpersonationEndedPage /></MemoryRouter>);

    expect(screen.getByRole("heading", { name: "O acesso à empresa terminou" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Voltar à plataforma" }));
    expect(leave).toHaveBeenCalled();
    expect(assign).toHaveBeenCalledWith("/platform");
    vi.unstubAllGlobals();
  });

  it("says the administrator left when that is the reason, and offers to close the tab", () => {
    const close = vi.spyOn(window, "close").mockImplementation(() => {});
    render(<MemoryRouter initialEntries={["/acesso-encerrado?motivo=saiu"]}><ImpersonationEndedPage /></MemoryRouter>);

    expect(screen.getByRole("heading", { name: "Acesso encerrado" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Fechar aba" }));
    expect(close).toHaveBeenCalled();
  });
});

describe("ImpersonatePage", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(cleanup);

  function show(search: string) {
    window.history.replaceState(null, "", `/impersonate${search}`);
    return render(
      <MemoryRouter initialEntries={[`/impersonate${search}`]}>
        <Routes>
          <Route path="/impersonate" element={<ImpersonatePage />} />
          <Route path="/dashboard" element={<p>painel</p>} />
        </Routes>
      </MemoryRouter>
    );
  }

  it("refuses a link without its nonce", async () => {
    show("");
    expect(await screen.findByText(/link de acesso não é válido/)).toBeInTheDocument();
  });

  it("enters the tenant with the token handed to this tab only", async () => {
    vi.spyOn(handoff, "requestImpersonation").mockResolvedValue({ accessToken: "imp-token" } as never);
    show("?k=nonce-1");
    expect(await screen.findByText("painel")).toBeInTheDocument();
    expect(handoff.requestImpersonation).toHaveBeenCalledWith("nonce-1", expect.anything());
    expect(setSessionFromVerify).toHaveBeenCalled();
  });

  it("explains when the access never arrives", async () => {
    vi.spyOn(handoff, "requestImpersonation").mockRejectedValue(new Error("timeout"));
    show("?k=nonce-2");
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("O acesso não chegou a esta aba"));
    expect(screen.getByRole("link", { name: "Voltar à plataforma" })).toHaveAttribute("href", "/platform");
  });
});
