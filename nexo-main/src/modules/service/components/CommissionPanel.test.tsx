import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { SvcProfessionalDto } from "../api/service.api";
import { CommissionPanel } from "./CommissionPanel";

const session = { role: "gerente" as string };

vi.mock("@/modules/auth/context/AuthContext", () => ({
  useAuth: () => ({ session }),
}));

vi.mock("../context/ServicePresetContext", () => ({
  useServicePreset: () => ({
    labels: { customer: "Cliente", professional: "Barbeiro", catalogItem: "Serviço",
      appointment: "Agendamento", order: "Comanda", subject: "—" },
    capabilities: { appointments: true, orders: true, packages: true, commissions: true, subjectKind: null },
  }),
}));

vi.mock("../api/service.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/service.api")>()),
  fetchCommissionSummary: vi.fn().mockResolvedValue([
    { professionalId: "p1", openCount: 2, openAmount: 45, pendingPayoutCount: 1, pendingPayoutAmount: 30,
      paidPayoutCount: 1, paidPayoutAmount: 120 },
  ]),
  fetchCommissionEntries: vi.fn().mockResolvedValue([
    { id: "e1", storeId: "s", professionalId: "p1", customerId: "c", source: "OrderItem", sourceId: "i1",
      baseAmount: 100, commissionPercent: 30, commissionAmount: 30, recognizedAt: new Date().toISOString(),
      payoutId: null, description: "OS-1 · Corte", createdAt: new Date().toISOString() },
    { id: "e2", storeId: "s", professionalId: "p1", customerId: "c", source: "PackageUsage", sourceId: "u1",
      baseAmount: 50, commissionPercent: 30, commissionAmount: 15, recognizedAt: new Date().toISOString(),
      payoutId: null, description: "PKG-1 · Barba", createdAt: new Date().toISOString() },
  ]),
  fetchCommissionPayouts: vi.fn().mockResolvedValue([
    { id: "po1", storeId: "s", professionalId: "p1", periodStart: "2026-09-01T03:00:00Z", periodEnd: "2026-09-30T23:59:59Z",
      totalAmount: 30, entryCount: 1, status: "Pending", paidAt: null, notes: null,
      createdAt: "2026-10-01T00:00:00Z", updatedAt: "2026-10-01T00:00:00Z" },
  ]),
}));

const professionals: SvcProfessionalDto[] = [
  { id: "p1", storeId: "s", name: "João Barbeiro", role: null, specialty: null, color: null, phone: null,
    email: null, defaultCommissionPercent: 30, userId: null, isActive: true } as SvcProfessionalDto,
];

function renderWithClient(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

beforeEach(() => { session.role = "gerente"; });
afterEach(cleanup);

describe("CommissionPanel", () => {
  it("shows each professional's position and the open entries of the period", async () => {
    renderWithClient(<CommissionPanel professionals={professionals} />);

    expect(await screen.findAllByText("João Barbeiro")).not.toHaveLength(0);
    expect(await screen.findByText("OS-1 · Corte")).toBeInTheDocument();
    expect(screen.getByText("PKG-1 · Barba")).toBeInTheDocument();
    // Source labels follow the preset (Comanda) and the open total sums the period entries.
    expect(screen.getByText("Comanda")).toBeInTheDocument();
    expect(screen.getByText("Em aberto no período")).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByText(/45,00/).length).toBeGreaterThan(0));
  });

  it("lets a manager close the period", async () => {
    renderWithClient(<CommissionPanel professionals={professionals} />);
    await screen.findByText("OS-1 · Corte");
    expect(screen.getByRole("button", { name: /Fechar período/ })).toBeEnabled();
  });

  it("hides close/pay actions from non-managers", async () => {
    session.role = "vendedor";
    renderWithClient(<CommissionPanel professionals={professionals} />);
    await screen.findByText("OS-1 · Corte");
    expect(screen.queryByRole("button", { name: /Fechar período/ })).not.toBeInTheDocument();
    expect(screen.getByText(/restrito a gerência e diretoria/)).toBeInTheDocument();
  });
});
