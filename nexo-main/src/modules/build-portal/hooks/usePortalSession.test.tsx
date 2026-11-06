import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import type { PortalGateway } from "../api/gateway";
import type { Access } from "../types";
import { usePortalSession } from "./usePortalSession";

const READY = (extra: object = {}) => ({ kind: "ready", portal: { ...extra } }) as unknown as Access;
const LOCKED = { kind: "locked" } as unknown as Access;
const THROTTLED = (retryAt: string) => ({ kind: "throttled", retryAt }) as unknown as Access;

type Listener = (event: Access | "changed") => void;

function fakeGateway(overrides: Partial<PortalGateway> = {}) {
  let listener: Listener | undefined;
  const gateway: PortalGateway = {
    load: vi.fn(async () => READY()),
    unlock: vi.fn(async () => READY()),
    subscribe: vi.fn((l: Listener) => {
      listener = l;
      return unsubscribe;
    }),
    dispose: vi.fn(),
    ...overrides,
  };
  const unsubscribe = vi.fn();
  return { gateway, unsubscribe, emit: (event: Access | "changed") => listener?.(event) };
}

const render = (gateway: PortalGateway) => renderHook(() => usePortalSession(gateway, "empresa", "obra"));

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("usePortalSession, the client's access to the construction portal", () => {
  it("reads the portal when it opens", async () => {
    const { gateway } = fakeGateway();

    const { result } = render(gateway);

    await waitFor(() => expect(result.current.access).toEqual(READY()));
    expect(gateway.load).toHaveBeenCalledWith("empresa", "obra", expect.any(AbortSignal));
    expect(result.current.busy).toBe(false);
  });

  it("a failed read shows the portal as unavailable, instead of an endless wait", async () => {
    const { gateway } = fakeGateway({ load: vi.fn(async () => { throw new Error("down"); }) });

    const { result } = render(gateway);

    await waitFor(() => expect(result.current.access).toEqual({ kind: "unavailable" }));
    expect(result.current.busy).toBe(false);
  });

  it("the password goes through unlock, and only then the portal opens", async () => {
    const { gateway } = fakeGateway({ load: vi.fn(async () => LOCKED) });
    const { result } = render(gateway);
    await waitFor(() => expect(result.current.access).toEqual(LOCKED));

    await act(async () => { await result.current.run("senha-da-obra"); });

    expect(gateway.unlock).toHaveBeenCalledWith("empresa", "obra", "senha-da-obra", expect.any(AbortSignal));
    expect(result.current.access).toEqual(READY());
  });

  it("an answer that arrives after a newer request is ignored", async () => {
    let answerFirst: (a: Access) => void = () => undefined;
    const load = vi.fn()
      .mockImplementationOnce(() => new Promise<Access>((resolve) => { answerFirst = resolve; }))
      .mockResolvedValueOnce(LOCKED);
    const { gateway } = fakeGateway({ load });
    const { result } = render(gateway);

    await act(async () => { await result.current.run(); });
    expect(result.current.access).toEqual(LOCKED);

    await act(async () => { answerFirst(READY()); });

    expect(result.current.access).toEqual(LOCKED);
  });

  describe("what the session tells on its own", () => {
    it("a change reads again in the background, keeping what is on screen meanwhile", async () => {
      const { gateway, emit } = fakeGateway();
      const { result } = render(gateway);
      await waitFor(() => expect(result.current.access).toEqual(READY()));
      let answer: (a: Access) => void = () => undefined;
      vi.mocked(gateway.load).mockImplementationOnce(() => new Promise<Access>((resolve) => { answer = resolve; }));

      act(() => emit("changed"));

      expect(result.current.access).toEqual(READY());
      await act(async () => { answer(READY({ refreshed: true })); });
      expect(result.current.access).toEqual(READY({ refreshed: true }));
    });

    it("a session that ended closes the portal at once, and a read still on its way cannot reopen it", async () => {
      const { gateway, emit } = fakeGateway();
      const { result } = render(gateway);
      await waitFor(() => expect(result.current.access).toEqual(READY()));
      let answer: (a: Access) => void = () => undefined;
      vi.mocked(gateway.load).mockImplementationOnce(() => new Promise<Access>((resolve) => { answer = resolve; }));
      act(() => emit("changed"));

      act(() => emit(LOCKED));

      expect(result.current.access).toEqual(LOCKED);
      expect(result.current.busy).toBe(false);
      await act(async () => { answer(READY()); });
      expect(result.current.access).toEqual(LOCKED);
    });
  });

  it("leaving the page stops listening and releases the gateway", async () => {
    const { gateway, unsubscribe } = fakeGateway();
    const { result, unmount } = render(gateway);
    await waitFor(() => expect(result.current.access).toBeDefined());

    unmount();

    expect(unsubscribe).toHaveBeenCalledTimes(1);
    expect(gateway.dispose).toHaveBeenCalledTimes(1);
  });

  it("a gateway with no subscription still works", async () => {
    const { gateway } = fakeGateway({ subscribe: undefined, dispose: undefined });

    const { result, unmount } = render(gateway);

    await waitFor(() => expect(result.current.access).toEqual(READY()));
    expect(unmount).not.toThrow();
  });

  describe("renewing a link that is about to expire", () => {
    it("reads again at the time the portal said, in the background", async () => {
      vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      const { gateway } = fakeGateway({ load: vi.fn(async () => READY({ refreshAt: "2026-11-06T12:00:10Z" })) });
      const { result } = render(gateway);
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });
      expect(result.current.access).toBeDefined();
      expect(gateway.load).toHaveBeenCalledTimes(1);

      await act(async () => { await vi.advanceTimersByTimeAsync(9_000); });
      expect(gateway.load).toHaveBeenCalledTimes(1);

      await act(async () => { await vi.advanceTimersByTimeAsync(1_000); });
      expect(gateway.load).toHaveBeenCalledTimes(2);
    });

    it("coming back to the tab reads again only if that time has passed", async () => {
      vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      const { gateway } = fakeGateway({ load: vi.fn(async () => READY({ refreshAt: "2026-11-06T12:00:10Z" })) });
      render(gateway);
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });

      await act(async () => { window.dispatchEvent(new Event("focus")); });
      expect(gateway.load).toHaveBeenCalledTimes(1);

      vi.setSystemTime(new Date("2026-11-06T12:00:30Z"));
      await act(async () => { window.dispatchEvent(new Event("focus")); });
      expect(gateway.load).toHaveBeenCalledTimes(2);
    });

    it("after leaving the page no timer or focus reads anything", async () => {
      vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      const { gateway } = fakeGateway({ load: vi.fn(async () => READY({ refreshAt: "2026-11-06T12:00:10Z" })) });
      const { unmount } = render(gateway);
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });

      unmount();
      vi.setSystemTime(new Date("2026-11-06T12:00:30Z"));
      await vi.advanceTimersByTimeAsync(60_000);
      window.dispatchEvent(new Event("focus"));

      expect(gateway.load).toHaveBeenCalledTimes(1);
    });
  });

  it("too many wrong passwords: counts down the wait in seconds", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"] });
    vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
    const { gateway } = fakeGateway({ load: vi.fn(async () => THROTTLED("2026-11-06T12:00:05Z")) });
    const { result } = render(gateway);
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(result.current.retrySeconds).toBe(5);

    await act(async () => { await vi.advanceTimersByTimeAsync(2_000); });
    expect(result.current.retrySeconds).toBe(3);

    await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(result.current.retrySeconds).toBe(0);
  });
});
