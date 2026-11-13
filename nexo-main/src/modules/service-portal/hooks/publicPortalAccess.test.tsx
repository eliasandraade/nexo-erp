import { type ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { usePublicMenu } from "@/modules/portal/hooks/usePublicMenu";
import { BookingApiError } from "../api/booking.api";
import { useCatalog, useCreateAppointment, usePortal, useProfessionals } from "./usePublicBooking";

const API = "http://localhost:5000/api";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

// What an anonymous visitor gets from a public portal. Whoever has no module with access, or a slug that does not exist, gets a
// refusal from the server: these hooks must show it at once, and must not send the visitor's anonymous call again and again.
describe("public portals", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  function wrapper({ children }: { children: ReactNode }) {
    // the hooks have to bring their own retry: this default would retry a refusal three times
    const client = new QueryClient({ defaultOptions: { queries: { retry: 3, retryDelay: 1 } } });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  }

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  describe("booking", () => {
    it("reads the portal of that slug", async () => {
      fetchMock.mockResolvedValueOnce(json({ name: "Salão" }));

      const { result } = renderHook(() => usePortal("salao"), { wrapper });

      await waitFor(() => expect(result.current.data).toEqual({ name: "Salão" }));
      expect(fetchMock.mock.calls[0][0]).toBe(`${API}/public/service/salao`);
    });

    it("a refused portal shows the server's reason at once, with no second call", async () => {
      fetchMock.mockResolvedValue(json({ error: "Este portal não está disponível." }, 404));

      const { result } = renderHook(() => usePortal("salao"), { wrapper });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeInstanceOf(BookingApiError);
      expect((result.current.error as BookingApiError).status).toBe(404);
      expect(result.current.error?.message).toBe("Este portal não está disponível.");
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it.each([
      ["catalog", useCatalog, "catalog"],
      ["professionals", useProfessionals, "professionals"],
    ])("the %s waits for the portal to be open", async (_name, hook, route) => {
      const { rerender, result } = renderHook(({ enabled }) => hook("salao", enabled), { wrapper, initialProps: { enabled: false } });
      expect(fetchMock).not.toHaveBeenCalled();

      fetchMock.mockResolvedValueOnce(json([{ id: "1" }]));
      rerender({ enabled: true });

      await waitFor(() => expect(result.current.data).toEqual([{ id: "1" }]));
      expect(fetchMock.mock.calls[0][0]).toBe(`${API}/public/service/salao/${route}`);
    });

    it("an appointment goes to that portal, and a refusal comes back with its reason", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "Este horário acabou de ser reservado." }, 409));
      const { result } = renderHook(() => useCreateAppointment("salao"), { wrapper });

      let error: unknown;
      await act(async () => {
        error = await result.current.mutateAsync({ catalogItemId: "c1" } as never).catch((e: unknown) => e);
      });

      expect((error as BookingApiError).status).toBe(409);
      expect((error as BookingApiError).message).toBe("Este horário acabou de ser reservado.");
      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`${API}/public/service/salao/appointments`);
      expect(init.method).toBe("POST");
    });
  });

  describe("menu", () => {
    it("reads the menu of that slug", async () => {
      fetchMock.mockResolvedValueOnce(json({ storeName: "Lanchonete" }));

      const { result } = renderHook(() => usePublicMenu("lanchonete"), { wrapper });

      await waitFor(() => expect(result.current.data).toEqual({ storeName: "Lanchonete" }));
      expect(fetchMock.mock.calls[0][0]).toBe(`${API}/public/menu/lanchonete`);
    });

    it("a refused menu shows the error at once, with no second call", async () => {
      fetchMock.mockResolvedValue(json({ error: "Este cardápio não está disponível." }, 404));

      const { result } = renderHook(() => usePublicMenu("lanchonete"), { wrapper });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
  });
});
