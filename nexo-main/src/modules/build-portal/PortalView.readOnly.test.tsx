import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { adaptPortal } from "./api/adapter";
import type { PublicBuildBootstrapDto } from "./api/generated/portal-types";
import { PortalView } from "./PortalView";

const bootstrap = (readOnly: boolean): PublicBuildBootstrapDto => ({
  contractVersion: "1.7",
  sections: [],
  readOnly,
  portal: { access: "Password" },
  company: { name: "Construtora de teste" },
  project: { name: "Obra de teste", type: "House", status: "InProgress" },
  phase: "construction",
});

describe("portal somente leitura", () => {
  it("avisa que decisões e pedidos estão indisponíveis, sem falar de assinatura", () => {
    render(<PortalView portal={adaptPortal(bootstrap(true), {})} />);

    const notice = screen.getByRole("heading", { name: "Portal disponível só para consulta" });
    expect(notice).toBeInTheDocument();
    expect(notice.parentElement?.textContent).toContain("Fale com a construtora");
    expect(document.body.textContent?.toLowerCase()).not.toContain("assinatura");
  });

  it("não mostra o aviso quando o portal aceita decisões", () => {
    render(<PortalView portal={adaptPortal(bootstrap(false), {})} />);

    expect(screen.queryByText("Portal disponível só para consulta")).not.toBeInTheDocument();
  });
});
