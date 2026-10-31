import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DailyLogClientVisibility, PhotoClientVisibilityToggle } from "./DailyLogClientVisibility";
import * as portalApi from "../../api/build-portal.api";
import type { BuildDailyLogDto } from "../../api/build.api";

vi.mock("../../api/build-portal.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-portal.api")>()),
  setDailyLogClientVisibility: vi.fn(),
  setPhotoClientVisibility:    vi.fn(),
}));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

const log: BuildDailyLogDto = {
  id: "log1", projectId: "p1", date: "2026-10-29", weatherSummary: null,
  notes: "Nota interna: atraso do fornecedor de aço", createdAt: "", updatedAt: "", photos: [],
  clientVisible: false, clientSummary: null,
};

function renderWith(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("DailyLogClientVisibility", () => {
  it("turning on without a summary asks for it first, then sends visible + summary", async () => {
    vi.mocked(portalApi.setDailyLogClientVisibility).mockResolvedValue({ ...log, clientVisible: true });
    renderWith(<DailyLogClientVisibility projectId="p1" log={log} />);

    fireEvent.click(screen.getByRole("switch", { name: /Visível ao cliente/ }));
    expect(portalApi.setDailyLogClientVisibility).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("Escreva o resumo para o cliente");
    expect(screen.getByText(/nunca vê as notas internas/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Resumo para o cliente"), { target: { value: "Laje do 1º pavimento concluída." } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar e mostrar ao cliente" }));

    await waitFor(() => expect(portalApi.setDailyLogClientVisibility).toHaveBeenCalledWith("log1", {
      clientVisible: true, clientSummary: "Laje do 1º pavimento concluída.",
    }));
  });

  it("turning off a visible log keeps its summary", async () => {
    const visible = { ...log, clientVisible: true, clientSummary: "Concretagem feita." };
    vi.mocked(portalApi.setDailyLogClientVisibility).mockResolvedValue({ ...visible, clientVisible: false });
    renderWith(<DailyLogClientVisibility projectId="p1" log={visible} />);

    expect(screen.getByText("Concretagem feita.")).toBeInTheDocument();
    const toggle = screen.getByRole("switch", { name: /Visível ao cliente/ });
    expect(toggle).toHaveAttribute("aria-checked", "true");
    fireEvent.click(toggle);
    await waitFor(() => expect(portalApi.setDailyLogClientVisibility).toHaveBeenCalledWith("log1", {
      clientVisible: false, clientSummary: "Concretagem feita.",
    }));
  });

  it("the photo toggle flips the photo's visibility", async () => {
    vi.mocked(portalApi.setPhotoClientVisibility).mockResolvedValue({} as never);
    renderWith(
      <PhotoClientVisibilityToggle projectId="p1" logVisible
        photo={{ id: "ph1", dailyLogId: "log1", storageKey: "k", url: null, caption: null, createdAt: "", clientVisible: false }} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Foto oculta do cliente" }));
    await waitFor(() => expect(portalApi.setPhotoClientVisibility).toHaveBeenCalledWith("ph1", true));
  });
});
