import routerSource from "@/app/router/AppRouter.tsx?raw";
import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { PortalSession } from "./PortalPage";
import { resolveBuildPortal } from "./route";
import { percent, safeUrl } from "./types";
describe("Public portal isolation", () => {
  it.each([
    "/service/agenda",
    "/platform/tenants",
    "/agendar/company",
    "/clientes/novo",
    "/rastrear/token",
    "/api/build",
    "/menu/store",
  ])("preserves existing route %s", (path) =>
    expect(resolveBuildPortal(path)).toBe(false),
  );
  it("resolves requested custom URL", () =>
    expect(resolveBuildPortal("/solutie/shoppingeusebio")).toBe(true));
  it("never turns missing progress into zero", () => {
    expect(percent(null)).toBe("Não informado");
    expect(percent(0)).toBe("0%");
    expect(percent(NaN)).toBe("Não informado");
  });
  it("rejects executable and insecure URLs", () => {
    expect(safeUrl("javascript:alert(1)")).toBeUndefined();
    expect(safeUrl("http://private.test")).toBeUndefined();
  });
  it("private response exposes no project metadata", async () => {
    const gateway = {
      load: vi.fn().mockResolvedValue({ kind: "private" }),
      unlock: vi.fn().mockResolvedValue({ kind: "wrong-password" }),
    };
    render(
      <PortalSession
        tenant="secret-company"
        project="secret-project"
        gateway={gateway}
      />,
    );
    await screen.findByLabelText("Senha de acesso");
    expect(document.body.textContent).not.toContain("secret-project");
    expect(
      document.querySelector('meta[name="robots"]')?.getAttribute("content"),
    ).toContain("noindex");
    fireEvent.change(screen.getByLabelText("Senha de acesso"), {
      target: { value: "bad" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Acessar minha obra" }));
    await screen.findByText("Não foi possível acessar com essa senha.");
    expect(screen.getByLabelText("Senha de acesso")).toHaveValue("");
  });
  it("ignores an aborted late response", async () => {
    let finish: (v: unknown) => void = () => {};
    const gateway = {
      load: vi.fn(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      ),
      unlock: vi.fn(),
    };
    const view = render(
      <PortalSession tenant="one" project="one" gateway={gateway as never} />,
    );
    view.unmount();
    finish({ kind: "private" });
    await waitFor(() =>
      expect(
        screen.queryByLabelText("Senha de acesso"),
      ).not.toBeInTheDocument(),
    );
  });
});

it.each([
 ['expired','Seu acesso expirou. Informe a senha novamente.'],
 ['rate-limited','Muitas tentativas. Aguarde antes de tentar novamente.'],
 ['unpublished','Este acompanhamento ainda não está publicado.'],
 ['not-found','Não encontramos este acompanhamento.'],
 ['unavailable','O acompanhamento está indisponível neste momento.']
])('renders safe access state %s',async(kind,message)=>{
 const gateway={load:vi.fn().mockResolvedValue({kind}),unlock:vi.fn()};
 render(<PortalSession tenant="private-tenant" project="private-project" gateway={gateway}/>);
 await screen.findByText(message);expect(document.title).toBe('Minha Obra');expect(document.body.textContent).not.toContain('private-project');
});

it('keeps every existing root namespace out of the public resolver',()=>{for(const match of routerSource.matchAll(/path="\/([^/" :]+)/g))expect(resolveBuildPortal('/'+match[1]+'/example')).toBe(false);});
