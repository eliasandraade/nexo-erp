import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup, fireEvent } from "@testing-library/react";
import { renderTax } from "../test/render";
import { ENGINE_SCENARIOS } from "../test/engineFixture";
import { EngineResult, TechnicalTrail } from "./EngineResult";
import { Delta, ScenarioCompare } from "./ScenarioCompare";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  verifyTaxCalculation: vi.fn(),
}));

const [current, proposed] = ENGINE_SCENARIOS;
const hashes = { calculationId: "calc-1", engineVersion: "orken-tax-engine/1.0.0", profileVersionNumber: 2, inputHash: "in", ruleSetHash: "rs", outputHash: "out" };

describe("EngineResult (real engine output)", () => {
  afterEach(cleanup);

  it("tells how much the operation costs and on which base", () => {
    renderTax(<EngineResult output={current.output} input={current.input} />);

    expect(screen.getByText("Quanto custa")).toBeInTheDocument();
    expect(screen.getAllByText("R$ 8,40").length).toBeGreaterThan(0);
    expect(screen.getByText("CBS")).toBeInTheDocument();
    expect(screen.getByText("IBS estadual")).toBeInTheDocument();
  });

  it("surfaces every warning of the engine: unknown taxes in the price, special regime, undeclared compliance", () => {
    renderTax(<EngineResult output={current.output} input={current.input} />);

    expect(screen.getByText(/ICMS, ISS, PIS e Cofins embutidos no preço não foram informados/)).toBeInTheDocument();
    expect(screen.getByText(/regime diferenciado ou específico \(reducao-60\)/)).toBeInTheDocument();
    expect(screen.getByText(/accessoryObligationsCompliant não declarado/)).toBeInTheDocument();
  });

  it("explains the 2026 compensation with its legal source", () => {
    renderTax(<EngineResult output={current.output} input={current.input} />);

    expect(screen.getByText(/compensados com o PIS e a Cofins/)).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "LC214-2025-ART348" })[0]).toHaveAttribute("href", "/tributos/regras?fonte=LC214-2025-ART348");
  });

  it("marks a hypothetical rate so it is never read as the rate in force", () => {
    const output = {
      ...current.output,
      lines: current.output.lines.map((l) => ({ ...l, components: l.components.map((c) => ({ ...c, isHypothesis: true })) })),
    };
    renderTax(<EngineResult output={output} />);

    expect(screen.getByText(/Contém alíquota hipotética/)).toBeInTheDocument();
  });

  it("lists rules that exist but were not applied", () => {
    const output = { ...current.output, notApplied: [{ ruleVersionId: "v", ruleKey: "CBS.RATE.REFERENCE-2027-2033", ruleVersion: 1, status: "PendingRegulation" as const }] };
    renderTax(<EngineResult output={output} />);

    expect(screen.getByText("Regras que existem mas não foram aplicadas")).toBeInTheDocument();
    expect(screen.getByText(/CBS\.RATE\.REFERENCE-2027-2033/)).toBeInTheDocument();
  });
});

describe("TechnicalTrail", () => {
  afterEach(cleanup);

  it("shows the fingerprints and reports a successful reproduction", async () => {
    vi.mocked(api.verifyTaxCalculation).mockResolvedValue({
      calculationId: "calc-1", reproducible: true, engineAvailable: true, engineVersionMatches: true, storedEngineVersion: "orken-tax-engine/1.0.0",
      currentEngineVersion: "orken-tax-engine/1.0.0", storedOutputHash: "out", recomputedOutputHash: "out", differences: [],
    });
    renderTax(<TechnicalTrail output={current.output} hashes={hashes} />);

    fireEvent.click(screen.getByRole("button", { name: /Rastro técnico/ }));
    expect(screen.getByText("rs")).toBeInTheDocument();
    expect(screen.getByText(`Regras executadas (${current.output.executions.length})`)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Verificar reprodução/ }));

    expect(await screen.findByText(/Reproduzido: o mesmo motor, as mesmas regras/)).toBeInTheDocument();
    expect(api.verifyTaxCalculation).toHaveBeenCalledWith("calc-1");
  });

  it("says plainly when the engine that made the calculation is gone, and shows no recomputed hash", async () => {
    vi.mocked(api.verifyTaxCalculation).mockResolvedValue({
      calculationId: "calc-1", reproducible: false, engineAvailable: false, engineVersionMatches: false,
      storedEngineVersion: "orken-tax-engine/1.0.0", currentEngineVersion: "orken-tax-engine/2.0.0", storedOutputHash: "a",
      recomputedOutputHash: null, differences: ["A engine orken-tax-engine/1.0.0 que fez este cálculo não está disponível."],
    });
    renderTax(<TechnicalTrail output={current.output} hashes={hashes} />);

    fireEvent.click(screen.getByRole("button", { name: /Rastro técnico/ }));
    fireEvent.click(screen.getByRole("button", { name: /Verificar reprodução/ }));

    expect(await screen.findByText(/Nenhum outro motor foi usado no lugar dele/)).toBeInTheDocument();
    expect(screen.queryByText(/recalculado:/)).not.toBeInTheDocument();
  });

  it("reports a failed reproduction with the engine versions and the differences", async () => {
    vi.mocked(api.verifyTaxCalculation).mockResolvedValue({
      calculationId: "calc-1", reproducible: false, engineAvailable: true, engineVersionMatches: false, storedEngineVersion: "orken-tax-engine/0.9.0",
      currentEngineVersion: "orken-tax-engine/1.0.0", storedOutputHash: "a", recomputedOutputHash: "b",
      differences: ["O resultado recalculado é diferente do armazenado."],
    });
    renderTax(<TechnicalTrail output={current.output} hashes={hashes} />);

    fireEvent.click(screen.getByRole("button", { name: /Rastro técnico/ }));
    fireEvent.click(screen.getByRole("button", { name: /Verificar reprodução/ }));

    expect(await screen.findByText(/Não reproduziu/)).toBeInTheDocument();
    expect(screen.getByText(/Motor guardado orken-tax-engine\/0\.9\.0/)).toBeInTheDocument();
    expect(screen.getByText("O resultado recalculado é diferente do armazenado.")).toBeInTheDocument();
  });

  it("offers no reproduction check for a simulated scenario (nothing stored to compare)", () => {
    renderTax(<TechnicalTrail output={current.output} />);

    fireEvent.click(screen.getByRole("button", { name: /Rastro técnico/ }));
    expect(screen.queryByRole("button", { name: /Verificar reprodução/ })).not.toBeInTheDocument();
  });
});

describe("ScenarioCompare", () => {
  afterEach(cleanup);

  it("compares the scenarios the engine produced, the baseline marked as current", () => {
    renderTax(<ScenarioCompare scenarios={[current, proposed]} />);

    expect(screen.getAllByText("atual").length).toBeGreaterThan(0);
    expect(screen.getAllByText(proposed.name).length).toBeGreaterThan(0);
    expect(screen.getAllByText("R$ 7,90").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Sem efeito de caixa no período.").length).toBeGreaterThan(0);
  });

  it("reads a lower tax as good, a higher one as bad, and a cent as no change", () => {
    const { rerender } = renderTax(<Delta value={-0.5} />);
    expect(screen.getByText("−R$ 0,50")).toHaveClass("text-success");
    rerender(<Delta value={1.25} />);
    expect(screen.getByText("+R$ 1,25")).toHaveClass("text-destructive");
    rerender(<Delta value={0.004} />);
    expect(screen.getByText("sem mudança")).toBeInTheDocument();
  });
});
