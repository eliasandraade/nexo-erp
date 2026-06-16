import { describe, it, expect } from "vitest";
import type { AuthSession } from "@/modules/auth/types";
import type { UserRole } from "@/modules/users/types";
import { resolvePostLogin, isManagementRole } from "./resolvePostLogin";
import { availableWorkspaces, workspaceForPath } from "./config";

function session(role: UserRole, modules: string[], type: "tenant" | "platform" = "tenant"): AuthSession {
  return {
    userId: "u1",
    tenantId: "t1",
    name: "Test",
    role,
    login: "test",
    email: "t@t.com",
    modules,
    storeIds: [],
    companyName: "ACME",
    type,
    isNewAccount: false,
  };
}

describe("resolvePostLogin", () => {
  it("sends platform users to /platform", () => {
    expect(resolvePostLogin(session("diretoria", [], "platform"), null)).toBe("/platform");
  });

  it("keeps operational roles on their role-based home (no workspace step)", () => {
    expect(resolvePostLogin(session("vendedor", ["varejo"]), null)).toBe("/pdv");
    expect(resolvePostLogin(session("vendedor", ["restaurante"]), null)).toBe("/restaurante");
    expect(resolvePostLogin(session("cozinha", ["restaurante"]), null)).toBe("/restaurante/cozinha");
    expect(resolvePostLogin(session("estoquista", ["varejo"]), null)).toBe("/estoque");
  });

  it("auto-enters the only available workspace for management (Caso 1)", () => {
    expect(resolvePostLogin(session("diretoria", ["varejo"]), null)).toBe("/dashboard");
    expect(resolvePostLogin(session("gerente", ["build"]), null)).toBe("/build");
  });

  it("shows the selector when management has 2+ workspaces and no saved choice (Caso 2)", () => {
    expect(resolvePostLogin(session("diretoria", ["varejo", "restaurante"]), null)).toBe("/workspaces");
  });

  it("honors a still-valid saved workspace choice", () => {
    expect(resolvePostLogin(session("diretoria", ["varejo", "build"]), "build")).toBe("/build");
  });

  it("ignores a saved choice the tenant no longer has access to", () => {
    // saved "menu" but only varejo+build available → falls back to the selector
    expect(resolvePostLogin(session("diretoria", ["varejo", "build"]), "menu")).toBe("/workspaces");
  });

  it("routes management with no active module to the selector (Caso 3 state)", () => {
    expect(resolvePostLogin(session("diretoria", []), null)).toBe("/workspaces");
  });
});

describe("availableWorkspaces", () => {
  it("maps module keys to workspaces in display order", () => {
    const ws = availableWorkspaces(session("diretoria", ["build", "varejo"]));
    expect(ws.map((w) => w.id)).toEqual(["store", "build"]);
  });

  it("returns empty when no modules are active", () => {
    expect(availableWorkspaces(session("diretoria", []))).toHaveLength(0);
  });
});

describe("workspaceForPath", () => {
  it("infers the vertical workspace from a deep-linked route", () => {
    expect(workspaceForPath("/restaurante/cozinha")).toBe("menu");
    expect(workspaceForPath("/build/projetos/1")).toBe("build");
    expect(workspaceForPath("/pdv")).toBe("store");
  });

  it("returns null for shared routes", () => {
    expect(workspaceForPath("/dashboard")).toBeNull();
    expect(workspaceForPath("/produtos")).toBeNull();
  });
});

describe("isManagementRole", () => {
  it("is true only for diretoria and gerente", () => {
    expect(isManagementRole("diretoria")).toBe(true);
    expect(isManagementRole("gerente")).toBe(true);
    expect(isManagementRole("vendedor")).toBe(false);
    expect(isManagementRole("cozinha")).toBe(false);
    expect(isManagementRole("estoquista")).toBe(false);
  });
});
