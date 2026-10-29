import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup } from "@testing-library/react";
import { renderTax } from "../test/render";
import { TaxGovernanceBanner } from "./TaxGovernanceBanner";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxGovernance: vi.fn(),
}));

const shadow = "Piloto controlado — resultados para análise e validação. Não executa alteração fiscal, contábil ou financeira automaticamente.";
const base: api.TaxGovernanceStatusDto = {
  enabled: true, mode: "ControlledPilot", policy: "orken-pilot-approval/1", admitted: true, refusalCode: null, refusalMessage: null,
  effectsNotice: shadow, pilotScope: null,
};

describe("TaxGovernanceBanner", () => {
  afterEach(cleanup);

  it("says the pilot is a shadow mode resting on an internal approval, not a professional review", async () => {
    vi.mocked(api.getTaxGovernance).mockResolvedValue(base);
    renderTax(<TaxGovernanceBanner />);

    expect(await screen.findByText(shadow)).toBeInTheDocument();
    expect(screen.getByText(/aprovação interna do piloto, não revisão profissional\. .*Não é parecer jurídico, contábil ou tributário profissional/)).toBeInTheDocument();
  });

  it("says why new work is refused for a company outside the pilot", async () => {
    vi.mocked(api.getTaxGovernance).mockResolvedValue({
      ...base, admitted: false, refusalCode: "tax_pilot_scope_tenant", refusalMessage: "Esta empresa não participa do piloto controlado.",
    });
    renderTax(<TaxGovernanceBanner />);

    expect(await screen.findByText(/Esta empresa não participa do piloto controlado\./)).toBeInTheDocument();
  });

  it("says the module is suspended by the platform before anything else", async () => {
    vi.mocked(api.getTaxGovernance).mockResolvedValue({
      ...base, admitted: false, moduleStatus: "Suspended", refusalCode: "tax_module_suspended", refusalMessage: "Suspenso para manutenção.",
    });
    renderTax(<TaxGovernanceBanner />);

    expect(await screen.findByText("Módulo suspenso pela plataforma")).toBeInTheDocument();
    expect(screen.getByText("Suspenso para manutenção.")).toBeInTheDocument();
    expect(screen.queryByText("Piloto controlado")).not.toBeInTheDocument();
  });

  it("says the module is off when the kill switch is off", async () => {
    vi.mocked(api.getTaxGovernance).mockResolvedValue({ ...base, enabled: false, mode: "Disabled", admitted: false, effectsNotice: null });
    renderTax(<TaxGovernanceBanner />);

    expect(await screen.findByText("Inteligência tributária desligada nesta instalação")).toBeInTheDocument();
    expect(screen.queryByText(/Piloto controlado/)).not.toBeInTheDocument();
  });
});
