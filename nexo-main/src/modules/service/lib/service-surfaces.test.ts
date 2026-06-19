import { describe, it, expect } from "vitest";
import type {
  ServiceCapabilities,
  ServiceLabels,
  ServicePresetDto,
  SvcSubjectKind,
} from "../api/service.api";
import { enabledSurfaces, SERVICE_SURFACES } from "./service-surfaces";

const LABELS: ServiceLabels = {
  customer: "Cliente",
  professional: "Profissional",
  catalogItem: "Serviço",
  appointment: "Agendamento",
  order: "Ordem de serviço",
  subject: "Registro",
};

const OFF: ServiceCapabilities = {
  appointments: false,
  orders: false,
  quotes: false,
  parts: false,
  packages: false,
  simpleRecord: false,
  commissions: false,
  recurrence: false,
  subjectKind: null,
};

function preset(caps: Partial<ServiceCapabilities>): ServicePresetDto {
  return {
    key: "test",
    displayName: "Test vertical",
    labels: LABELS,
    capabilities: { ...OFF, ...caps },
  };
}

const keysFor = (caps: Partial<ServiceCapabilities>) =>
  enabledSurfaces(preset(caps)).map((s) => s.key);

const keysForCtx = (caps: Partial<ServiceCapabilities>, ctx: { publicBookingEnabled?: boolean }) =>
  enabledSurfaces(preset(caps), ctx).map((s) => s.key);

describe("service-surfaces", () => {
  it("always exposes core cadastros (professionals + catalog)", () => {
    const keys = keysFor({});
    expect(keys).toContain("professionals");
    expect(keys).toContain("catalog");
  });

  it("clinica-medica (appointments only) → agenda + cadastros, no OS/packages/payments/subjects", () => {
    const keys = keysFor({ appointments: true, simpleRecord: true });
    expect(keys).toEqual(expect.arrayContaining(["agenda", "professionals", "catalog"]));
    expect(keys).not.toContain("orders");
    expect(keys).not.toContain("packages");
    expect(keys).not.toContain("payments"); // documented gap: no order/package target
    expect(keys).not.toContain("subjects");
  });

  it("oficina-mecanica (orders + vehicle subject) → orders + payments + subjects, no agenda/packages", () => {
    const keys = keysFor({
      orders: true,
      quotes: true,
      parts: true,
      subjectKind: "Vehicle" as SvcSubjectKind,
    });
    expect(keys).toContain("orders");
    expect(keys).toContain("payments"); // orders provide a payment target
    expect(keys).toContain("subjects");
    expect(keys).not.toContain("agenda");
    expect(keys).not.toContain("packages");
  });

  it("salao-beleza (appointments + packages) → agenda + packages + payments, no orders/subjects", () => {
    const keys = keysFor({ appointments: true, packages: true, commissions: true });
    expect(keys).toContain("agenda");
    expect(keys).toContain("packages");
    expect(keys).toContain("payments"); // packages provide a payment target
    expect(keys).not.toContain("orders");
    expect(keys).not.toContain("subjects");
  });

  it("pet-shop (appointments + packages + pet subject) → full set minus orders", () => {
    const keys = keysFor({
      appointments: true,
      packages: true,
      simpleRecord: true,
      subjectKind: "Pet" as SvcSubjectKind,
    });
    expect(keys).toEqual(
      expect.arrayContaining([
        "agenda",
        "packages",
        "payments",
        "professionals",
        "catalog",
        "subjects",
      ])
    );
    expect(keys).not.toContain("orders");
  });

  it("subjects surface is hidden when the preset has no subjectKind", () => {
    expect(keysFor({ appointments: true })).not.toContain("subjects");
    expect(keysFor({ subjectKind: "Student" as SvcSubjectKind })).toContain("subjects");
  });

  it("public booking keeps the Agenda discoverable even without the appointments capability", () => {
    // oficina-style (orders, no appointments) → Agenda hidden by default…
    expect(keysFor({ orders: true })).not.toContain("agenda");
    // …but visible once public booking is on (the portal creates appointments to manage).
    expect(keysForCtx({ orders: true }, { publicBookingEnabled: true })).toContain("agenda");
    // a vertical with appointments shows the Agenda regardless of booking.
    expect(keysForCtx({ appointments: true }, { publicBookingEnabled: false })).toContain("agenda");
  });

  it("subject label adapts to the preset subjectKind", () => {
    const subjects = SERVICE_SURFACES.find((s) => s.key === "subjects")!;
    expect(subjects.label(preset({ subjectKind: "Pet" as SvcSubjectKind }))).toBe("Pets");
    expect(subjects.label(preset({ subjectKind: "Vehicle" as SvcSubjectKind }))).toBe("Veículos");
    expect(subjects.label(preset({}))).toBe("Cadastros");
  });
});
