import { type ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../api/service.api";
import type {
  PublicBookingSettingsDto,
  ServiceSettingsDto,
  SvcAppointmentDto,
  SvcCustomerPackageDto,
  SvcOrderDto,
  SvcPackageDto,
  SvcPaymentDto,
} from "../api/service.api";
import { useAppointments, useChangeAppointmentStatus } from "./useAppointments";
import { useCommissionSummary } from "./useCommissions";
import { useConsumeCustomerPackage, useCustomerPackages } from "./useCustomerPackages";
import { useChangeOrderStatus, useOrders } from "./useOrders";
import { useCreatePackage, usePackages } from "./usePackages";
import { useCreatePayment, usePayments, useVoidPayment } from "./usePayments";
import {
  usePublicBookingSettings,
  useUpdateCustomerEmails,
  useUpdatePortalBranding,
  useUpdatePublicBookingSettings,
} from "./usePublicBookingSettings";
import { serviceKeys, useServicePresetQuery } from "./useServicePreset";
import { useServiceSettings, useSetServicePreset } from "./useServiceSettings";

// Só a borda HTTP é trocada: as consultas e mutações reais dos hooks rodam sobre ela.
vi.mock("../api/service.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/service.api")>()),
  fetchPayments: vi.fn(),
  fetchCommissionSummary: vi.fn(),
  fetchAppointments: vi.fn(),
  fetchOrders: vi.fn(),
  fetchPackages: vi.fn(),
  fetchCustomerPackages: vi.fn(),
  getServiceSettings: vi.fn(),
  getServicePreset: vi.fn(),
  getPublicBookingSettings: vi.fn(),
  createPayment: vi.fn(),
  voidPayment: vi.fn(),
  changeAppointmentStatus: vi.fn(),
  changeOrderStatus: vi.fn(),
  consumeCustomerPackage: vi.fn(),
  createPackage: vi.fn(),
  setServicePreset: vi.fn(),
  updatePublicBookingSettings: vi.fn(),
  updateCustomerEmails: vi.fn(),
  updatePortalBranding: vi.fn(),
}));

const m = vi.mocked(api);

function setup<T>(hook: () => T) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return { client, ...renderHook(hook, { wrapper }) };
}

beforeEach(() => {
  vi.clearAllMocks();
  m.fetchPayments.mockResolvedValue([]);
  m.fetchCommissionSummary.mockResolvedValue([]);
  m.fetchAppointments.mockResolvedValue([]);
  m.fetchOrders.mockResolvedValue([]);
  m.fetchPackages.mockResolvedValue([]);
  m.fetchCustomerPackages.mockResolvedValue([]);
});

describe("payments", () => {
  // O que a tela da comanda mostra ao lado do pagamento: os pagamentos dela, o resumo de comissões e a agenda.
  const usePaymentScreen = () => ({
    payments: usePayments({ orderId: "os-1" }),
    commissions: useCommissionSummary(undefined),
    appointments: useAppointments(),
    create: useCreatePayment(),
    voidIt: useVoidPayment(),
  });
  type Screen = ReturnType<typeof usePaymentScreen>;
  const loaded = (s: Screen) => s.payments.isSuccess && s.commissions.isSuccess && s.appointments.isSuccess;

  it("a payment received refreshes the payments and the commissions it settles, and leaves the rest alone", async () => {
    const body = { amount: 80, method: "Pix" as const, paidAt: "2026-11-13T15:00:00Z", orderId: "os-1" };
    m.createPayment.mockResolvedValue({ id: "pg-1" } as SvcPaymentDto);
    const { result } = setup(usePaymentScreen);
    await waitFor(() => expect(loaded(result.current)).toBe(true));

    await act(() => result.current.create.mutateAsync(body));

    expect(m.createPayment).toHaveBeenCalledWith(body);
    await waitFor(() => expect(m.fetchPayments).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(2));
    expect(m.fetchAppointments).toHaveBeenCalledTimes(1);
  });

  it("voiding a payment sends the reason and refreshes the payments and the commissions", async () => {
    m.voidPayment.mockResolvedValue({ id: "pg-1", status: "Voided" } as SvcPaymentDto);
    const { result } = setup(usePaymentScreen);
    await waitFor(() => expect(loaded(result.current)).toBe(true));

    await act(() => result.current.voidIt.mutateAsync({ id: "pg-1", reason: "lançado em duplicidade" }));

    expect(m.voidPayment).toHaveBeenCalledWith("pg-1", "lançado em duplicidade");
    await waitFor(() => expect(m.fetchPayments).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(2));
  });

  it("a refused payment surfaces the error and asks for nothing again", async () => {
    m.createPayment.mockRejectedValue(new Error("Valor acima do saldo da comanda."));
    const { result } = setup(usePaymentScreen);
    await waitFor(() => expect(loaded(result.current)).toBe(true));

    await act(async () => {
      await expect(result.current.create.mutateAsync({ amount: 999, method: "Cash", paidAt: "2026-11-13T15:00:00Z", orderId: "os-1" }))
        .rejects.toThrow("Valor acima do saldo da comanda.");
    });

    await waitFor(() => expect(result.current.create.error?.message).toBe("Valor acima do saldo da comanda."));
    expect(m.fetchPayments).toHaveBeenCalledTimes(1);
    expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(1);
  });
});

describe("appointment status", () => {
  it("completing an appointment refreshes the agenda and the commissions it recognises", async () => {
    m.changeAppointmentStatus.mockResolvedValue({ id: "ag-1", status: "Completed" } as SvcAppointmentDto);
    const { result } = setup(() => ({
      agenda: useAppointments({ professionalId: "p-1" }),
      commissions: useCommissionSummary("p-1"),
      orders: useOrders(),
      change: useChangeAppointmentStatus(),
    }));
    await waitFor(() => expect(result.current.agenda.isSuccess && result.current.commissions.isSuccess && result.current.orders.isSuccess).toBe(true));

    await act(() => result.current.change.mutateAsync({ id: "ag-1", status: "Completed" }));

    expect(m.changeAppointmentStatus).toHaveBeenCalledWith("ag-1", { status: "Completed", reason: undefined });
    await waitFor(() => expect(m.fetchAppointments).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(2));
    expect(m.fetchOrders).toHaveBeenCalledTimes(1);
  });
});

describe("service orders", () => {
  it("a status change keeps the order the server returned and refreshes the order list and the commissions", async () => {
    const completed = { id: "os-1", status: "Completed" } as SvcOrderDto;
    m.changeOrderStatus.mockResolvedValue(completed);
    const { result, client } = setup(() => ({
      orders: useOrders({ status: "Open" }),
      commissions: useCommissionSummary(undefined),
      change: useChangeOrderStatus(),
    }));
    await waitFor(() => expect(result.current.orders.isSuccess && result.current.commissions.isSuccess).toBe(true));

    await act(() => result.current.change.mutateAsync({ id: "os-1", status: "Completed", reason: null }));

    expect(m.changeOrderStatus).toHaveBeenCalledWith("os-1", { status: "Completed", reason: null });
    expect(client.getQueryData(serviceKeys.order("os-1"))).toEqual(completed);
    await waitFor(() => expect(m.fetchOrders).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(2));
  });

  it("a refused status change leaves the list and the cached order as they were", async () => {
    m.changeOrderStatus.mockRejectedValue(new Error("A comanda tem itens sem profissional."));
    const { result, client } = setup(() => ({ orders: useOrders(), change: useChangeOrderStatus() }));
    await waitFor(() => expect(result.current.orders.isSuccess).toBe(true));

    await act(async () => {
      await expect(result.current.change.mutateAsync({ id: "os-1", status: "Completed" })).rejects.toThrow();
    });

    expect(client.getQueryData(serviceKeys.order("os-1"))).toBeUndefined();
    expect(m.fetchOrders).toHaveBeenCalledTimes(1);
  });
});

describe("customer packages", () => {
  it("consuming a session keeps the package the server returned and refreshes the packages and the commissions", async () => {
    const after = { id: "cp-1", status: "Active" } as SvcCustomerPackageDto;
    m.consumeCustomerPackage.mockResolvedValue(after);
    const body = { catalogItemId: "corte", quantity: 1, professionalId: "p-1" };
    const { result, client } = setup(() => ({
      packages: useCustomerPackages({ customerId: "c-1" }),
      commissions: useCommissionSummary(undefined),
      consume: useConsumeCustomerPackage(),
    }));
    await waitFor(() => expect(result.current.packages.isSuccess && result.current.commissions.isSuccess).toBe(true));

    await act(() => result.current.consume.mutateAsync({ id: "cp-1", body }));

    expect(m.consumeCustomerPackage).toHaveBeenCalledWith("cp-1", body);
    expect(client.getQueryData(serviceKeys.customerPackage("cp-1"))).toEqual(after);
    await waitFor(() => expect(m.fetchCustomerPackages).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.fetchCommissionSummary).toHaveBeenCalledTimes(2));
  });
});

describe("packages", () => {
  it("a new package is cached and the package list is fetched again", async () => {
    const created = { id: "pk-1", name: "Combo 4 cortes" } as SvcPackageDto;
    m.createPackage.mockResolvedValue(created);
    const { result, client } = setup(() => ({ list: usePackages(true), create: useCreatePackage() }));
    await waitFor(() => expect(result.current.list.isSuccess).toBe(true));

    await act(() => result.current.create.mutateAsync({ name: "Combo 4 cortes", price: 120 }));

    expect(m.createPackage).toHaveBeenCalledWith({ name: "Combo 4 cortes", price: 120 });
    expect(client.getQueryData(serviceKeys.package("pk-1"))).toEqual(created);
    await waitFor(() => expect(m.fetchPackages).toHaveBeenCalledTimes(2));
  });
});

describe("service settings", () => {
  it("choosing a preset refreshes the settings and the resolved preset", async () => {
    m.getServiceSettings.mockResolvedValue({ isConfigured: false } as ServiceSettingsDto);
    m.getServicePreset.mockResolvedValue({} as Awaited<ReturnType<typeof api.getServicePreset>>);
    m.setServicePreset.mockResolvedValue({ isConfigured: true } as ServiceSettingsDto);
    const { result } = setup(() => ({
      settings: useServiceSettings(true),
      preset: useServicePresetQuery(true),
      choose: useSetServicePreset(),
    }));
    await waitFor(() => expect(result.current.settings.isSuccess && result.current.preset.isSuccess).toBe(true));

    await act(() => result.current.choose.mutateAsync("barbearia"));

    expect(m.setServicePreset).toHaveBeenCalledWith("barbearia");
    await waitFor(() => expect(m.getServiceSettings).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(m.getServicePreset).toHaveBeenCalledTimes(2));
  });
});

describe("public booking settings", () => {
  const saved = { publicBookingEnabled: true, customerEmailsEnabled: true, displayName: "Barbearia do Zé" } as PublicBookingSettingsDto;

  it.each([
    {
      what: "the booking rules",
      send: useUpdatePublicBookingSettings,
      endpoint: () => m.updatePublicBookingSettings,
      body: { publicBookingEnabled: true, bookingDaysAhead: 30, minLeadMinutes: 60, slotIntervalMinutes: 30, showPrices: true,
        autoConfirmAppointments: false, timeZoneId: "America/Fortaleza" },
    },
    {
      what: "the customer e-mails",
      send: useUpdateCustomerEmails,
      endpoint: () => m.updateCustomerEmails,
      body: { customerEmailsEnabled: true, reminderHoursBefore: 24 },
    },
    {
      what: "the portal branding",
      send: useUpdatePortalBranding,
      endpoint: () => m.updatePortalBranding,
      body: { displayName: "Barbearia do Zé", description: null, logoUrl: null, coverImageUrl: null, brandColor: "#0f766e",
        whatsApp: null, address: null },
    },
  ])("saving $what sends it to its own endpoint, shows what was saved and confirms it with the server", async ({ send, endpoint, body }) => {
    m.getPublicBookingSettings.mockResolvedValueOnce({ publicBookingEnabled: false } as PublicBookingSettingsDto)
      .mockResolvedValue(saved);
    endpoint().mockResolvedValue(saved);
    const { result, client } = setup(() => ({ settings: usePublicBookingSettings(true), save: send() }));
    await waitFor(() => expect(result.current.settings.data?.publicBookingEnabled).toBe(false));

    await act(() => (result.current.save.mutateAsync as (b: typeof body) => Promise<unknown>)(body));

    expect(endpoint()).toHaveBeenCalledWith(body);
    expect(client.getQueryData(serviceKeys.publicBooking())).toEqual(saved);
    await waitFor(() => expect(m.getPublicBookingSettings).toHaveBeenCalledTimes(2));
    expect(result.current.settings.data).toEqual(saved);
  });
});
