import { type ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { createPortalOrder, getDeliveryZones } from "../api/portal.api";
import type { CartItem } from "../types";
import { CartSheet } from "./CartSheet";

vi.mock("../api/portal.api", () => ({
  createPortalOrder: vi.fn(),
  getDeliveryZones: vi.fn(),
  validateCoupon: vi.fn(),
}));

const items: CartItem[] = [
  { productId: "baiao", productName: "Baião de dois", price: 25, quantity: 2, notes: "", modifiers: [] },
];

const viaCep = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });

let fetchMock: ReturnType<typeof vi.fn>;

async function renderCart() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/cardapio/bar-do-ze"]}>
        <Routes>
          <Route path="/cardapio/:slug" element={children} />
          <Route path="/rastrear/:token" element={<p>Acompanhe seu pedido</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
  const view = render(
    <CartSheet open onClose={vi.fn()} items={items} onChangeQty={vi.fn()} onRemove={vi.fn()}
      slug="bar-do-ze" deliveryEnabled takeawayEnabled />,
    { wrapper },
  );
  // A busca do CEP casa o bairro com as zonas já carregadas.
  await waitFor(() => expect(getDeliveryZones).toHaveBeenCalledWith("bar-do-ze"));
  await act(async () => undefined);
  return view;
}

const typeCep = (value: string) => fireEvent.change(screen.getByPlaceholderText("CEP *"), { target: { value } });

beforeEach(() => {
  vi.clearAllMocks();
  fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
  vi.mocked(getDeliveryZones).mockResolvedValue([{ id: "z-aldeota", neighborhood: "Aldeota", fee: 7.5 }]);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("CartSheet delivery address", () => {
  it("a full CEP finds the neighborhood's zone and the order goes with its fee and the full address", async () => {
    fetchMock.mockResolvedValue(viaCep({ logradouro: "Rua Silva Paulet", bairro: "Aldeota", localidade: "Fortaleza", uf: "CE" }));
    vi.mocked(createPortalOrder).mockResolvedValue({ trackingToken: "tk-87" } as Awaited<ReturnType<typeof createPortalOrder>>);
    await renderCart();

    fireEvent.change(screen.getByPlaceholderText("Seu nome *"), { target: { value: "Maria" } });
    fireEvent.change(screen.getByPlaceholderText("Telefone / WhatsApp *"), { target: { value: "85999990000" } });
    typeCep("60160-230");

    expect(await screen.findByText("Entrega disponível — Taxa: R$ 7.50")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("https://viacep.com.br/ws/60160230/json/");
    fireEvent.change(screen.getByPlaceholderText("Número e complemento *"), { target: { value: "Nº 100, ap 302" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirmar pedido · R$ 57.50" }));

    expect(await screen.findByText("Acompanhe seu pedido")).toBeInTheDocument();
    expect(createPortalOrder).toHaveBeenCalledWith(expect.objectContaining({
      publicSlug: "bar-do-ze",
      orderType: "Delivery",
      customerName: "Maria",
      deliveryZoneId: "z-aldeota",
      deliveryAddressJson: JSON.stringify({ address: "Rua Silva Paulet, Nº 100, ap 302, Aldeota, Fortaleza, CE", cep: "60160230" }),
    }));
  });

  it("an incomplete CEP asks nothing yet", async () => {
    await renderCart();

    typeCep("60160-23");

    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /confirmar pedido/i })).toBeDisabled();
  });

  it("a neighborhood outside the delivery area is named and the order cannot be sent", async () => {
    fetchMock.mockResolvedValue(viaCep({ logradouro: "Rua A", bairro: "Messejana", localidade: "Fortaleza", uf: "CE" }));
    await renderCart();

    fireEvent.change(screen.getByPlaceholderText("Seu nome *"), { target: { value: "Maria" } });
    fireEvent.change(screen.getByPlaceholderText("Telefone / WhatsApp *"), { target: { value: "85999990000" } });
    fireEvent.change(screen.getByPlaceholderText("Número e complemento *"), { target: { value: "Nº 10" } });
    typeCep("60871-000");

    expect(await screen.findByText('Bairro "Messejana" não está na área de entrega.')).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirmar pedido/i })).toBeDisabled();
  });

  it("a CEP lookup that fails says so instead of breaking the cart", async () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    await renderCart();

    typeCep("60160230");

    expect(await screen.findByText("Erro ao buscar CEP. Tente novamente.")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("CEP *")).toHaveValue("60160230");
  });
});
