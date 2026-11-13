import { Children, isValidElement, type ReactElement, type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import FiscalPage from "./FiscalPage";
import {
  getFiscalProfile, getFiscalReadiness, saveFiscalProfile,
  type FiscalProfileDto, type FiscalReadinessDto,
} from "../api/fiscal.api";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("../api/fiscal.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/fiscal.api")>()),
  getFiscalProfile: vi.fn(),
  getFiscalReadiness: vi.fn(),
  saveFiscalProfile: vi.fn(),
}));
// The product dialog has its own queries: here only which product the page asks it to open matters
vi.mock("../components/ProductFiscalDialog", () => ({
  ProductFiscalDialog: ({ productId, open, onOpenChange }: { productId: string | null; open: boolean; onOpenChange: (o: boolean) => void }) =>
    open ? (
      <div role="dialog" aria-label="Dados fiscais do produto">
        produto {productId}
        <button type="button" onClick={() => onOpenChange(true)}>Continuar</button>
        <button type="button" onClick={() => onOpenChange(false)}>Fechar</button>
      </div>
    ) : null,
}));
// Radix Select does not open in jsdom: a native select with the same value/onValueChange contract
vi.mock("@/components/ui/select", () => {
  const SelectTrigger = (_: { id?: string; children?: ReactNode }) => null;
  const SelectValue = (_: { placeholder?: string }) => null;
  const SelectContent = ({ children }: { children: ReactNode }) => <>{children}</>;
  const SelectItem = ({ value, children }: { value: string; children: ReactNode }) => <option value={value}>{children}</option>;
  const Select = ({ value, onValueChange, children }: { value?: string; onValueChange: (v: string) => void; children: ReactNode }) => {
    const trigger = Children.toArray(children).find(
      (c): c is ReactElement<{ id?: string }> => isValidElement(c) && c.type === SelectTrigger,
    );
    return (
      <select id={trigger?.props.id} value={value ?? ""} onChange={(e) => onValueChange(e.target.value)}>
        <option value="">Escolha</option>
        {children}
      </select>
    );
  };
  return { Select, SelectTrigger, SelectValue, SelectContent, SelectItem };
});

const profile: FiscalProfileDto = {
  legalName: "Loja Exemplo Ltda", tradeName: "Loja Exemplo", cnpj: "11.222.333/0001-81", stateRegistration: "110.042.490.114",
  municipalRegistration: null, taxRegime: "SimplesNacional", street: "Rua Augusta", number: "100", complement: null,
  district: "Consolação", cityName: "São Paulo", cityIbgeCode: "3550308", state: "SP", zipCode: "01305-000",
  operationNature: "Venda de mercadoria", nfceSeries: 1, nfceNextNumber: 120, nfeSeries: null, nfeNextNumber: null,
  crt: 1, updatedAt: "2026-11-01T10:00:00Z",
};

const readiness = (over: Partial<FiscalReadinessDto> = {}): FiscalReadinessDto => ({
  profileComplete: true, profileMissing: [], activeProducts: 40, productsWithoutNcm: 0, productsWithInvalidBarcode: 0,
  productsToFix: [], emissionAvailable: false, provider: "none", ...over,
});

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const metric = (label: string) => screen.getByText(label).closest("div.rounded-md") as HTMLElement;
const field = (label: string) => screen.getByLabelText(label) as HTMLInputElement;
const errors = () => screen.queryAllByRole("listitem").map((li) => li.textContent);

beforeEach(() => {
  vi.mocked(getFiscalProfile).mockReset().mockResolvedValue(profile);
  vi.mocked(getFiscalReadiness).mockReset().mockResolvedValue(readiness());
  vi.mocked(saveFiscalProfile).mockReset().mockResolvedValue(profile);
  vi.mocked(toast.error).mockClear();
  vi.mocked(toast.success).mockClear();
});
afterEach(cleanup);

describe("FiscalPage readiness", () => {
  it("shows what is ready and says no product is pending", async () => {
    render(<FiscalPage />, { wrapper });
    expect(await screen.findByText("Preenchido")).toBeInTheDocument();
    expect(metric("Emissor").className).toContain("border-success");
    expect(metric("Produtos sem NCM").textContent).toContain("0");
    expect(metric("Produtos sem NCM").textContent).toContain("de 40 produtos à venda");
    expect(metric("Produtos sem NCM").className).toContain("border-success");
    expect(screen.getByText("Nenhum produto pendente.")).toBeInTheDocument();
    expect(screen.getByText(/nada é enviado à Sefaz/)).toBeInTheDocument();
  });

  it("lists the products to fix and opens the fiscal dialog of the one chosen", async () => {
    vi.mocked(getFiscalReadiness).mockResolvedValue(readiness({
      profileComplete: false, productsWithoutNcm: 2, productsWithInvalidBarcode: 1,
      productsToFix: [
        { productId: "p1", code: "0001", name: "Café torrado", barcode: null, ncm: null, problems: ["Sem NCM"] },
        { productId: "p2", code: "0002", name: "Açúcar", barcode: "123", ncm: null, problems: ["Sem NCM", "GTIN inválido"] },
      ],
    }));
    render(<FiscalPage />, { wrapper });

    expect(await screen.findByText("Falta preencher")).toBeInTheDocument();
    expect(metric("Emissor").className).toContain("border-warning");
    expect(metric("Produtos sem NCM").className).toContain("border-warning");
    expect(metric("Código de barras fora do padrão").textContent).toContain("1");

    const row = screen.getByRole("button", { name: "Corrigir Açúcar" }).closest("tr") as HTMLElement;
    expect(within(row).getByText("0002")).toBeInTheDocument();
    expect(within(row).getByText("Sem NCM · GTIN inválido")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    fireEvent.click(within(row).getByRole("button", { name: "Corrigir Açúcar" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("produto p2");
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Fechar" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("a failed readiness check can be retried", async () => {
    vi.mocked(getFiscalReadiness).mockRejectedValueOnce(new Error("500"));
    render(<FiscalPage />, { wrapper });
    const alert = (await screen.findByText("Não foi possível conferir a preparação fiscal")).closest("[role=alert]") as HTMLElement;
    fireEvent.click(within(alert).getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Preenchido")).toBeInTheDocument();
    expect(getFiscalReadiness).toHaveBeenCalledTimes(2);
  });
});

describe("FiscalPage issuer profile", () => {
  it("fills the form with the saved profile and saves it unchanged", async () => {
    render(<FiscalPage />, { wrapper });
    await waitFor(() => expect(field("Razão social").value).toBe("Loja Exemplo Ltda"));
    expect(field("CNPJ").value).toBe("11.222.333/0001-81");
    expect(field("Inscrição municipal").value).toBe("");
    expect((screen.getByLabelText("UF") as HTMLSelectElement).value).toBe("SP");
    expect((screen.getByLabelText("Regime tributário") as HTMLSelectElement).value).toBe("SimplesNacional");
    expect(field("Série da NFC-e").value).toBe("1");
    expect(field("Série da NF-e").value).toBe("");

    fireEvent.click(screen.getByRole("button", { name: "Salvar dados do emissor" }));
    await waitFor(() => expect(saveFiscalProfile).toHaveBeenCalledTimes(1));
    const { crt: _crt, updatedAt: _updatedAt, ...input } = profile;
    expect(vi.mocked(saveFiscalProfile).mock.calls[0][0]).toEqual(input);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Dados do emissor salvos."));
    // the readiness is read again after saving
    await waitFor(() => expect(getFiscalReadiness).toHaveBeenCalledTimes(2));
  });

  it("sends what was typed: text, regime, state and numbering, empty numbers as null", async () => {
    render(<FiscalPage />, { wrapper });
    await waitFor(() => expect(field("Razão social").value).toBe("Loja Exemplo Ltda"));

    fireEvent.change(field("Nome fantasia"), { target: { value: "" } });
    fireEvent.change(field("Complemento"), { target: { value: "Loja 2" } });
    fireEvent.change(screen.getByLabelText("Regime tributário"), { target: { value: "LucroPresumido" } });
    fireEvent.change(screen.getByLabelText("UF"), { target: { value: "RJ" } });
    fireEvent.change(field("Código IBGE do município"), { target: { value: "3304557" } });
    fireEvent.change(field("Série da NFC-e"), { target: { value: "" } });
    fireEvent.change(field("Próximo número da NF-e"), { target: { value: "15" } });
    fireEvent.change(field("Série da NF-e"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar dados do emissor" }));

    await waitFor(() => expect(saveFiscalProfile).toHaveBeenCalledTimes(1));
    expect(vi.mocked(saveFiscalProfile).mock.calls[0][0]).toMatchObject({
      tradeName: "", complement: "Loja 2", taxRegime: "LucroPresumido", state: "RJ", cityIbgeCode: "3304557",
      nfceSeries: null, nfceNextNumber: 120, nfeSeries: 2, nfeNextNumber: 15,
    });
  });

  it("a store without a profile starts empty and lists every problem only after trying to save", async () => {
    vi.mocked(getFiscalProfile).mockResolvedValue(null);
    render(<FiscalPage />, { wrapper });
    await screen.findByRole("button", { name: "Salvar dados do emissor" });
    expect(field("Natureza da operação").value).toBe("Venda de mercadoria");
    expect(errors()).toEqual([]);

    fireEvent.click(screen.getByRole("button", { name: "Salvar dados do emissor" }));
    expect(errors()).toEqual([
      "Informe a razão social.",
      "CNPJ inválido.",
      "Inscrição estadual: números ou ISENTO.",
      "Complete o endereço.",
      "Escolha a UF.",
      "Código IBGE do município: 7 números.",
      "CEP: 8 números.",
    ]);
    expect(saveFiscalProfile).not.toHaveBeenCalled();
  });

  it("refuses numbering out of range and an IBGE code of another state", async () => {
    render(<FiscalPage />, { wrapper });
    await waitFor(() => expect(field("Razão social").value).toBe("Loja Exemplo Ltda"));
    fireEvent.change(field("Série da NFC-e"), { target: { value: "900" } });
    fireEvent.change(field("Próximo número da NFC-e"), { target: { value: "0" } });
    fireEvent.change(field("Código IBGE do município"), { target: { value: "3304557" } });
    // the number inputs carry min/max too, so the browser already blocks the button; the page checks the same on submit
    const save = screen.getByRole("button", { name: "Salvar dados do emissor" });
    fireEvent.click(save);
    expect(saveFiscalProfile).not.toHaveBeenCalled();
    fireEvent.submit(save.closest("form") as HTMLFormElement);

    expect(errors()).toEqual([
      "O código IBGE não é de um município dessa UF.",
      "Série: de 0 a 889.",
      "Próximo número: de 1 a 999.999.999.",
    ]);
    expect(saveFiscalProfile).not.toHaveBeenCalled();

    // fixing the fields clears the list and saves
    fireEvent.change(field("Série da NFC-e"), { target: { value: "1" } });
    fireEvent.change(field("Próximo número da NFC-e"), { target: { value: "121" } });
    fireEvent.change(field("Código IBGE do município"), { target: { value: "3550308" } });
    expect(errors()).toEqual([]);
    fireEvent.click(screen.getByRole("button", { name: "Salvar dados do emissor" }));
    await waitFor(() => expect(saveFiscalProfile).toHaveBeenCalledTimes(1));
  });

  it("shows the server refusal and keeps the form", async () => {
    let fail!: (e: Error) => void;
    vi.mocked(saveFiscalProfile).mockImplementationOnce(() => new Promise((_, reject) => (fail = reject)));
    render(<FiscalPage />, { wrapper });
    await waitFor(() => expect(field("Razão social").value).toBe("Loja Exemplo Ltda"));
    fireEvent.change(field("Razão social"), { target: { value: "Outra Razão Ltda" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar dados do emissor" }));

    expect(await screen.findByRole("button", { name: "Salvando…" })).toBeDisabled();
    fail(new Error("CNPJ já usado por outra loja."));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("CNPJ já usado por outra loja."));
    expect(field("Razão social").value).toBe("Outra Razão Ltda");
    expect(screen.getByRole("button", { name: "Salvar dados do emissor" })).toBeEnabled();
  });

  it("a failed profile read can be retried", async () => {
    vi.mocked(getFiscalProfile).mockRejectedValueOnce(new Error("500"));
    render(<FiscalPage />, { wrapper });
    const alert = (await screen.findByText("Não foi possível ler os dados do emissor")).closest("[role=alert]") as HTMLElement;
    fireEvent.click(within(alert).getByRole("button", { name: /Tentar novamente/ }));
    await waitFor(() => expect(field("Razão social").value).toBe("Loja Exemplo Ltda"));
  });
});
