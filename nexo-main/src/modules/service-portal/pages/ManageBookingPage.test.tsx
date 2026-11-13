import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicAppointmentView } from "../api/booking.api";
import ManageBookingPage from "./ManageBookingPage";

const api = vi.hoisted(() => ({ getAppointmentByToken: vi.fn(), cancelAppointmentByToken: vi.fn() }));
const portal = vi.hoisted(() => ({ isBookingEnabled: true }));

vi.mock("../api/booking.api", async (original) => ({
  ...(await original<typeof import("../api/booking.api")>()),
  getAppointmentByToken: api.getAppointmentByToken,
  cancelAppointmentByToken: api.cancelAppointmentByToken,
}));
vi.mock("../hooks/usePublicBooking", () => ({
  usePortal: () => ({ data: { presetKey: "salao-beleza", isBookingEnabled: portal.isBookingEnabled } }),
}));

const view = (over: Partial<PublicAppointmentView> = {}): PublicAppointmentView => ({
  status: "Scheduled", startsAt: "2030-01-10T13:00:00Z", endsAt: "2030-01-10T14:00:00Z", serviceName: "Corte",
  professionalName: "Ana", storeName: "Studio", canCancel: true, cancelDeadline: "2030-01-10T12:30:00Z",
  handledByStore: false, onlineChangesPaused: false, ...over,
});

const open = () => render(
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <MemoryRouter initialEntries={["/agendar/studio/reserva/abc"]}>
      <Routes><Route path="/agendar/:slug/reserva/:token" element={<ManageBookingPage />} /></Routes>
    </MemoryRouter>
  </QueryClientProvider>,
);

describe("the customer's own link", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    portal.isBookingEnabled = true;
  });
  afterEach(cleanup);

  describe("with the company in read-only: booking is off and the customer can still cancel", () => {
    beforeEach(() => { portal.isBookingEnabled = false; });

    it("offers to cancel and says nothing about changes being unavailable", async () => {
      api.getAppointmentByToken.mockResolvedValue(view());
      open();

      expect(await screen.findByRole("button", { name: "Cancelar agendamento" })).toBeInTheDocument();
      expect(screen.queryByText(/indisponíveis/)).toBeNull();
    });

    it("does not promise the slot to someone else, who could not book it", async () => {
      api.getAppointmentByToken.mockResolvedValue(view());
      open();

      fireEvent.click(await screen.findByRole("button", { name: "Cancelar agendamento" }));

      expect(await screen.findByText("Cancelar este agendamento?")).toBeInTheDocument();
      expect(screen.queryByText(/livre para outra pessoa/)).toBeNull();
    });

    it("after cancelling, does not offer a new booking that would be refused", async () => {
      api.getAppointmentByToken.mockResolvedValue(view());
      api.cancelAppointmentByToken.mockResolvedValue(view({ status: "Cancelled", canCancel: false }));
      open();

      fireEvent.click(await screen.findByRole("button", { name: "Cancelar agendamento" }));
      fireEvent.click(await screen.findByRole("button", { name: "Sim, cancelar" }));

      expect(await screen.findByText("Cancelado")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Agendar outro horário" })).toBeNull();
    });
  });

  describe("with booking on", () => {
    it("keeps the words about the slot and the way to book another one", async () => {
      api.getAppointmentByToken.mockResolvedValue(view());
      api.cancelAppointmentByToken.mockResolvedValue(view({ status: "Cancelled", canCancel: false }));
      open();

      fireEvent.click(await screen.findByRole("button", { name: "Cancelar agendamento" }));
      expect(await screen.findByText(/livre para outra pessoa/)).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Sim, cancelar" }));

      expect(await screen.findByRole("link", { name: "Agendar outro horário" })).toBeInTheDocument();
    });
  });

  describe("when the customer cannot cancel by the link", () => {
    it("with changes paused (company out of activity or store inactive), says to talk to the store", async () => {
      api.getAppointmentByToken.mockResolvedValue(view({ canCancel: false, onlineChangesPaused: true }));
      open();

      expect(await screen.findByText(/alterações online estão indisponíveis/)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Cancelar agendamento" })).toBeNull();
    });

    it("past the deadline, says the deadline is over, not that changes are paused", async () => {
      api.getAppointmentByToken.mockResolvedValue(view({ canCancel: false }));
      open();

      expect(await screen.findByText(/prazo para cancelar por aqui terminou/)).toBeInTheDocument();
      expect(screen.queryByText(/indisponíveis/)).toBeNull();
    });

    it("with a comanda or a package, says the store handles it", async () => {
      api.getAppointmentByToken.mockResolvedValue(view({ canCancel: false, handledByStore: true }));
      open();

      await waitFor(() => expect(screen.getByText(/já tem pagamento ou comanda/)).toBeInTheDocument());
    });
  });
});
