import { Children, isValidElement, type ReactElement, type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import AdicionaisPage from "./AdicionaisPage";
import { useProducts } from "@/modules/products/hooks/use-products";
import {
  addModifier, createModifierGroup, getModifierGroups, removeModifier, removeModifierGroup, updateModifierGroup,
} from "../api/restaurante.api";
import type { ModifierGroupDto } from "../types";
import { formatCurrency } from "@/lib/formatters";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/modules/products/hooks/use-products", () => ({ useProducts: vi.fn() }));
vi.mock("../api/restaurante.api", () => ({
  getModifierGroups: vi.fn(),
  createModifierGroup: vi.fn(),
  updateModifierGroup: vi.fn(),
  removeModifierGroup: vi.fn(),
  addModifier: vi.fn(),
  removeModifier: vi.fn(),
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

const products = [
  { id: "burger", name: "Hambúrguer artesanal", isActive: true },
  { id: "old", name: "Lanche retirado", isActive: false },
  { id: "fries", name: "Batata frita", isActive: true },
];

const group = (over: Partial<ModifierGroupDto> = {}): ModifierGroupDto => ({
  id: "g1",
  productId: "burger",
  name: "Ponto da carne",
  isRequired: true,
  minSelections: 0,
  maxSelections: 1,
  sortOrder: 0,
  isActive: true,
  modifiers: [
    { id: "m1", name: "Mal passado", priceAdjustment: 0, sortOrder: 0, isActive: true },
    { id: "m2", name: "Bacon extra", priceAdjustment: 2.5, sortOrder: 1, isActive: true },
    { id: "m3", name: "Sem queijo", priceAdjustment: -1, sortOrder: 2, isActive: true },
    { id: "m4", name: "Opção antiga", priceAdjustment: 9, sortOrder: 3, isActive: false },
  ],
  ...over,
});

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const money = (n: number) => formatCurrency(n).replace(/\u00a0/g, " ");
const card = (title: string) => screen.getByRole("heading", { name: title }).closest(".data-region") as HTMLElement;

async function openProduct(id = "burger") {
  render(<AdicionaisPage />, { wrapper });
  fireEvent.change(screen.getByLabelText("Produto"), { target: { value: id } });
}

beforeEach(() => {
  vi.mocked(useProducts).mockReturnValue({ data: products, isLoading: false } as unknown as ReturnType<typeof useProducts>);
  vi.mocked(getModifierGroups).mockReset().mockResolvedValue([group()]);
  for (const fn of [createModifierGroup, updateModifierGroup, removeModifierGroup, addModifier, removeModifier])
    vi.mocked(fn).mockReset().mockResolvedValue(undefined as never);
  vi.mocked(toast.error).mockClear();
  vi.mocked(toast.success).mockClear();
});
afterEach(cleanup);

describe("AdicionaisPage", () => {
  it("waits for the products and then offers only the active ones of the menu", () => {
    vi.mocked(useProducts).mockReturnValueOnce({ data: undefined, isLoading: true } as unknown as ReturnType<typeof useProducts>);
    const { rerender } = render(<AdicionaisPage />, { wrapper });
    expect(screen.queryByLabelText("Produto")).not.toBeInTheDocument();
    expect(useProducts).toHaveBeenCalledWith({ isIngredient: false });

    rerender(<AdicionaisPage />);
    const options = within(screen.getByLabelText("Produto")).getAllByRole("option").map((o) => o.textContent);
    expect(options).toEqual(["Escolha", "Hambúrguer artesanal", "Batata frita"]);
    expect(getModifierGroups).not.toHaveBeenCalled();
    expect(screen.queryByText("Novo grupo")).not.toBeInTheDocument();
  });

  it("a product without groups says so and offers to create one", async () => {
    vi.mocked(getModifierGroups).mockResolvedValue([]);
    await openProduct("fries");
    expect(await screen.findByText("Sem adicionais")).toBeInTheDocument();
    expect(getModifierGroups).toHaveBeenCalledWith("fries");
    expect(screen.getByText("Novo grupo")).toBeInTheDocument();
  });

  it("shows each active option with its price and the rule of a required group", async () => {
    await openProduct();
    const g = await screen.findByRole("heading", { name: "Ponto da carne" });
    const c = g.closest(".data-region") as HTMLElement;
    const rows = within(c).getAllByRole("listitem").map((li) => (li.textContent ?? "").replace(/\u00a0/g, " "));
    expect(rows).toEqual([
      "Mal passadosem custo",
      `Bacon extra+${money(2.5)}`,
      `Sem queijo${money(-1)}`,
    ]);
    expect(within(c).getByText("O cliente escolhe ao menos 1")).toBeInTheDocument();
    expect(within(c).queryByRole("button", { name: "Salvar regras" })).not.toBeInTheDocument();
  });

  it("an optional group has no rule badge", async () => {
    vi.mocked(getModifierGroups).mockResolvedValue([group({ isRequired: false, name: "Extras" })]);
    await openProduct();
    const c = (await screen.findByRole("heading", { name: "Extras" })).closest(".data-region") as HTMLElement;
    expect(within(c).queryByText(/O cliente escolhe/)).not.toBeInTheDocument();
  });

  it.each([
    ["2,50", 2.5],
    ["2.50", 2.5],
    ["1.000", 1000],
    ["1.234,56", 1234.56],
    ["-3", -3],
    ["0,335", 0.34],
    ["", 0],
  ])("adds an option priced %s as %s, after the last one, and clears the fields", async (typed, expected) => {
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    const c = card("Ponto da carne");
    fireEvent.change(within(c).getByLabelText("Nova opção"), { target: { value: "  Cheddar  " } });
    fireEvent.change(within(c).getByLabelText("Preço"), { target: { value: typed } });
    fireEvent.click(within(c).getByRole("button", { name: "Adicionar opção em Ponto da carne" }));

    await waitFor(() => expect(addModifier).toHaveBeenCalledWith("g1", "Cheddar", expected, 4));
    await waitFor(() => expect((within(c).getByLabelText("Nova opção") as HTMLInputElement).value).toBe(""));
    expect((within(c).getByLabelText("Preço") as HTMLInputElement).value).toBe("");
    await waitFor(() => expect(getModifierGroups).toHaveBeenCalledTimes(2));
  });

  it("does not add an option without a name or with an unreadable price", async () => {
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    const c = card("Ponto da carne");
    const add = within(c).getByRole("button", { name: "Adicionar opção em Ponto da carne" });
    expect(add).toBeDisabled();
    fireEvent.change(within(c).getByLabelText("Nova opção"), { target: { value: "   " } });
    expect(add).toBeDisabled();
    fireEvent.change(within(c).getByLabelText("Nova opção"), { target: { value: "Cebola" } });
    fireEvent.change(within(c).getByLabelText("Preço"), { target: { value: "dois reais" } });
    expect(add).toBeDisabled();
    fireEvent.change(within(c).getByLabelText("Preço"), { target: { value: "2" } });
    expect(add).toBeEnabled();
  });

  it("reports a failed option with the server message or a default one", async () => {
    vi.mocked(addModifier).mockRejectedValueOnce(new Error("Nome repetido no grupo.")).mockRejectedValueOnce(new Error(""));
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    const c = card("Ponto da carne");
    fireEvent.change(within(c).getByLabelText("Nova opção"), { target: { value: "Bacon extra" } });
    fireEvent.click(within(c).getByRole("button", { name: "Adicionar opção em Ponto da carne" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Nome repetido no grupo."));
    expect((within(c).getByLabelText("Nova opção") as HTMLInputElement).value).toBe("Bacon extra");

    fireEvent.click(within(c).getByRole("button", { name: "Adicionar opção em Ponto da carne" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível salvar."));
  });

  it("removes an option of the group", async () => {
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    fireEvent.click(screen.getByRole("button", { name: "Remover Bacon extra" }));
    await waitFor(() => expect(removeModifier).toHaveBeenCalledWith("g1", "m2"));
    await waitFor(() => expect(getModifierGroups).toHaveBeenCalledTimes(2));
  });

  it("saves new selection rules only when they changed and are valid", async () => {
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    const c = card("Ponto da carne");
    const min = within(c).getByLabelText("Mínimo");
    const max = within(c).getByLabelText("Máximo");

    fireEvent.change(max, { target: { value: "3" } });
    const save = within(c).getByRole("button", { name: "Salvar regras" });
    expect(save).toBeEnabled();

    fireEvent.change(min, { target: { value: "4" } });
    expect(save).toBeDisabled();
    fireEvent.change(min, { target: { value: "um" } });
    expect(save).toBeDisabled();
    fireEvent.change(min, { target: { value: "1" } });
    fireEvent.change(max, { target: { value: "0" } });
    expect(save).toBeDisabled();
    fireEvent.change(max, { target: { value: "2" } });
    fireEvent.click(within(c).getByRole("switch"));
    fireEvent.click(save);

    await waitFor(() => expect(updateModifierGroup).toHaveBeenCalledWith("g1", {
      name: "Ponto da carne", isRequired: false, minSelections: 1, maxSelections: 2, sortOrder: 0,
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Grupo salvo."));

    // back to the saved values: nothing to save
    fireEvent.change(min, { target: { value: "0" } });
    fireEvent.change(max, { target: { value: "1" } });
    fireEvent.click(within(c).getByRole("switch"));
    expect(within(c).queryByRole("button", { name: "Salvar regras" })).not.toBeInTheDocument();
  });

  it("removing a group needs a second click, and leaving the button cancels it", async () => {
    await openProduct();
    await screen.findByRole("heading", { name: "Ponto da carne" });
    fireEvent.click(screen.getByRole("button", { name: "Remover grupo Ponto da carne" }));
    const confirm = screen.getByRole("button", { name: "Confirmar remoção do grupo Ponto da carne" });
    expect(confirm).toHaveTextContent("Confirmar remoção");
    fireEvent.blur(confirm);
    expect(screen.getByRole("button", { name: "Remover grupo Ponto da carne" })).toBeInTheDocument();
    expect(removeModifierGroup).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Remover grupo Ponto da carne" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirmar remoção do grupo Ponto da carne" }));
    await waitFor(() => expect(removeModifierGroup).toHaveBeenCalledWith("g1"));
    await waitFor(() => expect(getModifierGroups).toHaveBeenCalledTimes(2));
  });

  it("creates an optional group and a required one that asks at least one choice", async () => {
    vi.mocked(getModifierGroups).mockResolvedValue([]);
    await openProduct();
    await screen.findByText("Sem adicionais");
    const c = card("Novo grupo");
    const create = within(c).getByRole("button", { name: /Criar grupo/ });
    expect(create).toBeDisabled();

    fireEvent.change(within(c).getByLabelText("Nome do grupo"), { target: { value: "  Acompanhamentos " } });
    fireEvent.click(create);
    await waitFor(() => expect(createModifierGroup).toHaveBeenCalledWith("burger", {
      name: "Acompanhamentos", isRequired: false, minSelections: 0, maxSelections: 1, sortOrder: 0,
    }));
    await waitFor(() => expect((within(c).getByLabelText("Nome do grupo") as HTMLInputElement).value).toBe(""));

    fireEvent.change(within(c).getByLabelText("Nome do grupo"), { target: { value: "Ponto" } });
    fireEvent.click(within(c).getByRole("switch"));
    fireEvent.click(create);
    await waitFor(() => expect(createModifierGroup).toHaveBeenLastCalledWith("burger", {
      name: "Ponto", isRequired: true, minSelections: 1, maxSelections: 1, sortOrder: 0,
    }));
    // after creating, the switch goes back to optional
    await waitFor(() => expect(within(c).getByRole("switch")).toHaveAttribute("aria-checked", "false"));
  });

  it("a refused group keeps what was typed and shows why", async () => {
    vi.mocked(getModifierGroups).mockResolvedValue([]);
    vi.mocked(createModifierGroup).mockRejectedValueOnce(new Error("Já existe um grupo com esse nome.")).mockRejectedValueOnce(new Error(""));
    await openProduct();
    await screen.findByText("Sem adicionais");
    const c = card("Novo grupo");
    fireEvent.change(within(c).getByLabelText("Nome do grupo"), { target: { value: "Extras" } });
    fireEvent.click(within(c).getByRole("button", { name: /Criar grupo/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe um grupo com esse nome."));
    expect((within(c).getByLabelText("Nome do grupo") as HTMLInputElement).value).toBe("Extras");
    fireEvent.click(within(c).getByRole("button", { name: /Criar grupo/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível criar o grupo."));
  });
});
