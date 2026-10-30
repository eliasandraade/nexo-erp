import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { toast } from "sonner";
import { renderTax } from "../test/render";
import TaxProfilePage from "./TaxProfilePage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

const session = { role: "diretoria" as string, userId: "u-dir" };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxProfile: vi.fn(),
  addTaxProfileVersion: vi.fn(),
  listObligations: vi.fn().mockResolvedValue([]),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const simples: api.TaxProfileVersionDto = {
  id: "v3", versionNumber: 3, validFrom: "2026-08-01", regime: "SimplesNacional", state: "SP", cityIbgeCode: "3550308",
  primaryCnae: "4781400", secondaryCnaes: ["4782201"], specialRegimes: [], benefits: [],
  reason: "Volta ao Simples", createdByName: "Diretora Exemplo", createdAt: "2026-08-01T12:00:00Z",
  simplesSituation: "WithinSublimit", ibsCbsRegularRegimeOption: true,
} as api.TaxProfileVersionDto;

function show(current: api.TaxProfileVersionDto | null = simples) {
  vi.mocked(api.getTaxProfile).mockResolvedValue({ current, versions: current ? [current] : [], suggestion: null, complianceAssessments: [] });
  return renderTax(<TaxProfilePage />);
}

const combo = (name: string) => screen.getByRole("combobox", { name });
async function pick(name: string, option: string) {
  fireEvent.click(combo(name));
  fireEvent.click(await screen.findByRole("option", { name: option }));
}

describe("TaxProfilePage — Simples situation and IBS/CBS regular option", { timeout: 30_000 }, () => {
  beforeEach(() => { vi.clearAllMocks(); session.role = "diretoria"; });
  afterEach(cleanup);

  it("shows the Simples situation and the IBS/CBS option of the version in force", async () => {
    show();

    expect(await screen.findByText("Simples Nacional dentro do sublimite", { selector: "dd" })).toBeInTheDocument();
    expect(screen.getByText("Optou pelo regime regular do IBS/CBS")).toBeInTheDocument();
    cleanup();

    show({ ...simples, ibsCbsRegularRegimeOption: false });
    expect(await screen.findByText("Recolhe IBS/CBS pelo Simples", { selector: "dd" })).toBeInTheDocument();
    cleanup();

    show({ ...simples, ibsCbsRegularRegimeOption: null });
    expect(await screen.findByText("Não informada — operações não são calculadas")).toBeInTheDocument();
    cleanup();

    show({ ...simples, regime: "LucroPresumido", simplesSituation: "NotOptant", ibsCbsRegularRegimeOption: null });
    expect(await screen.findByText("Não optante do Simples", { selector: "dd" })).toBeInTheDocument();
    expect(screen.queryByText("IBS/CBS")).not.toBeInTheDocument();
  });

  it("asks the Simples questions only for Simples regimes, and resets the situation when the regime changes", async () => {
    show();

    await screen.findByText("Registrar uma mudança");
    expect(combo("Situação no Simples")).toHaveTextContent("Simples Nacional dentro do sublimite");
    expect(combo("Opção pelo regime regular do IBS/CBS")).toHaveTextContent("Optou pelo regime regular");

    // Another Simples regime: the situation must be chosen again (it may not fit), the IBS/CBS option is kept.
    await pick("Regime", "MEI");
    expect(combo("Situação no Simples")).toHaveTextContent("Conforme o regime");
    expect(combo("Opção pelo regime regular do IBS/CBS")).toHaveTextContent("Optou pelo regime regular");
    fireEvent.click(combo("Situação no Simples"));
    expect((await screen.findAllByRole("option")).map((o) => o.textContent)).toEqual(["Conforme o regime", "MEI", "SIMEI", "Situação no Simples não informada"]);
    fireEvent.keyDown(screen.getByRole("listbox"), { key: "Escape" });

    // Leaving the Simples: neither question exists, and both answers are dropped.
    await pick("Regime", "Lucro Presumido");
    expect(screen.queryByRole("combobox", { name: "Situação no Simples" })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Opção pelo regime regular do IBS/CBS" })).not.toBeInTheDocument();

    await pick("Regime", "Simples Nacional");
    expect(combo("Situação no Simples")).toHaveTextContent("Conforme o regime");
    expect(combo("Opção pelo regime regular do IBS/CBS")).toHaveTextContent("Não informada");
  });

  it("sends the situation and the IBS/CBS option chosen", async () => {
    vi.mocked(api.addTaxProfileVersion).mockResolvedValue({ ...simples, id: "v4", versionNumber: 4, validFrom: "2026-11-01" });
    show();

    await screen.findByText("Registrar uma mudança");
    fireEvent.change(screen.getByLabelText("Vale a partir de"), { target: { value: "2026-11-01" } });
    await pick("Regime", "Simples Nacional — acima do sublimite");
    await pick("Situação no Simples", "Simples acima do sublimite");
    await pick("Opção pelo regime regular do IBS/CBS", "Recolhe IBS/CBS pelo Simples");
    fireEvent.change(screen.getByLabelText("Motivo da nova versão"), { target: { value: "Faturamento acima do sublimite" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));

    await waitFor(() => expect(api.addTaxProfileVersion).toHaveBeenCalledWith(expect.objectContaining({
      validFrom: "2026-11-01", regime: "SimplesNacionalExcessoSublimite", state: "SP", cityIbgeCode: "3550308", primaryCnae: "4781400",
      secondaryCnaes: ["4782201"], reason: "Faturamento acima do sublimite", simplesSituation: "AboveSublimit", ibsCbsRegularRegimeOption: false,
    })));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Versão 4 registrada, válida a partir de 01/11/2026."));
  });

  it("sends nothing derived when the situation and the option are left open", async () => {
    vi.mocked(api.addTaxProfileVersion).mockResolvedValue(simples);
    show();

    await screen.findByText("Registrar uma mudança");
    await pick("Situação no Simples", "Conforme o regime");
    await pick("Opção pelo regime regular do IBS/CBS", "Não informada");
    fireEvent.change(screen.getByLabelText("Motivo da nova versão"), { target: { value: "Revisão" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));

    await waitFor(() => expect(api.addTaxProfileVersion).toHaveBeenCalledWith(expect.objectContaining({
      regime: "SimplesNacional", simplesSituation: null, ibsCbsRegularRegimeOption: null,
    })));
  });

  it("checks the version before sending and explains a refusal", async () => {
    vi.mocked(api.addTaxProfileVersion).mockRejectedValue(new ApiError(422, "Situação não combina com o regime.", "tax_profile_invalid"));
    const logged = [vi.spyOn(console, "error"), vi.spyOn(console, "warn")];
    show(null);

    await screen.findByText("Cadastrar o perfil");
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));
    expect(screen.getByText("Escolha a UF.")).toBeInTheDocument();
    expect(screen.getByText("Diga o motivo da nova versão.")).toBeInTheDocument();
    expect(api.addTaxProfileVersion).not.toHaveBeenCalled();

    await pick("UF", "RJ");
    fireEvent.change(screen.getByLabelText("Município (código IBGE)"), { target: { value: "3304557" } });
    fireEvent.change(screen.getByLabelText("CNAE principal"), { target: { value: "4781-4/00" } });
    fireEvent.change(screen.getByLabelText("CNAEs secundários"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Regimes especiais"), { target: { value: "Bares e restaurantes" } });
    fireEvent.change(screen.getByLabelText("Benefícios fiscais"), { target: { value: "Benefício A\nBenefício B" } });
    fireEvent.change(screen.getByLabelText("Motivo da nova versão"), { target: { value: "Início" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));

    await waitFor(() => expect(api.addTaxProfileVersion).toHaveBeenCalledWith(expect.objectContaining({
      regime: "SimplesNacional", state: "RJ", primaryCnae: "4781400", specialRegimes: ["Bares e restaurantes"], benefits: ["Benefício A", "Benefício B"],
      simplesSituation: null, ibsCbsRegularRegimeOption: null,
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    // The selects stay controlled from the first render: no "uncontrolled to controlled" warning.
    expect(logged.flatMap((s) => s.mock.calls.flat()).join(" ")).not.toMatch(/uncontrolled|to be controlled|from controlled/i);
    logged.forEach((s) => s.mockRestore());
  });

  it("says why the profile cannot be loaded", async () => {
    vi.mocked(api.getTaxProfile).mockRejectedValue(new ApiError(403, "proibido"));
    renderTax(<TaxProfilePage />);
    expect(await screen.findByText("Seu perfil não pode ver o perfil tributário. Peça acesso à diretoria.")).toBeInTheDocument();
  });
});
