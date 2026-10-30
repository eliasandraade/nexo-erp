import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AdminMfaBanner } from "./AdminMfaBanner";

const auth = { session: { role: "super_admin" } as { role: string } | null };
let allowed: boolean | null = false;
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => auth }));
vi.mock("../hooks/usePlatformSecurity", () => ({ useAdminActionsAllowed: () => allowed }));

const show = () => render(<MemoryRouter><AdminMfaBanner /></MemoryRouter>);

describe("AdminMfaBanner", () => {
  afterEach(cleanup);

  it("tells a super administrator without a two-step sign-in where to turn it on", () => {
    auth.session = { role: "super_admin" };
    allowed = false;
    show();
    expect(screen.getByRole("status")).toHaveTextContent("Ações administrativas exigem login com verificação em duas etapas.");
    expect(screen.getByRole("link", { name: "Configurar segurança" })).toHaveAttribute("href", "/platform/seguranca");
  });

  it("tells support it can only read, without a setup link", () => {
    auth.session = { role: "support" };
    allowed = false;
    show();
    expect(screen.getByRole("status")).toHaveTextContent("Seu perfil de suporte permite apenas consultar.");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("stays out of the way when admin acts are allowed or still unknown", () => {
    auth.session = { role: "super_admin" };
    allowed = true;
    show();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    cleanup();
    allowed = null;
    show();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
