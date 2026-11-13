import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "./api-client";

/**
 * A screen builds its API call from a value of the address bar (`/sales/${id}`), and the router decodes that value. A crafted link
 * must not turn it into a call to another route of the API, with the session of whoever clicked.
 */
describe("API paths", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it.each([
    "/sales/../../users/1",
    "/sales/..",
    "/../users",
    "/sales/%2e%2e/users",
    "/sales/%2E%2E/users",
    "/sales/.%2e/users",
    "/sales/..\\users",
  ])("refuses %s before it reaches the network", async (path) => {
    await expect(apiClient.get(path)).rejects.toMatchObject({ status: 400 });
    await expect(apiClient.post(path, {})).rejects.toMatchObject({ status: 400 });

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    "/sales/123",
    "/reports/sales/export?from=2026-01-01&to=2026-01-31",
    "/produtos?busca=a..b",
    "/produtos?busca=../x",
    "/files/nome..com.pontos.pdf",
  ])("sends %s as it is", async (path) => {
    await apiClient.get(path);

    expect(String(fetchMock.mock.calls[0][0])).toContain(path);
  });
});
