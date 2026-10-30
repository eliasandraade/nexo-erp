import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { apiClient } from "@/services/api-client";
import PlatformSystemPage from "./PlatformSystemPage";
import * as platformApi from "../services/platformApi";
import { renderAi } from "../test/aiFixtures";

vi.mock("../services/platformApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../services/platformApi")>()),
  fetchApiEndpoints: vi.fn(),
  fetchPlatformHealth: vi.fn(),
  fetchPlatformErrors: vi.fn(),
}));

const endpoints = [
  { method: "GET", path: "/api/platform/stats", hasParameters: false, controller: "Platform", action: "Stats", description: "" },
  { method: "GET", path: "/api/platform/tenants/{tenantId}", hasParameters: true, controller: "Platform", action: "Tenant", description: "" },
  { method: "POST", path: "/api/platform/tenants", hasParameters: false, controller: "Platform", action: "Create", description: "" },
];

describe("PlatformSystemPage", () => {
  beforeEach(() => {
    vi.mocked(platformApi.fetchApiEndpoints).mockResolvedValue(endpoints);
    vi.mocked(platformApi.fetchPlatformErrors).mockResolvedValue([] as never);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it("offers Run only for GET routes without parameters and calls the path without the /api prefix", async () => {
    vi.mocked(platformApi.fetchPlatformHealth).mockResolvedValue({ status: "healthy", timestamp: "2026-10-30T10:00:00Z", checks: [] });
    const get = vi.spyOn(apiClient, "get").mockResolvedValue({ tenants: 3 });
    renderAi(<PlatformSystemPage />);

    await screen.findByText("/api/platform/tenants/{tenantId}");
    const runs = screen.getAllByRole("button", { name: /Run/ });
    expect(runs).toHaveLength(1);
    expect(screen.getByText("com parâmetros")).toBeInTheDocument();

    fireEvent.click(runs[0]);
    await waitFor(() => expect(get).toHaveBeenCalledWith("/platform/stats"));
  });

  it("only claims every system is fine when the server says healthy, and shows each check honestly", async () => {
    vi.mocked(platformApi.fetchPlatformHealth).mockResolvedValue({
      status: "degraded", timestamp: "2026-10-30T10:00:00Z",
      checks: [
        { name: "database", status: "healthy", latencyMs: 12 },
        { name: "cache", status: "unhealthy", latencyMs: 0 },
      ],
    });
    renderAi(<PlatformSystemPage />);
    expect(await screen.findByText("Sistema degradado")).toBeInTheDocument();
    expect(screen.queryByText("Todos os sistemas operacionais")).not.toBeInTheDocument();
    expect(screen.getByText("Banco de dados")).toBeInTheDocument();
    expect(screen.getByText(/Funcionando · 12ms/)).toBeInTheDocument();
    expect(screen.getByText("Com falha")).toBeInTheDocument();
  });

  it("shows a cache that is not configured as neutral", async () => {
    vi.mocked(platformApi.fetchPlatformHealth).mockResolvedValue({
      status: "healthy", timestamp: "2026-10-30T10:00:00Z",
      checks: [
        { name: "database", status: "healthy", latencyMs: 1 },
        { name: "cache", status: "not_configured", latencyMs: 0 },
      ],
    });
    renderAi(<PlatformSystemPage />);
    expect(await screen.findByText("Todos os sistemas operacionais")).toBeInTheDocument();
    expect(screen.getByText("Não configurado")).toBeInTheDocument();
  });
});

describe("clientPath", () => {
  it("drops the leading /api exactly once", () => {
    expect(platformApi.clientPath("/api/platform/stats")).toBe("/platform/stats");
    expect(platformApi.clientPath("/api/api/x")).toBe("/api/x");
    expect(platformApi.clientPath("/apiary")).toBe("/apiary");
    expect(platformApi.clientPath("/health")).toBe("/health");
  });
});
