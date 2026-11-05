import { render, renderHook, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { ProtectedRoute } from "./ProtectedRoute";
import { ModuleRoute } from "./ModuleRoute";
import { RoleRoute } from "./RoleRoute";
import { useNavigation } from "@/components/shared/useNavigation";
import { ServiceModuleRoute } from "@/modules/service/routing/ServiceModuleRoute";
import { resolvePostLogin } from "@/modules/workspace/resolvePostLogin";
import type { AuthSession } from "@/modules/auth/types";
import type { UserRole } from "@/modules/users/types";

const useAuthMock = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => useAuthMock() }));
vi.mock("@/modules/workspace/WorkspaceContext", () => ({ useWorkspace: () => ({ active: null }) }));

const EXPIRED = "2026-01-01T00:00:00Z";

function session(role: UserRole, modules: string[], readOnlyModules: string[], trialEndsAt?: string): AuthSession {
  return {
    userId: "u1", tenantId: "t1", name: "Pessoa", role, login: "pessoa", email: "p@orken.test", modules, readOnlyModules,
    storeIds: [], companyName: "ACME", type: "tenant", isNewAccount: false, trialEndsAt,
  };
}

/** Same guards, same nesting as AppRouter. The screens only say where they are. */
function App() {
  const screenAt = (path: string) => <Route path={path} element={<p data-testid="screen">{path}</p>} />;
  return (
    <Routes>
      <Route element={<ProtectedRoute />}>
        <Route element={<ModuleRoute moduleKey="varejo" />}><Route element={<RoleRoute path="/pdv" />}>{screenAt("/pdv")}</Route></Route>
        <Route element={<ModuleRoute moduleKey="build" />}><Route element={<RoleRoute path="/build" />}>{screenAt("/build")}</Route></Route>
        <Route element={<ServiceModuleRoute />}><Route element={<RoleRoute path="/service" />}>{screenAt("/service")}</Route></Route>
        <Route element={<ModuleRoute moduleKey="restaurante" />}>
          <Route element={<RoleRoute path="/restaurante" />}>{screenAt("/restaurante")}</Route>
          <Route element={<RoleRoute path="/restaurante/cozinha" />}>{screenAt("/restaurante/cozinha")}</Route>
          <Route element={<RoleRoute path="/restaurante/portal" />}>{screenAt("/restaurante/portal")}</Route>
        </Route>
        <Route element={<RoleRoute path="/dashboard" />}>
          {screenAt("/dashboard")}
          {screenAt("/vendas")}
          {screenAt("/assinatura")}
        </Route>
        <Route element={<RoleRoute path="/usuarios" />}>{screenAt("/usuarios")}</Route>
        <Route element={<RoleRoute path="/estoque" />}>{screenAt("/estoque")}</Route>
        {screenAt("/workspaces")}
        {screenAt("/perfil")}
      </Route>
    </Routes>
  );
}

/** Breaks a navigation loop as a test failure: without it, a guard loop would never let the render end. */
function LoopGuard() {
  const { pathname, key } = useLocation();
  const seen = (LoopGuard.seen ??= new Set<string>());
  seen.add(key);
  if (seen.size > 12) throw new Error(`laço de navegação, parou em ${pathname}`);
  return null;
}
LoopGuard.seen = undefined as Set<string> | undefined;

function renderAt(s: AuthSession, path: string): string {
  LoopGuard.seen = undefined;
  useAuthMock.mockReturnValue({ session: s, isReady: true });
  const view = render(
    <MemoryRouter initialEntries={[path]}>
      <LoopGuard />
      <App />
    </MemoryRouter>,
  );
  const where = screen.getByTestId("screen").textContent ?? "";
  view.unmount();
  return where;
}

beforeEach(() => vi.spyOn(console, "error").mockImplementation(() => undefined));

describe("somente leitura: cada papel navega, sem laço e sem tela vazia", () => {
  it.each<[string, UserRole, string[], string, string]>([
    // [caso, papel, módulos vencidos, onde abre, onde fica]
    ["diretoria abre o painel", "diretoria", ["varejo"], "/dashboard", "/dashboard"],
    ["diretoria consulta as vendas", "diretoria", ["varejo"], "/vendas", "/vendas"],
    ["diretoria abre o PDV vencido", "diretoria", ["varejo"], "/pdv", "/pdv"],
    ["diretoria abre o restaurante vencido", "diretoria", ["restaurante"], "/restaurante/portal", "/restaurante/portal"],
    ["diretoria abre o Build vencido", "diretoria", ["build"], "/build", "/build"],
    ["diretoria abre o Service vencido", "diretoria", ["service"], "/service", "/service"],
    ["diretoria abre a assinatura", "diretoria", ["varejo"], "/assinatura", "/assinatura"],
    ["diretoria abre usuários, para bloquear alguém", "diretoria", ["varejo"], "/usuarios", "/usuarios"],
    ["gerente abre o painel", "gerente", ["varejo"], "/dashboard", "/dashboard"],
    ["gerente abre o Build vencido", "gerente", ["build"], "/build", "/build"],
    ["vendedor abre o PDV vencido", "vendedor", ["varejo"], "/pdv", "/pdv"],
    ["vendedor cai no PDV vencido, saindo do painel", "vendedor", ["varejo"], "/dashboard", "/pdv"],
    ["vendedor abre o salão vencido", "vendedor", ["restaurante"], "/restaurante", "/restaurante"],
    ["vendedor cai no salão vencido, saindo do painel", "vendedor", ["restaurante"], "/dashboard", "/restaurante"],
    ["estoquista abre o estoque", "estoquista", ["varejo"], "/estoque", "/estoque"],
    ["estoquista volta ao estoque, saindo do painel", "estoquista", ["varejo"], "/dashboard", "/estoque"],
    ["cozinha abre a cozinha vencida", "cozinha", ["restaurante"], "/restaurante/cozinha", "/restaurante/cozinha"],
    ["cozinha volta à cozinha, saindo do painel", "cozinha", ["restaurante"], "/dashboard", "/restaurante/cozinha"],
  ])("%s", (_case, role, readOnly, from, to) => {
    expect(renderAt(session(role, [], readOnly, EXPIRED), from)).toBe(to);
  });

  it.each<[string, UserRole, string[], string, string]>([
    ["vendedor sem módulo nenhum vai ao perfil", "vendedor", [], "/dashboard", "/perfil"],
    ["cozinha sem o restaurante vai ao perfil", "cozinha", ["varejo"], "/restaurante/cozinha", "/perfil"],
    ["cozinha sem módulo nenhum vai ao perfil", "cozinha", [], "/dashboard", "/perfil"],
    ["estoquista sem módulo nenhum fica no estoque, que é do núcleo", "estoquista", [], "/dashboard", "/estoque"],
    ["diretoria com outro módulo vencido não abre o Build", "diretoria", ["varejo"], "/build", "/dashboard"],
    ["vendedor com varejo vencido não abre o restaurante", "vendedor", ["varejo"], "/restaurante", "/pdv"],
  ])("módulo que a empresa nunca teve: %s", (_case, role, readOnly, from, to) => {
    expect(renderAt(session(role, [], readOnly, EXPIRED), from)).toBe(to);
  });

  it("um módulo ativo e outro vencido: os dois abrem", () => {
    const mixed = session("diretoria", ["build"], ["varejo"]);

    expect(renderAt(mixed, "/build")).toBe("/build");
    expect(renderAt(mixed, "/pdv")).toBe("/pdv");
  });

  it("com o módulo válido, nada muda", () => {
    expect(renderAt(session("vendedor", ["varejo"], []), "/pdv")).toBe("/pdv");
    expect(renderAt(session("vendedor", ["varejo"], []), "/dashboard")).toBe("/pdv");
    expect(renderAt(session("diretoria", ["varejo"], []), "/dashboard")).toBe("/dashboard");
  });

  it("uma sessão guardada antes desta mudança, sem a lista de vencidos, não quebra", () => {
    const stored = session("diretoria", [], []);
    delete stored.readOnlyModules;

    expect(renderAt(stored, "/dashboard")).toBe("/dashboard");
    expect(renderAt(stored, "/pdv")).toBe("/dashboard");
  });
});

describe("somente leitura: o menu mostra as telas do módulo vencido", () => {
  const pathsFor = (s: AuthSession) => {
    useAuthMock.mockReturnValue({ session: s, isReady: true });
    const { result } = renderHook(() => useNavigation());
    return result.current.map((route) => route.path);
  };

  it("módulo vencido aparece, e o que a empresa nunca teve não", () => {
    const paths = pathsFor(session("diretoria", [], ["varejo", "service"], EXPIRED));

    expect(paths).toEqual(expect.arrayContaining(["/pdv", "/listas-de-preco", "/service", "/dashboard", "/usuarios"]));
    expect(paths).not.toContain("/build");
    expect(paths).not.toContain("/restaurante");
  });

  it("cada papel segue vendo só o que é dele", () => {
    const paths = pathsFor(session("vendedor", [], ["varejo"], EXPIRED));

    expect(paths).toContain("/pdv");
    expect(paths).not.toContain("/listas-de-preco");
    expect(paths).not.toContain("/dashboard");
  });
});

describe("somente leitura: o destino pós-login sempre leva a uma tela", () => {
  it.each<[UserRole, string[], string[]]>([
    ["diretoria", [], ["varejo"]],
    ["diretoria", [], ["varejo", "build"]],
    ["diretoria", [], []],
    ["gerente", [], ["restaurante"]],
    ["gerente", ["build"], ["varejo"]],
    ["vendedor", [], ["varejo"]],
    ["vendedor", [], ["restaurante"]],
    ["vendedor", [], []],
    ["vendedor", ["build"], ["varejo"]],
    ["estoquista", [], ["varejo"]],
    ["estoquista", [], []],
    ["cozinha", [], ["restaurante"]],
    ["cozinha", [], ["varejo"]],
    ["cozinha", [], []],
  ])("%s, módulos %j, vencidos %j", (role, modules, readOnly) => {
    const s = session(role, modules, readOnly, EXPIRED);
    const target = resolvePostLogin(s, null);

    expect(renderAt(s, target)).toBe(target);
  });
});
