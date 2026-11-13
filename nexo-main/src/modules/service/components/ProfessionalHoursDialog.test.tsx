import { type ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fetchProfessionals, updateProfessional, type SvcProfessionalDto } from "../api/service.api";
import { useProfessionals } from "../hooks/useProfessionals";
import { ProfessionalHoursDialog } from "./ProfessionalHoursDialog";

vi.mock("../api/service.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/service.api")>()),
  fetchProfessionals: vi.fn(),
  updateProfessional: vi.fn(),
}));

const joao = {
  id: "p-1", storeId: "s", name: "João", role: "Barbeiro", specialty: "Degradê", color: "#0f766e", phone: "85999990000",
  email: "joao@barbearia.test", defaultCommissionPercent: 40, userId: null, isActive: true, createdAt: "2026-01-01T00:00:00Z",
  workingHoursJson: JSON.stringify([{ weekday: 1, windows: [{ start: "09:00", end: "18:00" }] }]),
} as SvcProfessionalDto;

// A lista de profissionais que fica atrás do diálogo na tela de equipe.
function TeamScreen({ onClose }: { onClose: () => void }) {
  const team = useProfessionals();
  return (
    <>
      <p>{team.data?.length ?? 0} na equipe</p>
      <ProfessionalHoursDialog professional={joao} professionalLabel="Barbeiro" onClose={onClose} />
    </>
  );
}

function renderScreen(onClose: () => void) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  return render(<TeamScreen onClose={onClose} />, { wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchProfessionals).mockResolvedValue([joao]);
});
afterEach(cleanup);

describe("ProfessionalHoursDialog", () => {
  it("saves the week with the day turned on, keeps the rest of the profile, closes and refreshes the team", async () => {
    vi.mocked(updateProfessional).mockResolvedValue(joao);
    const onClose = vi.fn();
    renderScreen(onClose);
    await screen.findByText("1 na equipe");

    fireEvent.click(await screen.findByRole("checkbox", { name: "Terça" }));
    fireEvent.click(screen.getByRole("button", { name: /salvar horários/i }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    const [id, body] = vi.mocked(updateProfessional).mock.calls[0];
    expect(id).toBe("p-1");
    expect(body).toMatchObject({
      name: "João", role: "Barbeiro", specialty: "Degradê", color: "#0f766e", phone: "85999990000",
      email: "joao@barbearia.test", defaultCommissionPercent: 40,
    });
    expect(JSON.parse(body.workingHoursJson!)).toEqual([
      { weekday: 1, windows: [{ start: "09:00", end: "18:00" }] },
      { weekday: 2, windows: [{ start: "09:00", end: "18:00" }] },
    ]);
    await waitFor(() => expect(fetchProfessionals).toHaveBeenCalledTimes(2));
  });

  it("a save the server refuses shows its reason, stays open and leaves the team as it was", async () => {
    vi.mocked(updateProfessional).mockRejectedValue(new Error("Horário fora do funcionamento da loja."));
    const onClose = vi.fn();
    renderScreen(onClose);
    await screen.findByText("1 na equipe");

    fireEvent.click(screen.getByRole("button", { name: /salvar horários/i }));

    expect(await screen.findByText("Horário fora do funcionamento da loja.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(fetchProfessionals).toHaveBeenCalledTimes(1);
  });
});
