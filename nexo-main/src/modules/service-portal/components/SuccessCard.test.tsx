import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import { SuccessCard } from "./SuccessCard";
import type { PublicAppointmentCreated } from "../api/booking.api";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const created = {
  appointmentId: "a1",
  status: "Confirmed",
  serviceName: "Corte",
  professionalName: "Ana",
  customerName: "Bia",
  startsAt: "2026-11-10T13:00:00Z",
  endsAt: "2026-11-10T13:30:00Z",
} as unknown as PublicAppointmentCreated;

function stubClipboard(writeText: () => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

function renderCard() {
  render(<MemoryRouter><SuccessCard created={created} storeName="Studio" onRestart={() => {}} /></MemoryRouter>);
}

beforeEach(() => vi.mocked(toast.error).mockClear());
afterEach(cleanup);

describe("SuccessCard copy", () => {
  it("shows the confirmation only after the browser copied", async () => {
    stubClipboard(vi.fn().mockResolvedValue(undefined));
    renderCard();
    fireEvent.click(screen.getByRole("button", { name: /copiar/i }));
    expect(await screen.findByRole("button", { name: /copiado/i })).toBeInTheDocument();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("tells the customer when the browser refuses", async () => {
    stubClipboard(vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")));
    renderCard();
    fireEvent.click(screen.getByRole("button", { name: /copiar/i }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("button", { name: /copiado/i })).not.toBeInTheDocument();
  });
});
