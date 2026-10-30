import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NewProjectDialog } from "./NewProjectDialog";
import * as buildApi from "../../api/build.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  createProject: vi.fn(),
}));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

function renderDialog(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><MemoryRouter>{ui}</MemoryRouter></QueryClientProvider>);
}

function fillRequired() {
  fireEvent.change(screen.getByLabelText("Nome da obra *"), { target: { value: "Residencial Parque Sul" } });
  fireEvent.change(screen.getByLabelText("Cliente *"), { target: { value: "Maria" } });
}

describe("NewProjectDialog — tracking mode", () => {
  it("asks how to follow the obra and sends trackingMode=ThreeD with the site fields", async () => {
    vi.mocked(buildApi.createProject).mockResolvedValue({ id: "p1" } as buildApi.BuildProjectDto);
    renderDialog(<NewProjectDialog open onClose={() => {}} />);

    expect(screen.getByText("Como você deseja acompanhar esta obra?")).toBeInTheDocument();
    const standard = screen.getByRole("radio", { name: /Acompanhamento padrão/ });
    const threeD   = screen.getByRole("radio", { name: /Acompanhamento 3D/ });
    expect(standard).toHaveAttribute("aria-checked", "true");

    fillRequired();
    fireEvent.click(threeD);
    expect(threeD).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText(/abra a aba “Gêmeo digital” para enviar o projeto/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Latitude"), { target: { value: "-23,55" } });
    fireEvent.change(screen.getByLabelText("Longitude"), { target: { value: "-46.63" } });
    fireEvent.change(screen.getByLabelText("Área (m²)"), { target: { value: "250" } });
    fireEvent.click(screen.getByRole("button", { name: "Criar obra" }));

    await waitFor(() => expect(buildApi.createProject).toHaveBeenCalledTimes(1));
    expect(vi.mocked(buildApi.createProject).mock.calls[0][0]).toMatchObject({
      name: "Residencial Parque Sul", clientName: "Maria",
      trackingMode: "ThreeD", latitude: -23.55, longitude: -46.63, areaM2: 250,
    });
  });

  it("defaults to the standard mode", async () => {
    vi.mocked(buildApi.createProject).mockResolvedValue({ id: "p1" } as buildApi.BuildProjectDto);
    renderDialog(<NewProjectDialog open onClose={() => {}} />);
    fillRequired();
    fireEvent.click(screen.getByRole("button", { name: "Criar obra" }));
    await waitFor(() => expect(buildApi.createProject).toHaveBeenCalled());
    expect(vi.mocked(buildApi.createProject).mock.calls[0][0]).toMatchObject({ trackingMode: "Standard" });
  });

  it("blocks invalid coordinates (out of range, or only one of the two)", () => {
    renderDialog(<NewProjectDialog open onClose={() => {}} />);
    fillRequired();
    fireEvent.change(screen.getByLabelText("Latitude"), { target: { value: "95" } });
    fireEvent.change(screen.getByLabelText("Longitude"), { target: { value: "10" } });
    expect(screen.getByRole("alert")).toHaveTextContent("Latitude inválida");
    expect(screen.getByRole("button", { name: "Criar obra" })).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Latitude"), { target: { value: "" } });
    expect(screen.getByRole("alert")).toHaveTextContent("latitude e longitude juntas");
    expect(buildApi.createProject).not.toHaveBeenCalled();
  });
});
