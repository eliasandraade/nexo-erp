import { type ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider, useQueries } from "@tanstack/react-query";
import { toast } from "sonner";
import { beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import * as api from "../api/restaurante.api";
import type { DeliveryOrderDto, OrderDto } from "../types";
import { ACTIVE_ORDER_KEY, ORDERS_KEY } from "./useActiveOrder";
import { DELIVERY_KEY } from "./useDeliveryOrders";
import {
  useAcceptDelivery,
  useAdvanceDelivery,
  useCancelDelivery,
  useCreateManualDelivery,
  useRejectDelivery,
} from "./useDeliveryMutations";
import { KITCHEN_KEY } from "./useKitchenItems";
import {
  useAddItem,
  useCancelOrder,
  useCancelOrderItem,
  useCloseOrder,
  useOpenOrder,
  usePayOrder,
  useUpdateItemStatus,
} from "./useOrderMutations";
import { TABLES_KEY } from "./useRestauranteTables";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../api/restaurante.api", () => ({
  openOrder: vi.fn(),
  addOrderItem: vi.fn(),
  updateItemStatus: vi.fn(),
  closeOrder: vi.fn(),
  cancelOrderItem: vi.fn(),
  payOrder: vi.fn(),
  cancelOrder: vi.fn(),
  acceptDeliveryOrder: vi.fn(),
  rejectDeliveryOrder: vi.fn(),
  updateDeliveryStatus: vi.fn(),
  cancelDeliveryOrder: vi.fn(),
  createManualDeliveryOrder: vi.fn(),
}));

const m = vi.mocked(api);
const STORE = "loja-1";

// O que as telas do salão, da cozinha, do caixa e do delivery têm abertas enquanto a equipe trabalha.
const shown = {
  tables: TABLES_KEY(STORE),
  orders: ORDERS_KEY(STORE),
  tableOrder: ACTIVE_ORDER_KEY(STORE, "mesa-4"),
  kitchen: KITCHEN_KEY(STORE),
  sales: ["sales", "today"],
  cash: ["cash", "current"],
  delivery: DELIVERY_KEY(STORE),
} as const;
type Shown = keyof typeof shown;
const names = Object.keys(shown) as Shown[];
let fetches: Record<Shown, Mock>;

async function setup<T>(mutationHook: () => T) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const { result } = renderHook(() => ({
    screens: useQueries({ queries: names.map((name) => ({ queryKey: shown[name], queryFn: fetches[name] })) }),
    mutation: mutationHook(),
  }), { wrapper });
  await waitFor(() => expect(result.current.screens.every((q) => q.isSuccess)).toBe(true));
  return result;
}

/** As telas listadas pedem os dados de novo; as outras ficam como estavam. */
async function expectRefreshed(...refreshed: Shown[]) {
  for (const name of refreshed) await waitFor(() => expect(fetches[name], name).toHaveBeenCalledTimes(2));
  for (const name of names.filter((n) => !refreshed.includes(n))) expect(fetches[name], name).toHaveBeenCalledTimes(1);
}

beforeEach(() => {
  vi.clearAllMocks();
  fetches = Object.fromEntries(names.map((name) => [name, vi.fn().mockResolvedValue([])])) as Record<Shown, Mock>;
});

describe("table orders", () => {
  const order = { id: "cmd-1" } as OrderDto;

  it.each([
    {
      action: "opening an order for a table",
      hook: useOpenOrder,
      vars: { orderType: "DineIn", tableId: "mesa-4", partySize: 2 },
      call: () => m.openOrder,
      args: [{ orderType: "DineIn", tableId: "mesa-4", partySize: 2 }],
    },
    {
      action: "adding an item",
      hook: useAddItem,
      vars: { orderId: "cmd-1", req: { productId: "chope", quantity: 2 } },
      call: () => m.addOrderItem,
      args: ["cmd-1", { productId: "chope", quantity: 2 }],
    },
    {
      action: "the kitchen marking an item ready",
      hook: useUpdateItemStatus,
      vars: { orderId: "cmd-1", itemId: "it-1", status: "Ready" },
      call: () => m.updateItemStatus,
      args: ["cmd-1", "it-1", "Ready"],
    },
    {
      action: "closing the bill",
      hook: useCloseOrder,
      vars: "cmd-1",
      call: () => m.closeOrder,
      args: ["cmd-1"],
    },
    {
      action: "cancelling the order",
      hook: useCancelOrder,
      vars: "cmd-1",
      call: () => m.cancelOrder,
      args: ["cmd-1"],
    },
  ])("$action reaches the API and refreshes the tables, the orders, the table's order and the kitchen", async ({ hook, vars, call, args }) => {
    call().mockResolvedValue(order as never);
    const result = await setup(() => hook(STORE));

    await act(() => (result.current.mutation.mutateAsync as (v: unknown) => Promise<unknown>)(vars));

    expect(call()).toHaveBeenCalledWith(...args);
    await expectRefreshed("tables", "orders", "tableOrder", "kitchen");
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("cancelling an item confirms it to the waiter and refreshes the same screens", async () => {
    m.cancelOrderItem.mockResolvedValue(order);
    const result = await setup(() => useCancelOrderItem(STORE));

    await act(() => result.current.mutation.mutateAsync({ orderId: "cmd-1", itemId: "it-1" }));

    expect(m.cancelOrderItem).toHaveBeenCalledWith("cmd-1", "it-1");
    expect(toast.success).toHaveBeenCalledWith("Item cancelado.");
    await expectRefreshed("tables", "orders", "tableOrder", "kitchen");
  });

  it("a bill the server refuses to close shows its reason and refreshes nothing", async () => {
    m.closeOrder.mockRejectedValue(new Error("Há itens ainda não entregues."));
    const result = await setup(() => useCloseOrder(STORE));

    await act(async () => {
      await expect(result.current.mutation.mutateAsync("cmd-1")).rejects.toThrow();
    });

    expect(toast.error).toHaveBeenCalledWith("Há itens ainda não entregues.");
    await expectRefreshed();
  });
});

describe("paying an order", () => {
  const req = { payments: [{ method: "Pix", type: "Full", amount: 132.5 }], partySize: 2 };

  it("sends the payments and refreshes the floor, the kitchen, the sales and the cash register", async () => {
    m.payOrder.mockResolvedValue({ id: "cmd-1" } as OrderDto);
    const result = await setup(() => usePayOrder(STORE));

    await act(() => result.current.mutation.mutateAsync({ orderId: "cmd-1", req }));

    expect(m.payOrder).toHaveBeenCalledWith("cmd-1", req);
    await expectRefreshed("tables", "orders", "kitchen", "sales", "cash");
  });

  it("a payment that fails tells the cashier and leaves sales and cash as they were", async () => {
    m.payOrder.mockRejectedValue(new Error("gateway"));
    const result = await setup(() => usePayOrder(STORE));

    await act(async () => {
      await expect(result.current.mutation.mutateAsync({ orderId: "cmd-1", req })).rejects.toThrow();
    });

    expect(toast.error).toHaveBeenCalledWith("Erro ao processar pagamento. Tente novamente.");
    await expectRefreshed();
  });
});

describe("delivery orders", () => {
  const delivery = (over: Partial<DeliveryOrderDto> = {}) =>
    ({ id: "d-1", orderNumber: 87, status: "Accepted", orderType: "Delivery", ...over }) as DeliveryOrderDto;

  it.each([
    {
      action: "accepting",
      hook: useAcceptDelivery,
      vars: { id: "d-1", req: { estimatedMinutes: 40 } },
      call: () => m.acceptDeliveryOrder,
      args: ["d-1", { estimatedMinutes: 40 }],
      message: "Pedido aceito — comanda aberta na cozinha.",
    },
    {
      action: "rejecting",
      hook: useRejectDelivery,
      vars: { id: "d-1", req: { reason: "Fora da área" } },
      call: () => m.rejectDeliveryOrder,
      args: ["d-1", { reason: "Fora da área" }],
      message: "Pedido rejeitado.",
    },
    {
      action: "cancelling",
      hook: useCancelDelivery,
      vars: "d-1",
      call: () => m.cancelDeliveryOrder,
      args: ["d-1"],
      message: "Pedido cancelado.",
    },
    {
      action: "registering a phone order",
      hook: useCreateManualDelivery,
      vars: { orderType: "Delivery", customerName: "Maria", customerPhone: "85999990000", channel: "Phone" },
      call: () => m.createManualDeliveryOrder,
      args: [{ orderType: "Delivery", customerName: "Maria", customerPhone: "85999990000", channel: "Phone" }],
      message: "Pedido #87 criado com sucesso.",
    },
  ])("$action reaches the API, confirms it and refreshes the delivery board only", async ({ hook, vars, call, args, message }) => {
    call().mockResolvedValue(delivery() as never);
    const result = await setup(() => hook(STORE));

    await act(() => (result.current.mutation.mutateAsync as (v: unknown) => Promise<unknown>)(vars));

    expect(call()).toHaveBeenCalledWith(...args);
    expect(toast.success).toHaveBeenCalledWith(message);
    await expectRefreshed("delivery");
  });

  it.each([
    { status: "OutForDelivery" as const, orderType: "Delivery", message: "Pedido despachado para entrega." },
    { status: "Delivered" as const, orderType: "Delivery", message: "Entrega confirmada!" },
    { status: "Delivered" as const, orderType: "Takeaway", message: "Retirada confirmada!" },
  ])("advancing a $orderType order to $status says \"$message\" and refreshes the board", async ({ status, orderType, message }) => {
    m.updateDeliveryStatus.mockResolvedValue(delivery({ status, orderType } as Partial<DeliveryOrderDto>));
    const result = await setup(() => useAdvanceDelivery(STORE));

    await act(() => result.current.mutation.mutateAsync({ id: "d-1", req: { status, riderName: "João" } }));

    expect(m.updateDeliveryStatus).toHaveBeenCalledWith("d-1", { status, riderName: "João" });
    expect(toast.success).toHaveBeenCalledWith(message);
    await expectRefreshed("delivery");
  });

  it("an accept the server refuses tells the attendant and leaves the board as it was", async () => {
    m.acceptDeliveryOrder.mockRejectedValue(new Error("409"));
    const result = await setup(() => useAcceptDelivery(STORE));

    await act(async () => {
      await expect(result.current.mutation.mutateAsync({ id: "d-1", req: {} })).rejects.toThrow();
    });

    expect(toast.error).toHaveBeenCalledWith("Erro ao aceitar pedido. Tente novamente.");
    expect(toast.success).not.toHaveBeenCalled();
    await expectRefreshed();
  });
});
