import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import { PortalBookingSettingsForm } from "./PortalBookingSettingsForm";
import type { PublicBookingSettingsDto } from "../api/service.api";

const mutate = vi.fn();
vi.mock("../hooks/usePublicBookingSettings", () => ({
  useUpdatePublicBookingSettings: () => ({ mutate, isPending: false, isSuccess: false, isError: false }),
}));

// Stub the radix Select (its open interaction needs pointer APIs jsdom lacks); we only test
// the toggle + save behaviour here, not the dropdown.
vi.mock("@/components/ui/select", () => ({
  Select: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  SelectValue: () => <span />,
  SelectContent: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
}));

const SETTINGS: PublicBookingSettingsDto = {
  isConfigured: true, publicBookingEnabled: false, bookingDaysAhead: 14,
  minLeadMinutes: 120, slotIntervalMinutes: 30, showPrices: true,
  autoConfirmAppointments: false, timeZoneId: "America/Sao_Paulo",
};

beforeEach(() => mutate.mockClear());
afterEach(cleanup);

describe("PortalBookingSettingsForm", () => {
  it("save calls the mutation with the current settings", () => {
    render(<PortalBookingSettingsForm settings={SETTINGS} />);
    fireEvent.click(screen.getByRole("button", { name: /Salvar configurações/ }));
    expect(mutate).toHaveBeenCalledTimes(1);
    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({
      publicBookingEnabled: false,
      bookingDaysAhead: 14,
      minLeadMinutes: 120,
      slotIntervalMinutes: 30,
      showPrices: true,
      autoConfirmAppointments: false,
      timeZoneId: "America/Sao_Paulo",
    }));
  });

  it("toggling public booking on is reflected in the saved payload", () => {
    render(<PortalBookingSettingsForm settings={SETTINGS} />);
    fireEvent.click(screen.getByRole("button", { name: /Agendamento público ativo/ }));
    fireEvent.click(screen.getByRole("button", { name: /Salvar configurações/ }));
    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ publicBookingEnabled: true }));
  });
});
