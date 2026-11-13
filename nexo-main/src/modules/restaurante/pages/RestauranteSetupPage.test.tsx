import { type ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { AreaDto, TableDto } from "../types";
import { createArea, createTable, listAreas, listTables, updateArea, updateTable } from "../api/restaurante.api";
import RestauranteSetupPage from "./RestauranteSetupPage";

// Um servidor em memória: a página só mostra a mudança se buscar a lista de novo depois de salvar.
const db = vi.hoisted(() => ({ areas: [] as AreaDto[], tables: [] as TableDto[] }));

vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { storeId: "loja-1" } }) }));
vi.mock("../api/restaurante.api", () => ({
  listAreas: vi.fn(async () => db.areas.map((a) => ({ ...a }))),
  listTables: vi.fn(async () => db.tables.map((t) => ({ ...t }))),
  createArea: vi.fn(async (req: { name: string }) => {
    const area: AreaDto = { id: `area-${db.areas.length + 1}`, name: req.name, description: null, isActive: true, tableCount: 0 };
    db.areas.push(area);
    return area;
  }),
  updateArea: vi.fn(async (id: string, req: Pick<AreaDto, "name" | "description" | "isActive">) => {
    const area = db.areas.find((a) => a.id === id)!;
    Object.assign(area, req);
    return area;
  }),
  createTable: vi.fn(async (req: { areaId: string; number: string; capacity: number }) => {
    const table: TableDto = { id: `mesa-${db.tables.length + 1}`, areaName: "", status: "Available", isActive: true, ...req };
    db.tables.push(table);
    return table;
  }),
  updateTable: vi.fn(async (id: string, req: Pick<TableDto, "areaId" | "number" | "capacity" | "isActive">) => {
    const table = db.tables.find((t) => t.id === id)!;
    Object.assign(table, req);
    return table;
  }),
}));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  return render(<RestauranteSetupPage />, { wrapper });
}

/** Os botões de ação da linha são só ícones: acha pelo ícone dentro da linha. */
function iconButton(row: HTMLElement, icon: "pencil" | "check" | "chevron-right") {
  const svg = row.querySelector(`svg.lucide-${icon}`);
  if (!svg) throw new Error(`sem botão ${icon} na linha`);
  return svg.closest("button")!;
}

const areaRow = async (name: string) => (await screen.findByText(name)).parentElement!;

beforeEach(() => {
  vi.clearAllMocks();
  db.areas = [{ id: "area-1", name: "Salão", description: "Térreo", isActive: true, tableCount: 1 }];
  db.tables = [{ id: "mesa-1", areaId: "area-1", areaName: "Salão", number: "1", capacity: 4, status: "Available", isActive: true }];
});
afterEach(cleanup);

describe("RestauranteSetupPage", () => {
  it("a new area is saved and shows up in the list", async () => {
    renderPage();
    await screen.findByText("Salão");

    fireEvent.click(screen.getByRole("button", { name: /nova área/i }));
    const input = screen.getByPlaceholderText("Nome da área (ex: Salão, Terraço)");
    fireEvent.change(input, { target: { value: "  Terraço " } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(await screen.findByText("Terraço")).toBeInTheDocument();
    expect(createArea).toHaveBeenCalledWith({ name: "Terraço" });
    expect(screen.queryByPlaceholderText("Nome da área (ex: Salão, Terraço)")).not.toBeInTheDocument();
  });

  it("an area the server refuses keeps the form open with what was typed", async () => {
    vi.mocked(createArea).mockRejectedValueOnce(new Error("Já existe uma área com esse nome."));
    renderPage();
    await screen.findByText("Salão");

    fireEvent.click(screen.getByRole("button", { name: /nova área/i }));
    const input = screen.getByPlaceholderText("Nome da área (ex: Salão, Terraço)");
    fireEvent.change(input, { target: { value: "Salão" } });
    fireEvent.keyDown(input, { key: "Enter" });

    await waitFor(() => expect(createArea).toHaveBeenCalledTimes(1));
    expect(screen.getByPlaceholderText("Nome da área (ex: Salão, Terraço)")).toHaveValue("Salão");
    expect(listAreas).toHaveBeenCalledTimes(1);
  });

  it("renaming an area keeps its description and status and shows the new name", async () => {
    renderPage();
    fireEvent.click(iconButton(await areaRow("Salão"), "pencil"));

    const input = screen.getByDisplayValue("Salão");
    fireEvent.change(input, { target: { value: "Salão principal" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(await screen.findByText("Salão principal")).toBeInTheDocument();
    expect(updateArea).toHaveBeenCalledWith("area-1", { name: "Salão principal", description: "Térreo", isActive: true });
    expect(screen.queryByDisplayValue("Salão principal")).not.toBeInTheDocument();
  });

  it("a table added to an area is saved with its capacity and counted in the area", async () => {
    renderPage();
    const row = await areaRow("Salão");
    await within(row).findByText("1 mesa(s)");
    fireEvent.click(iconButton(row, "chevron-right"));

    fireEvent.click(screen.getByRole("button", { name: /adicionar mesa/i }));
    fireEvent.change(screen.getByPlaceholderText("Nº mesa (ex: 1)"), { target: { value: " 5 " } });
    fireEvent.change(screen.getByPlaceholderText("Capacidade"), { target: { value: "6" } });
    fireEvent.click(iconButton(screen.getByPlaceholderText("Nº mesa (ex: 1)").parentElement!, "check"));

    expect(await within(row).findByText("2 mesa(s)")).toBeInTheDocument();
    expect(createTable).toHaveBeenCalledWith({ areaId: "area-1", number: "5", capacity: 6 });
    expect(screen.getByText("· 6 pessoas")).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Nº mesa (ex: 1)")).not.toBeInTheDocument();
  });

  it("editing a table saves the new capacity and shows it", async () => {
    renderPage();
    fireEvent.click(iconButton(await areaRow("Salão"), "chevron-right"));
    const tableRow = (await screen.findByText("· 4 pessoas")).closest("div")!;

    fireEvent.click(iconButton(tableRow, "pencil"));
    fireEvent.change(screen.getByDisplayValue("4"), { target: { value: "8" } });
    fireEvent.click(iconButton(tableRow, "check"));

    expect(await screen.findByText("· 8 pessoas")).toBeInTheDocument();
    expect(updateTable).toHaveBeenCalledWith("mesa-1", { areaId: "area-1", number: "1", capacity: 8, isActive: true });
    expect(listTables).toHaveBeenCalledTimes(2);
  });
});
