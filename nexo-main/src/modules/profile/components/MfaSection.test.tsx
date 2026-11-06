import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import { MfaSection } from "./MfaSection";
import { disableMfa, fetchMfaStatus } from "@/modules/auth/services/authService";
import { getAccessToken, getRefreshToken, setTokens } from "@/services/api-client";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("qrcode", () => ({ default: { toDataURL: vi.fn().mockResolvedValue("data:image/png;base64,") } }));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ logout: vi.fn() }) }));
vi.mock("@/modules/auth/services/authService", () => ({
  fetchMfaStatus: vi.fn().mockResolvedValue({ enabled: false, recoveryCodesLeft: 0 }),
  startMfaSetup: vi.fn().mockResolvedValue({ secret: "SECRET", otpAuthUri: "otpauth://totp/x" }),
  enableMfa: vi.fn().mockResolvedValue({ recoveryCodes: ["AAAA-1111", "BBBB-2222"] }),
  disableMfa: vi.fn(),
}));

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}><MemoryRouter>{children}</MemoryRouter></QueryClientProvider>;
}

/** Walks the real setup until the recovery codes are on screen. */
async function reachRecoveryCodes() {
  render(<MfaSection />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: /Ativar verificação/ }));
  fireEvent.change(screen.getByLabelText("Sua senha"), { target: { value: "senha" } });
  fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
  fireEvent.change(await screen.findByLabelText(/código de 6 números/), { target: { value: "123456" } });
  fireEvent.click(screen.getByRole("button", { name: "Ativar" }));
  await screen.findByText("AAAA-1111");
}

beforeEach(() => {
  vi.mocked(toast.error).mockClear();
  vi.mocked(toast.success).mockClear();
  vi.mocked(fetchMfaStatus).mockClear();
});
afterEach(cleanup);

describe("MfaSection after turning it on", () => {
  it("drops the tokens the server just revoked, so no refetch ends the page before the codes are saved", async () => {
    setTokens("tenant-access", "tenant-refresh");
    await reachRecoveryCodes();

    expect(getAccessToken()).toBeNull();
    expect(getRefreshToken()).toBeNull();
    expect(vi.mocked(fetchMfaStatus)).toHaveBeenCalledTimes(1);
    expect(screen.getByText("BBBB-2222")).toBeTruthy();
  });
});

describe("MfaSection recovery codes copy", () => {
  it("copies every code and confirms", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard(writeText);
    await reachRecoveryCodes();
    fireEvent.click(screen.getByRole("button", { name: /Copiar/ }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Códigos copiados."));
    expect(writeText).toHaveBeenCalledWith("AAAA-1111\nBBBB-2222");
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("never says the codes were copied when the browser refused: they are shown only once", async () => {
    stubClipboard(vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")));
    await reachRecoveryCodes();
    fireEvent.click(screen.getByRole("button", { name: /Copiar/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(toast.success).not.toHaveBeenCalled();
  });
});

describe("MfaSection with the second step on", () => {
  async function reachDisableForm() {
    vi.mocked(fetchMfaStatus).mockResolvedValue({ enabled: true, setupPending: false, recoveryCodesLeft: 5 });
    render(<MfaSection />, { wrapper });
    fireEvent.click(await screen.findByRole("button", { name: /Desativar verificação/ }));
  }

  const confirmButton = () => screen.getByRole("button", { name: /^Desativar$/ }) as HTMLButtonElement;

  afterEach(() => {
    vi.mocked(fetchMfaStatus).mockResolvedValue({ enabled: false, setupPending: false, recoveryCodesLeft: 0 });
    vi.mocked(disableMfa).mockReset();
  });

  it("says it is on and how many recovery codes are left", async () => {
    vi.mocked(fetchMfaStatus).mockResolvedValue({ enabled: true, setupPending: false, recoveryCodesLeft: 5 });

    render(<MfaSection />, { wrapper });

    expect((await screen.findByText(/Ativa\./)).textContent).toContain("5");
  });

  it("turning it off needs both the password and a code", async () => {
    await reachDisableForm();
    expect(confirmButton().disabled).toBe(true);

    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "senha" } });
    expect(confirmButton().disabled).toBe(true);

    fireEvent.change(screen.getByLabelText("Código do app ou de recuperação"), { target: { value: "   " } });
    expect(confirmButton().disabled).toBe(true);

    fireEvent.change(screen.getByLabelText("Código do app ou de recuperação"), { target: { value: "123456" } });
    expect(confirmButton().disabled).toBe(false);
  });

  it("sends password and code, and confirms", async () => {
    vi.mocked(disableMfa).mockResolvedValue(undefined);
    await reachDisableForm();
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "senha" } });
    fireEvent.change(screen.getByLabelText("Código do app ou de recuperação"), { target: { value: "123456" } });

    fireEvent.click(confirmButton());

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Verificação em duas etapas desativada."));
    expect(vi.mocked(disableMfa).mock.calls[0].slice(0, 2)).toEqual(["senha", "123456"]);
  });

  it("refused, says so and keeps the form", async () => {
    vi.mocked(disableMfa).mockRejectedValue(new Error("Senha ou código incorretos."));
    await reachDisableForm();
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "senha" } });
    fireEvent.change(screen.getByLabelText("Código do app ou de recuperação"), { target: { value: "000000" } });

    fireEvent.click(confirmButton());

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Senha")).toBeTruthy();
  });

  it("cancelling goes back without asking the server", async () => {
    await reachDisableForm();

    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByLabelText("Senha")).toBeNull();
    expect(screen.getByRole("button", { name: /Desativar verificação/ })).toBeTruthy();
    expect(vi.mocked(disableMfa)).not.toHaveBeenCalled();
  });
});
