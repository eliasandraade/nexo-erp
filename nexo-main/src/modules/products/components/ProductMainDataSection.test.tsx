import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import { polyfillRadix } from "@/modules/platform/test/aiFixtures";
import { ProductMainDataSection } from "./ProductMainDataSection";
import type { CategoryDto, Product } from "../types";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
// the upload itself is covered by ImageUploadButton's own tests; here only what the section does with the result
vi.mock("@/components/shared/ImageUploadButton", () => ({
  ImageUploadButton: ({ onChange }: { onChange: (url: string | null) => void }) => (
    <button type="button" onClick={() => onChange("https://cdn.orken.test/cafe.png")}>Enviar imagem</button>
  ),
}));

polyfillRadix();

const get = vi.mocked(apiClient.get);
const patch = vi.mocked(apiClient.patch);

const categories = [{ id: "cat-bebidas", name: "Bebidas" }, { id: "cat-mercearia", name: "Mercearia" }] as CategoryDto[];
const EAN = "7891000100103";
const found = {
  found: true,
  data: {
    barcode: EAN, name: "Café Torrado e Moído 500g", brand: "Grão de Ouro", category: "Cafés",
    quantity: "500", unit: "g", imageUrl: "https://img.test/cafe.png", sourceProvider: "Cosmos", confidence: 0.87,
  },
};

let onChange: ReturnType<typeof vi.fn>;
function renderSection(data: Partial<Product> = {}) {
  onChange = vi.fn();
  return render(<ProductMainDataSection data={data} onChange={onChange} categories={categories} />);
}
const barcodeInput = () => screen.getByPlaceholderText("EAN-13");

beforeEach(() => {
  get.mockImplementation(async (url: string) => {
    if (url === `/integrations/barcode/${EAN}`) return structuredClone(found);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("ProductMainDataSection — barcode lookup", () => {
  it("pressing Enter on the barcode looks it up and shows the suggestion to confirm", async () => {
    renderSection();

    fireEvent.change(barcodeInput(), { target: { value: EAN } });
    expect(onChange).toHaveBeenCalledWith("barcode", EAN);
    fireEvent.keyDown(barcodeInput(), { key: "Enter" });

    expect(await screen.findByText("Produto encontrado — confirme os dados antes de aplicar")).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith(`/integrations/barcode/${EAN}`);
    expect(screen.getByText("Café Torrado e Moído 500g")).toBeInTheDocument();
    expect(screen.getByText("Grão de Ouro")).toBeInTheDocument();
    expect(screen.getByText("Cafés")).toBeInTheDocument();
    expect(screen.getByText("500 g")).toBeInTheDocument();
    expect(screen.getByText(/Fonte: Cosmos • confiança 87%/)).toBeInTheDocument();
  });

  it("other keys do not trigger a lookup", () => {
    renderSection();
    fireEvent.change(barcodeInput(), { target: { value: EAN } });

    fireEvent.keyDown(barcodeInput(), { key: "Tab" });

    expect(get).not.toHaveBeenCalled();
  });

  it("applying the suggestion fills an empty name", async () => {
    renderSection({ barcode: EAN });

    fireEvent.click(screen.getByRole("button", { name: /Buscar por código/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Aplicar sugestão" }));

    expect(onChange).toHaveBeenCalledWith("name", "Café Torrado e Moído 500g");
    expect(toast.success).toHaveBeenCalledWith("Sugestão aplicada.");
    expect(screen.queryByText("Produto encontrado — confirme os dados antes de aplicar")).toBeNull();
  });

  it("applying never overwrites a name the operator already typed", async () => {
    renderSection({ barcode: EAN, name: "Café da Casa 500g" });

    fireEvent.click(screen.getByRole("button", { name: /Buscar por código/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Aplicar sugestão" }));

    expect(onChange).not.toHaveBeenCalledWith("name", expect.anything());
  });

  it("ignoring the suggestion dismisses it without changing the product", async () => {
    renderSection({ barcode: EAN });

    fireEvent.keyDown(barcodeInput(), { key: "Enter" });
    fireEvent.click(await screen.findByRole("button", { name: "Ignorar" }));

    expect(screen.queryByText("Produto encontrado — confirme os dados antes de aplicar")).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("refuses a barcode outside 8 to 14 digits without asking the server", () => {
    renderSection();
    fireEvent.change(barcodeInput(), { target: { value: "12345" } });

    fireEvent.keyDown(barcodeInput(), { key: "Enter" });

    expect(toast.error).toHaveBeenCalledWith("Código de barras inválido. Use entre 8 e 14 dígitos.");
    expect(get).not.toHaveBeenCalled();
  });

  it("says when the product is not in the base, so it is registered by hand", async () => {
    get.mockResolvedValueOnce({ found: false, data: null });
    renderSection({ barcode: "78900000" });

    fireEvent.keyDown(barcodeInput(), { key: "Enter" });

    expect(await screen.findByText("Produto não encontrado nessa base. Cadastre manualmente.")).toBeInTheDocument();
  });

  it("says when the lookup service is unavailable or fails", async () => {
    get.mockResolvedValueOnce({ found: false, data: null, unavailable: true });
    renderSection({ barcode: EAN });

    fireEvent.keyDown(barcodeInput(), { key: "Enter" });
    const message = "Consulta indisponível no momento. Cadastre manualmente e tente novamente depois.";
    expect(await screen.findByText(message)).toBeInTheDocument();

    // typing a new code clears the old answer; a network failure says the same
    fireEvent.change(barcodeInput(), { target: { value: "7891000100110" } });
    expect(screen.queryByText(message)).toBeNull();
    get.mockRejectedValueOnce(new Error("offline"));
    fireEvent.keyDown(barcodeInput(), { key: "Enter" });
    expect(await screen.findByText(message)).toBeInTheDocument();
  });
});

describe("ProductMainDataSection — main data", () => {
  it("reports each field the operator edits", () => {
    renderSection({ code: "PRD-001", name: "Café", trackStock: true, isActive: true });

    fireEvent.change(screen.getByPlaceholderText("PRD-000"), { target: { value: "PRD-002" } });
    fireEvent.change(screen.getByPlaceholderText("Nome do produto"), { target: { value: "Café 1kg" } });
    fireEvent.change(screen.getByPlaceholderText("Informações complementares sobre o produto"), { target: { value: "Torra média" } });

    expect(onChange).toHaveBeenCalledWith("code", "PRD-002");
    expect(onChange).toHaveBeenCalledWith("name", "Café 1kg");
    expect(onChange).toHaveBeenCalledWith("description", "Torra média");
    expect(screen.getByText("Ativo")).toBeInTheDocument();
  });

  it("choosing 'Sem categoria' clears the category", async () => {
    renderSection({ categoryId: "cat-bebidas" });

    const [categoryTrigger] = screen.getAllByRole("combobox");
    expect(categoryTrigger).toHaveTextContent("Bebidas");
    fireEvent.keyDown(categoryTrigger, { key: "Enter" });
    fireEvent.keyDown(await screen.findByRole("option", { name: "Sem categoria" }), { key: "Enter" });

    expect(onChange).toHaveBeenCalledWith("categoryId", null);
  });

  it("turning stock control off is reported", () => {
    renderSection({ trackStock: true, isActive: true });

    const [trackStock] = screen.getAllByRole("switch");
    fireEvent.click(trackStock);

    expect(onChange).toHaveBeenCalledWith("trackStock", false);
  });

  it("a new product has no image yet: it is uploaded after the first save", () => {
    renderSection({ name: "Novo" });

    expect(screen.queryByText("Imagem do produto")).toBeNull();
  });

  it("a saved product's new image is stored on the product at once", async () => {
    patch.mockResolvedValueOnce({ id: "p1" });
    renderSection({ id: "p1", name: "Café" });

    fireEvent.click(screen.getByRole("button", { name: "Enviar imagem" }));

    expect(onChange).toHaveBeenCalledWith("imageUrl", "https://cdn.orken.test/cafe.png");
    await waitFor(() => expect(patch).toHaveBeenCalledWith("/products/p1/image", { imageUrl: "https://cdn.orken.test/cafe.png" }));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("tells the operator when the image could not be stored", async () => {
    patch.mockRejectedValueOnce(new Error("413"));
    renderSection({ id: "p1", name: "Café" });

    fireEvent.click(screen.getByRole("button", { name: "Enviar imagem" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Falha ao salvar imagem. Tente novamente."));
  });
});
