import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { renderTax } from "@/modules/tax/test/render";
import { TaxPilotSection } from "./TaxPilotSection";
import * as api from "../../services/taxAdminApi";
import { fetchPlatformTenants } from "../../services/platformApi";

vi.mock("../../services/taxAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/taxAdminApi")>()),
  getPilotScope: vi.fn(),
  configurePilotScope: vi.fn(),
  addPilotParticipant: vi.fn(),
  removePilotParticipant: vi.fn(),
}));
vi.mock("../../services/platformApi", () => ({ fetchPlatformTenants: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const tenants = [
  { id: "t1", companyName: "Empresa Teste Ltda", tradeName: "Loja Teste", stores: [{ id: "st1", name: "Filial Teste" }] },
  { id: "t2", companyName: "Outra Empresa Teste", stores: [{ id: "st2", name: "Matriz Teste" }] },
];

const scope: api.TaxPilotScopeDto = {
  id: "s1", enabled: true, validFrom: "2026-01-01", validTo: "2026-12-31", decisionReference: "REF-PILOTO-TESTE",
  activatedByName: "Operador Teste", activatedAt: "2026-10-28T10:00:00Z",
  participants: [
    { id: "p1", tenantId: "t1", storeId: null, active: true, addedByName: "Operador Teste", addedAt: "2026-10-28T10:00:00Z",
      removedAt: null, removedByName: null, removalReason: null },
    { id: "p2", tenantId: "t2", storeId: "st2", active: false, addedByName: "Operador Teste", addedAt: "2026-10-28T10:00:00Z",
      removedAt: "2026-10-29T10:00:00Z", removedByName: "Operador Dois", removalReason: "Saiu do recorte de teste" },
  ],
};

describe("TaxPilotSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getPilotScope).mockResolvedValue(scope);
    vi.mocked(fetchPlatformTenants).mockResolvedValue(tenants as never);
  });
  afterEach(cleanup);

  it("shows the scope window, its decision and the participants, active and removed", async () => {
    renderTax(<TaxPilotSection />);

    expect(await screen.findByText(/Escopo ligado · 01\/01\/2026 a 31\/12\/2026 · REF-PILOTO-TESTE · Operador Teste em/)).toBeInTheDocument();
    expect(await screen.findByText("Loja Teste · empresa inteira")).toBeInTheDocument();
    expect(screen.getByText("Outra Empresa Teste · Matriz Teste")).toBeInTheDocument();
    expect(screen.getByText(/Retirado por Operador Dois em .*: Saiu do recorte de teste · incluído por Operador Teste/)).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Retirar" })).toHaveLength(1);
  });

  it("shows a scope not yet configured, with no participants", async () => {
    vi.mocked(api.getPilotScope).mockResolvedValue({
      id: null, enabled: false, validFrom: null, validTo: null, decisionReference: null, activatedByName: null, activatedAt: null, participants: [],
    });
    renderTax(<TaxPilotSection />);

    expect(await screen.findByText("Escopo desligado")).toBeInTheDocument();
    expect(screen.getByText("Nenhuma empresa no piloto.")).toBeInTheDocument();
    expect(screen.getByLabelText("Início")).toHaveValue("");
    expect(screen.getByRole("checkbox")).not.toBeChecked();
  });

  it("falls back to the ids for a company or store the tenant list does not know, and an open-ended window", async () => {
    vi.mocked(api.getPilotScope).mockResolvedValue({
      ...scope, validTo: null, participants: [{ ...scope.participants[0], tenantId: "t9", storeId: "st9" }],
    });
    renderTax(<TaxPilotSection />);

    expect(await screen.findByText(/01\/01\/2026 em diante/)).toBeInTheDocument();
    expect(screen.getByText("t9 · st9")).toBeInTheDocument();
  });

  it("explains a 403 on the scope", async () => {
    vi.mocked(api.getPilotScope).mockRejectedValue(new ApiError(403, "Proibido"));
    renderTax(<TaxPilotSection />);

    expect(await screen.findByText("Sem permissão")).toBeInTheDocument();
    expect(screen.getByText(/Seu perfil não pode ver o escopo do piloto/)).toBeInTheDocument();
  });

  it("asks for a fresh decision reference every time and validates the window before saving the scope", async () => {
    vi.mocked(api.configurePilotScope).mockResolvedValue(scope);
    renderTax(<TaxPilotSection />);
    const reference = await screen.findByLabelText("Referência da decisão");

    expect(reference).toHaveValue(""); // never pre-filled with the previous decision
    expect(screen.getByLabelText("Início")).toHaveValue("2026-01-01");
    expect(screen.getByRole("checkbox")).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Registrar escopo" }));
    expect(screen.getByText("Informe a referência da decisão.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Início"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Fim (opcional)"), { target: { value: "2025-12-31" } });
    expect(screen.getByText("Informe o início.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Início"), { target: { value: "2026-01-01" } });
    expect(screen.getByText("O fim é anterior ao início.")).toBeInTheDocument();
    expect(api.configurePilotScope).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Fim (opcional)"), { target: { value: "" } });
    fireEvent.change(reference, { target: { value: " REF-NOVA-TESTE " } });
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Registrar escopo" }));

    await waitFor(() => expect(api.configurePilotScope).toHaveBeenCalledWith({
      enabled: false, validFrom: "2026-01-01", validTo: null, decisionReference: "REF-NOVA-TESTE",
    }));
    await waitFor(() => expect(reference).toHaveValue(""));
    expect(toast.success).toHaveBeenCalledWith("Escopo do piloto registrado.");
  });

  it("toasts the server's refusal of the scope", async () => {
    vi.mocked(api.configurePilotScope).mockRejectedValue(new ApiError(403, "Só super admin.", "forbidden"));
    renderTax(<TaxPilotSection />);
    fireEvent.change(await screen.findByLabelText("Referência da decisão"), { target: { value: "REF-TESTE" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar escopo" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.any(String), { description: "Só super admin." }));
  });

  it("removes a participant only with a reason", async () => {
    vi.mocked(api.removePilotParticipant).mockResolvedValue(scope);
    renderTax(<TaxPilotSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Retirar" }));

    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("Retirar do piloto")).toBeInTheDocument();
    const confirm = dialog.getByRole("button", { name: "Retirar" });
    expect(confirm).toBeDisabled();
    fireEvent.change(dialog.getByLabelText("Motivo"), { target: { value: "Fim do teste" } });
    fireEvent.click(confirm);

    await waitFor(() => expect(api.removePilotParticipant).toHaveBeenCalledWith("p1", "Fim do teste"));
    expect(toast.success).toHaveBeenCalledWith("Participante retirado do piloto.");
  });

  it("adds a company, whole or one store, only after one is chosen", async () => {
    vi.mocked(api.addPilotParticipant).mockResolvedValue(scope);
    const logged = [vi.spyOn(console, "error"), vi.spyOn(console, "warn")];
    renderTax(<TaxPilotSection />);
    const add = await screen.findByRole("button", { name: "Incluir no piloto" });
    expect(add).toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Loja" })).toBeDisabled();

    fireEvent.click(screen.getByRole("combobox", { name: "Empresa" }));
    fireEvent.click(await screen.findByRole("option", { name: "Loja Teste" }));
    expect(add).toBeEnabled();
    fireEvent.click(screen.getByRole("combobox", { name: "Loja" }));
    fireEvent.click(await screen.findByRole("option", { name: "Filial Teste" }));
    fireEvent.click(add);

    await waitFor(() => expect(api.addPilotParticipant).toHaveBeenCalledWith("t1", "st1"));
    expect(toast.success).toHaveBeenCalledWith("Empresa incluída no piloto.");
    await waitFor(() => expect(add).toBeDisabled()); // form reset

    fireEvent.click(screen.getByRole("combobox", { name: "Empresa" }));
    fireEvent.click(await screen.findByRole("option", { name: "Outra Empresa Teste" }));
    fireEvent.click(add);
    await waitFor(() => expect(api.addPilotParticipant).toHaveBeenLastCalledWith("t2", null));
    // The selects stay controlled from the first render: no "uncontrolled to controlled" warning.
    expect(logged.flatMap((s) => s.mock.calls.flat()).join(" ")).not.toMatch(/uncontrolled|to be controlled|from controlled/i);
    logged.forEach((s) => s.mockRestore());
  });
});
