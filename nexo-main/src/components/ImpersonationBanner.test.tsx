import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ImpersonationBanner } from "./ImpersonationBanner";
import { beginImpersonation, getImpersonation, onSessionEnded, TOKEN_KEYS, type SessionEndReason } from "@/services/api-client";

describe("ImpersonationBanner", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let closeSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    // the administrator's own session in this browser
    localStorage.setItem(TOKEN_KEYS.access, "admin-access");
    localStorage.setItem(TOKEN_KEYS.refresh, "p.admin-refresh");
    localStorage.setItem(TOKEN_KEYS.session, "{\"type\":\"platform\"}");
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    closeSpy = vi.spyOn(window, "close").mockImplementation(() => {});
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    closeSpy.mockRestore();
  });

  const renderBanner = () => render(<MemoryRouter><ImpersonationBanner /></MemoryRouter>);

  it("does not show in a normal tab", () => {
    renderBanner();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows who the administrator acts as and until when", () => {
    const expiresAt = new Date(Date.now() + 30 * 60_000).toISOString();
    beginImpersonation("imp-access", "{}", { tenant: "Padaria Sol", user: "Ana Lima", expiresAt });
    renderBanner();
    const banner = screen.getByRole("status");
    expect(banner).toHaveTextContent("Ana Lima");
    expect(banner).toHaveTextContent("Padaria Sol");
    const time = new Date(expiresAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
    expect(banner).toHaveTextContent(`até ${time}`);
  });

  it("'Sair do acesso' ends the access on the server, clears only this tab and closes it", async () => {
    beginImpersonation("imp-access", "{}", {
      tenant: "Padaria Sol", user: "Ana Lima", expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    const ended: SessionEndReason[] = [];
    const off = onSessionEnded(r => ended.push(r));
    renderBanner();

    fireEvent.click(screen.getByRole("button", { name: /Sair do acesso/ }));

    await waitFor(() => expect(closeSpy).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/auth\/impersonation\/end$/);
    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer imp-access");

    expect(sessionStorage.getItem(TOKEN_KEYS.access)).toBeNull();
    expect(sessionStorage.getItem(TOKEN_KEYS.session)).toBeNull();
    expect(getImpersonation()?.ended).toBe(true);
    expect(ended).toEqual(["impersonation_left"]);
    // the admin's own session is untouched
    expect(localStorage.getItem(TOKEN_KEYS.access)).toBe("admin-access");
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBe("p.admin-refresh");
    expect(localStorage.getItem(TOKEN_KEYS.session)).toBe("{\"type\":\"platform\"}");
    off();
  });

  it("leaves even when the server call fails", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("offline"));
    beginImpersonation("imp-access", "{}", { tenant: "T", user: "U", expiresAt: null });
    renderBanner();
    fireEvent.click(screen.getByRole("button", { name: /Sair do acesso/ }));
    await waitFor(() => expect(closeSpy).toHaveBeenCalled());
    expect(getImpersonation()?.ended).toBe(true);
    expect(localStorage.getItem(TOKEN_KEYS.access)).toBe("admin-access");
  });
});
