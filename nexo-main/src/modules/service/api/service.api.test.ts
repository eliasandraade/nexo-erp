import { describe, it, expect, vi, beforeEach } from "vitest";

const http = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  patch: vi.fn(),
  delete: vi.fn(),
}));

vi.mock("@/services/api-client", () => ({ apiClient: http }));

import * as api from "./service.api";

type Method = keyof typeof http;

/** O último pedido feito ao cliente HTTP: método, caminho e corpo (quando há). */
function lastCall(): { method: Method; path: string; body?: unknown; args: number } {
  const calls = (Object.keys(http) as Method[])
    .filter((m) => http[m].mock.calls.length > 0)
    .map((m) => ({ method: m, call: http[m].mock.calls[http[m].mock.calls.length - 1] }));
  expect(calls).toHaveLength(1);
  const [{ method, call }] = calls;
  return { method, path: call[0] as string, body: call[1], args: call.length };
}

beforeEach(() => {
  for (const fn of Object.values(http)) {
    fn.mockReset();
    fn.mockResolvedValue({ ok: true });
  }
});

const appt = {
  customerId: "c1", professionalId: "p1", catalogItemId: "s1",
  startsAt: "2026-11-13T12:00:00.000Z", endsAt: "2026-11-13T12:30:00.000Z", subjectId: null, notes: "Primeira vez",
};

/**
 * Cada função do cliente da Service: o método HTTP, a rota (sob /v1/service) e o corpo que a API recebe.
 * Uma rota errada aqui é um 404 em produção que nenhum teste de tela pega, porque as telas mockam a API.
 */
const cases: Array<[string, () => Promise<unknown>, Method, string, unknown?]> = [
  // preset e configurações
  ["getServicePreset", () => api.getServicePreset(), "get", "/v1/service/preset"],
  ["getServiceSettings", () => api.getServiceSettings(), "get", "/v1/service/settings"],
  ["setServicePreset", () => api.setServicePreset("barbearia"), "put", "/v1/service/settings/preset", { presetKey: "barbearia" }],
  ["getPublicBookingSettings", () => api.getPublicBookingSettings(), "get", "/v1/service/settings/public-booking"],
  ["updatePublicBookingSettings", () => api.updatePublicBookingSettings({
    publicBookingEnabled: true, bookingDaysAhead: 30, minLeadMinutes: 60, slotIntervalMinutes: 15,
    showPrices: true, autoConfirmAppointments: false, timeZoneId: "America/Sao_Paulo",
  }), "put", "/v1/service/settings/public-booking", {
    publicBookingEnabled: true, bookingDaysAhead: 30, minLeadMinutes: 60, slotIntervalMinutes: 15,
    showPrices: true, autoConfirmAppointments: false, timeZoneId: "America/Sao_Paulo",
  }],
  ["updatePortalBranding", () => api.updatePortalBranding({
    displayName: "Barbearia do João", description: null, logoUrl: null, coverImageUrl: null,
    brandColor: "#112233", whatsApp: "85999990000", address: null,
  }), "put", "/v1/service/settings/branding", {
    displayName: "Barbearia do João", description: null, logoUrl: null, coverImageUrl: null,
    brandColor: "#112233", whatsApp: "85999990000", address: null,
  }],
  ["updateCustomerEmails", () => api.updateCustomerEmails({ customerEmailsEnabled: true, reminderHoursBefore: 24 }),
    "put", "/v1/service/settings/customer-emails", { customerEmailsEnabled: true, reminderHoursBefore: 24 }],

  // profissionais
  ["fetchProfessionals (padrão)", () => api.fetchProfessionals(), "get", "/v1/service/professionals?onlyActive=false"],
  ["fetchProfessionals (ativos)", () => api.fetchProfessionals(true), "get", "/v1/service/professionals?onlyActive=true"],
  ["fetchProfessional", () => api.fetchProfessional("p1"), "get", "/v1/service/professionals/p1"],
  ["createProfessional", () => api.createProfessional({ name: "Ana", defaultCommissionPercent: 40 }),
    "post", "/v1/service/professionals", { name: "Ana", defaultCommissionPercent: 40 }],
  ["updateProfessional", () => api.updateProfessional("p1", { name: "Ana Paula", color: "#ff0000" }),
    "put", "/v1/service/professionals/p1", { name: "Ana Paula", color: "#ff0000" }],
  ["activateProfessional", () => api.activateProfessional("p1"), "post", "/v1/service/professionals/p1/activate"],
  ["deactivateProfessional", () => api.deactivateProfessional("p1"), "post", "/v1/service/professionals/p1/deactivate"],

  // catálogo
  ["fetchCatalog (padrão)", () => api.fetchCatalog(), "get", "/v1/service/catalog?onlyActive=false"],
  ["fetchCatalog (ativos)", () => api.fetchCatalog(true), "get", "/v1/service/catalog?onlyActive=true"],
  ["fetchCatalogItem", () => api.fetchCatalogItem("s1"), "get", "/v1/service/catalog/s1"],
  ["createCatalogItem", () => api.createCatalogItem({ name: "Corte", durationMinutes: 30, price: 45 }),
    "post", "/v1/service/catalog", { name: "Corte", durationMinutes: 30, price: 45 }],
  ["updateCatalogItem", () => api.updateCatalogItem("s1", { name: "Corte + barba", durationMinutes: 50, requiresSubject: false }),
    "put", "/v1/service/catalog/s1", { name: "Corte + barba", durationMinutes: 50, requiresSubject: false }],
  ["activateCatalogItem", () => api.activateCatalogItem("s1"), "post", "/v1/service/catalog/s1/activate"],
  ["deactivateCatalogItem", () => api.deactivateCatalogItem("s1"), "post", "/v1/service/catalog/s1/deactivate"],

  // cadastros (pet, veículo...)
  ["fetchSubject", () => api.fetchSubject("sub1"), "get", "/v1/service/subjects/sub1"],
  ["createSubject", () => api.createSubject({ customerId: "c1", kind: "Pet", displayName: "Rex" }),
    "post", "/v1/service/subjects", { customerId: "c1", kind: "Pet", displayName: "Rex" }],
  ["updateSubject", () => api.updateSubject("sub1", { kind: "Pet", displayName: "Rex II", notes: "Alérgico" }),
    "put", "/v1/service/subjects/sub1", { kind: "Pet", displayName: "Rex II", notes: "Alérgico" }],
  ["activateSubject", () => api.activateSubject("sub1"), "post", "/v1/service/subjects/sub1/activate"],
  ["deactivateSubject", () => api.deactivateSubject("sub1"), "post", "/v1/service/subjects/sub1/deactivate"],

  // prontuário
  ["fetchRecords", () => api.fetchRecords("Subject", "sub1"), "get", "/v1/service/records?contextType=Subject&contextId=sub1"],
  ["fetchRecord", () => api.fetchRecord("r1"), "get", "/v1/service/records/r1"],
  ["createRecord", () => api.createRecord({ contextType: "Order", contextId: "o1", text: "Cliente pediu degradê" }),
    "post", "/v1/service/records", { contextType: "Order", contextId: "o1", text: "Cliente pediu degradê" }],
  ["deleteRecord", () => api.deleteRecord("r1"), "delete", "/v1/service/records/r1"],

  // agendamentos
  ["fetchAppointment", () => api.fetchAppointment("a1"), "get", "/v1/service/appointments/a1"],
  ["createAppointment", () => api.createAppointment(appt), "post", "/v1/service/appointments", appt],
  ["updateAppointment", () => api.updateAppointment("a1", appt), "put", "/v1/service/appointments/a1", appt],
  ["changeAppointmentStatus", () => api.changeAppointmentStatus("a1", { status: "Cancelled", reason: "Cliente desmarcou" }),
    "patch", "/v1/service/appointments/a1/status", { status: "Cancelled", reason: "Cliente desmarcou" }],

  // ordens / comandas
  ["fetchOrder", () => api.fetchOrder("o1"), "get", "/v1/service/orders/o1"],
  ["createOrder", () => api.createOrder({ customerId: "c1", professionalId: "p1" }),
    "post", "/v1/service/orders", { customerId: "c1", professionalId: "p1" }],
  ["createOrderFromAppointment", () => api.createOrderFromAppointment("a1"), "post", "/v1/service/orders/from-appointment/a1"],
  ["updateOrder", () => api.updateOrder("o1", { notes: "Retorno em 15 dias" }), "put", "/v1/service/orders/o1", { notes: "Retorno em 15 dias" }],
  ["changeOrderStatus", () => api.changeOrderStatus("o1", { status: "Completed" }),
    "patch", "/v1/service/orders/o1/status", { status: "Completed" }],
  ["addOrderItem", () => api.addOrderItem("o1", { catalogItemId: "s1", quantity: 2, professionalId: null }),
    "post", "/v1/service/orders/o1/items", { catalogItemId: "s1", quantity: 2, professionalId: null }],
  ["updateOrderItem", () => api.updateOrderItem("o1", "i1", { quantity: 3, professionalId: "p2" }),
    "put", "/v1/service/orders/o1/items/i1", { quantity: 3, professionalId: "p2" }],
  ["removeOrderItem", () => api.removeOrderItem("o1", "i1"), "delete", "/v1/service/orders/o1/items/i1"],

  // pacotes
  ["fetchPackages (todos)", () => api.fetchPackages(), "get", "/v1/service/packages"],
  ["fetchPackages (ativos)", () => api.fetchPackages(true), "get", "/v1/service/packages?active=true"],
  ["fetchPackages (inativos)", () => api.fetchPackages(false), "get", "/v1/service/packages?active=false"],
  ["fetchPackage", () => api.fetchPackage("pk1"), "get", "/v1/service/packages/pk1"],
  ["createPackage", () => api.createPackage({ name: "4 cortes", price: 150, validityDays: 90 }),
    "post", "/v1/service/packages", { name: "4 cortes", price: 150, validityDays: 90 }],
  ["updatePackage", () => api.updatePackage("pk1", { name: "4 cortes (promo)", validityDays: null }),
    "put", "/v1/service/packages/pk1", { name: "4 cortes (promo)", validityDays: null }],
  ["updatePackagePrice", () => api.updatePackagePrice("pk1", 139.9), "put", "/v1/service/packages/pk1/price", { price: 139.9 }],
  ["activatePackage", () => api.activatePackage("pk1"), "post", "/v1/service/packages/pk1/activate"],
  ["deactivatePackage", () => api.deactivatePackage("pk1"), "post", "/v1/service/packages/pk1/deactivate"],
  ["addPackageItem", () => api.addPackageItem("pk1", { catalogItemId: "s1", includedQuantity: 4 }),
    "post", "/v1/service/packages/pk1/items", { catalogItemId: "s1", includedQuantity: 4 }],
  ["updatePackageItem", () => api.updatePackageItem("pk1", "pi1", 5), "put", "/v1/service/packages/pk1/items/pi1", { includedQuantity: 5 }],
  ["removePackageItem", () => api.removePackageItem("pk1", "pi1"), "delete", "/v1/service/packages/pk1/items/pi1"],

  // pacotes do cliente
  ["fetchCustomerPackage", () => api.fetchCustomerPackage("cp1"), "get", "/v1/service/customer-packages/cp1"],
  ["assignCustomerPackage", () => api.assignCustomerPackage({ packageId: "pk1", customerId: "c1", startsAt: "2026-11-13T03:00:00.000Z" }),
    "post", "/v1/service/customer-packages", { packageId: "pk1", customerId: "c1", startsAt: "2026-11-13T03:00:00.000Z" }],
  ["cancelCustomerPackage", () => api.cancelCustomerPackage("cp1"), "post", "/v1/service/customer-packages/cp1/cancel"],
  ["consumeCustomerPackage", () => api.consumeCustomerPackage("cp1", { catalogItemId: "s1", quantity: 1, professionalId: "p1" }),
    "post", "/v1/service/customer-packages/cp1/consume", { catalogItemId: "s1", quantity: 1, professionalId: "p1" }],
  ["reverseCustomerPackageUsage", () => api.reverseCustomerPackageUsage("cp1", "u1", "Lançado em duplicidade"),
    "post", "/v1/service/customer-packages/cp1/usages/u1/reverse", { reason: "Lançado em duplicidade" }],
  ["fetchCustomerPackageUsages", () => api.fetchCustomerPackageUsages("cp1"), "get", "/v1/service/customer-packages/cp1/usages"],

  // pagamentos
  ["fetchPayment", () => api.fetchPayment("pay1"), "get", "/v1/service/payments/pay1"],
  ["createPayment", () => api.createPayment({ amount: 90, method: "Pix", paidAt: "2026-11-13T15:00:00.000Z", orderId: "o1" }),
    "post", "/v1/service/payments", { amount: 90, method: "Pix", paidAt: "2026-11-13T15:00:00.000Z", orderId: "o1" }],
  ["voidPayment", () => api.voidPayment("pay1", "Cobrado a mais"), "post", "/v1/service/payments/pay1/void", { reason: "Cobrado a mais" }],
  ["voidPayment (sem motivo)", () => api.voidPayment("pay1"), "post", "/v1/service/payments/pay1/void", { reason: undefined }],
  ["fetchOrderPaymentSummary", () => api.fetchOrderPaymentSummary("o1"), "get", "/v1/service/payments/order/o1/summary"],
  ["fetchCustomerPackagePaymentSummary", () => api.fetchCustomerPackagePaymentSummary("cp1"),
    "get", "/v1/service/payments/customer-package/cp1/summary"],

  // comissões
  ["fetchCommissionSummary (todos)", () => api.fetchCommissionSummary(), "get", "/v1/service/commissions/summary"],
  ["fetchCommissionSummary (um profissional)", () => api.fetchCommissionSummary("p1"),
    "get", "/v1/service/commissions/summary?professionalId=p1"],
  ["fetchCommissionPayout", () => api.fetchCommissionPayout("po1"), "get", "/v1/service/commissions/payouts/po1"],
  ["closeCommissionPayout", () => api.closeCommissionPayout({
    professionalId: "p1", periodStart: "2026-10-01", periodEnd: "2026-10-31", notes: null,
  }), "post", "/v1/service/commissions/payouts", { professionalId: "p1", periodStart: "2026-10-01", periodEnd: "2026-10-31", notes: null }],
  ["markCommissionPayoutPaid (com data)", () => api.markCommissionPayoutPaid("po1", "2026-11-05T12:00:00.000Z"),
    "post", "/v1/service/commissions/payouts/po1/pay", { paidAt: "2026-11-05T12:00:00.000Z" }],
  ["markCommissionPayoutPaid (sem data)", () => api.markCommissionPayoutPaid("po1"), "post", "/v1/service/commissions/payouts/po1/pay", {}],
  ["markCommissionPayoutPaid (data nula)", () => api.markCommissionPayoutPaid("po1", null), "post", "/v1/service/commissions/payouts/po1/pay", {}],

  // bloqueios de agenda
  ["createTimeBlock", () => api.createTimeBlock({
    professionalId: null, startsAt: "2026-12-25T03:00:00.000Z", endsAt: "2026-12-26T03:00:00.000Z", reason: "Natal",
  }), "post", "/v1/service/time-blocks", {
    professionalId: null, startsAt: "2026-12-25T03:00:00.000Z", endsAt: "2026-12-26T03:00:00.000Z", reason: "Natal",
  }],
  ["deleteTimeBlock", () => api.deleteTimeBlock("b1"), "delete", "/v1/service/time-blocks/b1"],

  // painel financeiro
  ["fetchServiceFinance", () => api.fetchServiceFinance("2026-11-01", "2026-11-30"),
    "get", "/v1/service/finance/summary?from=2026-11-01&to=2026-11-30"],
];

describe("service.api — rota, método e corpo de cada chamada", () => {
  it.each(cases)("%s", async (_name, invoke, method, path, body) => {
    const response = { id: "resposta" };
    http[method].mockResolvedValueOnce(response);

    await expect(invoke()).resolves.toBe(response);

    const call = lastCall();
    expect(call.method).toBe(method);
    expect(call.path).toBe(path);
    if (body === undefined) {
      expect(call.body).toBeUndefined();
    } else {
      expect(call.body).toEqual(body);
    }
  });

  it("propaga a falha da API sem engolir o erro", async () => {
    const error = new Error("Conflito de horário");
    http.post.mockRejectedValueOnce(error);
    await expect(api.createAppointment(appt)).rejects.toBe(error);
  });
});

describe("service.api — filtros na query string", () => {
  it("fetchSubjects só envia os filtros informados, com active=false explícito", async () => {
    await api.fetchSubjects();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/subjects?");

    await api.fetchSubjects({ customerId: "c1", kind: "Vehicle", active: false });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/subjects?customerId=c1&kind=Vehicle&active=false");

    await api.fetchSubjects({ active: true });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/subjects?active=true");
  });

  it("fetchAppointments monta o período e ignora filtros vazios", async () => {
    await api.fetchAppointments();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/appointments?");

    await api.fetchAppointments({
      from: "2026-11-13T03:00:00.000Z", to: "2026-11-14T02:59:59.999Z", professionalId: "p1",
      status: undefined, customerId: "",
    });
    const path = http.get.mock.calls.at(-1)![0] as string;
    const q = new URLSearchParams(path.split("?")[1]);
    expect(path.startsWith("/v1/service/appointments?")).toBe(true);
    expect(q.get("from")).toBe("2026-11-13T03:00:00.000Z");
    expect(q.get("to")).toBe("2026-11-14T02:59:59.999Z");
    expect(q.get("professionalId")).toBe("p1");
    expect(q.has("status")).toBe(false);
    expect(q.has("customerId")).toBe(false);
  });

  it("fetchOrders filtra pela comanda de um agendamento", async () => {
    await api.fetchOrders();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/orders?");
    await api.fetchOrders({ appointmentId: "a1", status: "Open" });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/orders?appointmentId=a1&status=Open");
  });

  it("fetchCustomerPackages combina cliente, cadastro e situação", async () => {
    await api.fetchCustomerPackages();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/customer-packages?");
    await api.fetchCustomerPackages({ customerId: "c1", subjectId: "sub1", status: "Active", packageId: "" });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/customer-packages?customerId=c1&subjectId=sub1&status=Active");
  });

  it("fetchPayments filtra por método, situação e período", async () => {
    await api.fetchPayments();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/payments?");
    await api.fetchPayments({ method: "Pix", status: "Paid", from: "2026-11-01", to: "2026-11-30", orderId: undefined });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/payments?method=Pix&status=Paid&from=2026-11-01&to=2026-11-30");
  });

  it("fetchCommissionEntries e fetchCommissionPayouts ignoram filtros vazios", async () => {
    await api.fetchCommissionEntries();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/commissions/entries?");
    await api.fetchCommissionEntries({ professionalId: "p1", status: "open", from: "", payoutId: undefined });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/commissions/entries?professionalId=p1&status=open");

    await api.fetchCommissionPayouts();
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/commissions/payouts?");
    await api.fetchCommissionPayouts({ professionalId: "p1", status: "Pending" });
    expect(http.get).toHaveBeenLastCalledWith("/v1/service/commissions/payouts?professionalId=p1&status=Pending");
  });

  it("listTimeBlocks envia o período e, se houver, o profissional", async () => {
    await api.listTimeBlocks("2026-11-13T03:00:00.000Z", "2026-11-14T02:59:59.999Z");
    let q = new URLSearchParams((http.get.mock.calls.at(-1)![0] as string).split("?")[1]);
    expect([...q.keys()]).toEqual(["from", "to"]);
    expect(q.get("from")).toBe("2026-11-13T03:00:00.000Z");

    await api.listTimeBlocks("2026-11-13T03:00:00.000Z", "2026-11-14T02:59:59.999Z", "p1");
    const path = http.get.mock.calls.at(-1)![0] as string;
    expect(path.startsWith("/v1/service/time-blocks?")).toBe(true);
    q = new URLSearchParams(path.split("?")[1]);
    expect(q.get("professionalId")).toBe("p1");
  });
});
