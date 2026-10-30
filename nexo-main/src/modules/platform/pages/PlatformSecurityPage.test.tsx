import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { screen, fireEvent, waitFor, within, cleanup } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError, TOKEN_KEYS } from "@/services/api-client";
import PlatformSecurityPage from "./PlatformSecurityPage";
import * as api from "../services/platformSecurityApi";
import { renderAi, polyfillRadix } from "../test/aiFixtures";
import type { PlatformSecurity } from "../types";

const endLocalSession = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({
  useAuth: () => ({ session: { role: "super_admin", type: "platform" }, endLocalSession }),
}));
vi.mock("../services/platformSecurityApi", () => ({
  fetchPlatformSecurity: vi.fn(),
  startPlatformMfaSetup: vi.fn(),
  enablePlatformMfa: vi.fn(),
  disablePlatformMfa: vi.fn(),
  revokePlatformSession: vi.fn(),
  revokeAllPlatformSessions: vi.fn(),
}));
vi.mock("qrcode", () => ({ default: { toDataURL: vi.fn().mockResolvedValue("data:image/png;base64,qr") } }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const session = (over: Partial<PlatformSecurity["sessions"][number]>) => ({
  id: "s", createdAt: "2026-10-30T10:00:00Z", lastUsedAt: "2026-10-30T10:30:00Z", expiresAt: "2026-11-06T10:00:00Z",
  mfaVerified: false, ipAddress: "10.0.0.1", userAgent: "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0", current: false, ...over,
});

const security = (over: Partial<PlatformSecurity> = {}): PlatformSecurity => ({
  mfa: { enabled: false, setupPending: false, recoveryCodesLeft: 0 },
  currentSessionMfaVerified: false,
  sessions: [
    session({ id: "current", current: true }),
    session({ id: "other", userAgent: "Mozilla/5.0 (iPhone) Safari/605.1", ipAddress: "10.0.0.2" }),
  ],
  ...over,
});

const codes = ["aaaa-1111", "bbbb-2222", "cccc-3333", "dddd-4444", "eeee-5555", "ffff-6666", "gggg-7777", "hhhh-8888"];

describe("PlatformSecurityPage", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(api.fetchPlatformSecurity).mockResolvedValue(security());
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("sets up two-step verification, shows the recovery codes once and then sends the admin to sign in again", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "admin-access");
    localStorage.setItem(TOKEN_KEYS.refresh, "p.admin-refresh");
    vi.mocked(api.startPlatformMfaSetup).mockResolvedValue({ secret: "JBSWY3DPEHPK3PXP", otpauthUri: "otpauth://totp/Orken:admin?secret=JBSWY3DPEHPK3PXP" });
    vi.mocked(api.enablePlatformMfa).mockResolvedValue({ recoveryCodes: codes });
    renderAi(<PlatformSecurityPage />);

    expect(await screen.findByText(/Esta sessão entrou sem verificação em duas etapas/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ativar verificação em duas etapas" }));
    fireEvent.change(screen.getByLabelText("Sua senha"), { target: { value: "senha-forte" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(api.startPlatformMfaSetup).toHaveBeenCalledWith("senha-forte"));
    expect(await screen.findByText("JBSWY3DPEHPK3PXP")).toBeInTheDocument();
    expect(await screen.findByAltText("QR code para o aplicativo autenticador")).toBeInTheDocument();

    const activate = screen.getByRole("button", { name: "Ativar" });
    fireEvent.change(screen.getByLabelText(/código de 6 números/), { target: { value: "12a" } });
    expect(activate).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/código de 6 números/), { target: { value: "123456" } });
    fireEvent.click(activate);

    await waitFor(() => expect(api.enablePlatformMfa).toHaveBeenCalledWith("123456"));
    const list = await screen.findByRole("list", { name: "Códigos de recuperação" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(8);
    expect(list).toHaveTextContent("aaaa-1111");
    // every session ended on the server: the dead tokens are dropped right away
    expect(localStorage.getItem(TOKEN_KEYS.access)).toBeNull();
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBeNull();
    expect(endLocalSession).not.toHaveBeenCalled();

    const again = screen.getByRole("button", { name: "Entrar de novo" });
    expect(again).toBeDisabled();
    fireEvent.click(screen.getByLabelText("Guardei os códigos em local seguro"));
    fireEvent.click(again);
    expect(endLocalSession).toHaveBeenCalledWith("/login?motivo=mfa");
  });

  it("shows the server's message when the code is wrong", async () => {
    vi.mocked(api.startPlatformMfaSetup).mockResolvedValue({ secret: "S", otpauthUri: "otpauth://x" });
    vi.mocked(api.enablePlatformMfa).mockRejectedValue(new ApiError(422, "Código inválido.", "invalid_code", undefined, undefined, "Código inválido."));
    renderAi(<PlatformSecurityPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Ativar verificação em duas etapas" }));
    fireEvent.change(screen.getByLabelText("Sua senha"), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    fireEvent.change(await screen.findByLabelText(/código de 6 números/), { target: { value: "000000" } });
    fireEvent.click(screen.getByRole("button", { name: "Ativar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Código inválido."));
    expect(screen.queryByRole("list", { name: "Códigos de recuperação" })).not.toBeInTheDocument();
  });

  it("disables with password and a recovery code, then signs in again", async () => {
    vi.mocked(api.fetchPlatformSecurity).mockResolvedValue(security({
      mfa: { enabled: true, setupPending: false, recoveryCodesLeft: 7 }, currentSessionMfaVerified: true,
    }));
    vi.mocked(api.disablePlatformMfa).mockResolvedValue(undefined);
    renderAi(<PlatformSecurityPage />);
    expect(await screen.findByText(/7 códigos de recuperação restantes/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Desativar verificação em duas etapas" }));
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "senha" } });
    fireEvent.click(screen.getByRole("button", { name: "Usar um código de recuperação" }));
    fireEvent.change(screen.getByLabelText("Código de recuperação"), { target: { value: "aaaa-1111" } });
    fireEvent.click(screen.getByRole("button", { name: "Desativar" }));
    await waitFor(() => expect(api.disablePlatformMfa).toHaveBeenCalledWith({ password: "senha", recoveryCode: "aaaa-1111" }));
    expect(endLocalSession).toHaveBeenCalledWith("/login?motivo=mfa-desativada");
  });

  it("lists the sessions, marks the current one and revokes another", async () => {
    vi.mocked(api.revokePlatformSession).mockResolvedValue(undefined);
    renderAi(<PlatformSecurityPage />);

    const current = await screen.findByRole("listitem", { name: "Esta sessão" });
    expect(within(current).getByText("Esta sessão")).toBeInTheDocument();
    expect(within(current).queryByRole("button", { name: /Encerrar/ })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Encerrar sessão Safari · iPhone/iPad" }));
    await waitFor(() => expect(api.revokePlatformSession).toHaveBeenCalledWith("other"));
    await waitFor(() => expect(api.fetchPlatformSecurity).toHaveBeenCalledTimes(2));
    expect(toast.success).toHaveBeenCalledWith("Sessão encerrada.");
  });

  it("revoking every session asks first and then signs out", async () => {
    vi.mocked(api.revokeAllPlatformSessions).mockResolvedValue(undefined);
    renderAi(<PlatformSecurityPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Encerrar todas" }));
    expect(api.revokeAllPlatformSessions).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Encerrar todas" }));
    await waitFor(() => expect(api.revokeAllPlatformSessions).toHaveBeenCalled());
    await waitFor(() => expect(endLocalSession).toHaveBeenCalledWith("/login?motivo=sessoes"));
  });
});
