import { useCallback, useEffect, useRef, useState } from "react";
import type { PortalGateway } from "../api/gateway";
import type { Access } from "../types";
export function usePortalSession(
  gateway: PortalGateway,
  tenant: string,
  project: string,
) {
  const [access, setAccess] = useState<Access>();
  const [busy, setBusy] = useState(false);
  const request = useRef<AbortController>();
  const sequence = useRef(0);
  const run = useCallback(
    async (password?: string, background = false) => {
      request.current?.abort();
      const controller = new AbortController();
      request.current = controller;
      const id = ++sequence.current;
      setBusy(true);
      if (!background) setAccess(undefined);
      try {
        const result =
          password === undefined
            ? await gateway.load(tenant, project, controller.signal)
            : await gateway.unlock(
                tenant,
                project,
                password,
                controller.signal,
              );
        if (!controller.signal.aborted && id === sequence.current)
          setAccess(result);
      } catch {
        if (!controller.signal.aborted && id === sequence.current)
          setAccess({ kind: "unavailable" });
      } finally {
        if (!controller.signal.aborted && id === sequence.current)
          setBusy(false);
      }
    },
    [gateway, tenant, project],
  );
  useEffect(() => {
    const unsubscribe = gateway.subscribe?.((event) => {
      if (event === "changed") {
        void run(undefined, true);
        return;
      }
      if (event.kind !== "ready") {
        request.current?.abort();
        sequence.current++;
        setBusy(false);
      }
      setAccess(event);
    });
    void run();
    return () => {
      request.current?.abort();
      unsubscribe?.();
      gateway.dispose?.();
    };
  }, [gateway, run]);
  const refreshAt =
    access?.kind === "ready" ? access.portal.refreshAt : undefined;
  useEffect(() => {
    if (!refreshAt) return;
    const refresh = () => {
      if (Date.now() >= Date.parse(refreshAt)) void run(undefined, true);
    };
    const timer = window.setTimeout(
      refresh,
      Math.max(0, Date.parse(refreshAt) - Date.now()),
    );
    window.addEventListener("focus", refresh);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("focus", refresh);
    };
  }, [refreshAt, run]);
  const [now, setNow] = useState(Date.now());
  const retryAt =
    access && access.kind !== "ready" ? access.retryAt : undefined;
  useEffect(() => {
    if (!retryAt) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [retryAt]);
  return {
    access,
    busy,
    run,
    retrySeconds: retryAt
      ? Math.max(0, Math.ceil((Date.parse(retryAt) - now) / 1000))
      : 0,
  };
}
