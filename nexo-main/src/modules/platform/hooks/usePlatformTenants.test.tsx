import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError, apiClient } from "@/services/api-client";
import { ADMIN_ACTION_FORBIDDEN } from "../services/platformErrors";
import {
  useGrantModule, useRevokeAllSessions, useRevokeModule, useSetTenantStatus, useUpdateTenant,
} from "./usePlatformTenants";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);
const del = vi.mocked(apiClient.delete);

const LIST = ["platform", "tenants"];
const T1 = ["platform", "tenants", "t1"];
const T2 = ["platform", "tenants", "t2"];
const MRR = ["platform", "mrr"];
const SESSIONS = (userId: string) => ["platform", "tenants", "t1", "users", userId, "sessions"];

function setup() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  qc.setQueryData(LIST, [{ id: "t1" }, { id: "t2" }]);
  qc.setQueryData(T1, { id: "t1", status: "Active", modules: [] });
  qc.setQueryData(T2, { id: "t2", status: "Active", modules: [] });
  qc.setQueryData(MRR, { mrr: 1000 });
  qc.setQueryData(SESSIONS("u1"), [{ id: "s1" }]);
  qc.setQueryData(SESSIONS("u2"), [{ id: "s2" }]);
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  const stale = (key: readonly unknown[]) => qc.getQueryState(key)?.isInvalidated;
  return { wrapper, stale };
}

/** Empty 403: the server's answer when the super admin is not signed in with two-step verification. */
const forbidden = () => new ApiError(403, "Forbidden");

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("tenant administration", () => {
  it("editing a tenant saves the data and reloads the list and its page", async () => {
    put.mockResolvedValueOnce(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useUpdateTenant("t1"), { wrapper });

    const input = { companyName: "Padaria Pão Quente LTDA", taxId: "12345678000190", email: "contato@paoquente.test" };
    await act(() => result.current.mutateAsync(input));

    expect(put).toHaveBeenCalledWith("/platform/tenants/t1", input);
    expect(stale(LIST)).toBe(true);
    expect(stale(T1)).toBe(true);
    expect(stale(MRR)).toBe(false);
  });

  it("an edit refused for lack of two-step verification says why", async () => {
    put.mockRejectedValueOnce(forbidden());
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useUpdateTenant("t1"), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ companyName: "X", taxId: "1", email: "x@x.test" }).catch(() => {});
    });

    expect(toast.error).toHaveBeenCalledWith(ADMIN_ACTION_FORBIDDEN);
    expect(stale(T1)).toBe(false);
  });

  it("suspending and reactivating send the status and confirm each one in its own words", async () => {
    put.mockResolvedValue(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useSetTenantStatus("t1"), { wrapper });

    await act(() => result.current.mutateAsync("Suspended"));
    expect(put).toHaveBeenLastCalledWith("/platform/tenants/t1/status", { status: "Suspended" });
    expect(toast.success).toHaveBeenLastCalledWith("Cliente suspenso.");
    expect(stale(LIST)).toBe(true);
    expect(stale(T1)).toBe(true);

    await act(() => result.current.mutateAsync("Active"));
    expect(put).toHaveBeenLastCalledWith("/platform/tenants/t1/status", { status: "Active" });
    expect(toast.success).toHaveBeenLastCalledWith("Cliente reativado.");
  });

  it("a status change refused by the server shows the server's reason", async () => {
    put.mockRejectedValueOnce(new ApiError(409, "Conflict", undefined, undefined, undefined, "Cliente já está suspenso."));
    const { wrapper } = setup();
    const { result } = renderHook(() => useSetTenantStatus("t1"), { wrapper });

    await act(async () => { await result.current.mutateAsync("Suspended").catch(() => {}); });

    expect(toast.error).toHaveBeenCalledWith("Cliente já está suspenso.");
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("granting a module records the note and reloads only that tenant", async () => {
    post.mockResolvedValueOnce(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useGrantModule("t1"), { wrapper });

    await act(() => result.current.mutateAsync({ moduleKey: "fiscal", notes: "cortesia de 30 dias" }));

    expect(post).toHaveBeenCalledWith("/platform/tenants/t1/modules", {
      moduleKey: "fiscal", expiresAt: undefined, notes: "cortesia de 30 dias",
    });
    expect(toast.success).toHaveBeenCalledWith("Módulo ativado.");
    expect(stale(T1)).toBe(true);
    expect(stale(T2)).toBe(false);
  });

  it("a module grant refused for lack of two-step verification says why", async () => {
    post.mockRejectedValueOnce(forbidden());
    const { wrapper } = setup();
    const { result } = renderHook(() => useGrantModule("t1"), { wrapper });

    await act(async () => { await result.current.mutateAsync({ moduleKey: "fiscal" }).catch(() => {}); });

    expect(toast.error).toHaveBeenCalledWith(ADMIN_ACTION_FORBIDDEN);
  });

  it("revoking a module removes it on the server and reloads only that tenant", async () => {
    del.mockResolvedValueOnce(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useRevokeModule("t1"), { wrapper });

    await act(() => result.current.mutateAsync("fiscal"));

    expect(del).toHaveBeenCalledWith("/platform/tenants/t1/modules/fiscal");
    expect(toast.success).toHaveBeenCalledWith("Módulo revogado.");
    expect(stale(T1)).toBe(true);
    expect(stale(T2)).toBe(false);
  });

  it("revoking a user's sessions disconnects that user and reloads only their sessions", async () => {
    del.mockResolvedValueOnce(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useRevokeAllSessions("t1"), { wrapper });

    await act(() => result.current.mutateAsync("u1"));

    expect(del).toHaveBeenCalledWith("/platform/tenants/t1/users/u1/sessions");
    expect(toast.success).toHaveBeenCalledWith("Sessões revogadas. O usuário foi desconectado.");
    expect(stale(SESSIONS("u1"))).toBe(true);
    expect(stale(SESSIONS("u2"))).toBe(false);
  });

  it("a failed session revocation tells the operator and keeps the sessions listed", async () => {
    del.mockRejectedValueOnce(new Error("Falha de rede."));
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useRevokeAllSessions("t1"), { wrapper });

    await act(async () => { await result.current.mutateAsync("u1").catch(() => {}); });

    expect(toast.error).toHaveBeenCalledWith("Falha de rede.");
    expect(stale(SESSIONS("u1"))).toBe(false);
  });
});
