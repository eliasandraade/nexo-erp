/**
 * Hand-off of an impersonation token from the platform tab to the new tenant tab — without any storage.
 *
 * The platform tab opens `/impersonate?k=<nonce>` synchronously (inside the click, so no popup blocker), then
 * asks the server for the token. The new tab asks on a same-origin BroadcastChannel for "its" nonce and the
 * platform tab answers that request only, once. The token never touches localStorage/sessionStorage of the
 * platform tab, and is dropped from memory when handed over or after {@link HANDOFF_TIMEOUT_MS}.
 */
import type { ImpersonationGrant } from "@/modules/auth/services/authService";

export const IMPERSONATION_CHANNEL = "orken-impersonation";
export const HANDOFF_TIMEOUT_MS = 10_000;
/** How often the new tab repeats its request while the platform tab is still waiting for the server. */
const ASK_INTERVAL_MS = 500;

type HandoffMessage =
  | { type: "request"; nonce: string }
  | { type: "grant"; nonce: string; grant: ImpersonationGrant }
  | { type: "fail"; nonce: string; message: string };

/** Unguessable one-time id of a hand-off (crypto, never Math.random). */
export function newHandoffNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
}

/**
 * Platform tab: answers the new tab's request for `nonce` with the grant once `grant` resolves (or with the
 * failure message when it rejects). Gives up after the timeout. Returns a cancel function.
 */
export function offerImpersonation(
  nonce: string,
  grant: Promise<ImpersonationGrant>,
  timeoutMs = HANDOFF_TIMEOUT_MS
): () => void {
  const channel = new BroadcastChannel(IMPERSONATION_CHANNEL);
  let ready: ImpersonationGrant | null = null;
  let failure: string | null = null;
  let requested = false;
  let done = false;

  const finish = () => {
    if (done) return;
    done = true;
    ready = null;   // the token does not outlive the hand-off
    clearTimeout(timer);
    channel.close();
  };

  const answer = () => {
    if (done || !requested) return;
    if (ready) {
      channel.postMessage({ type: "grant", nonce, grant: ready } satisfies HandoffMessage);
      finish();
    } else if (failure !== null) {
      channel.postMessage({ type: "fail", nonce, message: failure } satisfies HandoffMessage);
      finish();
    }
  };

  channel.onmessage = (e: MessageEvent<HandoffMessage>) => {
    if (e.data?.type === "request" && e.data.nonce === nonce) {
      requested = true;
      answer();
    }
  };

  const timer = setTimeout(finish, timeoutMs);

  grant.then(
    g => { if (!done) { ready = g; answer(); } },
    (err: unknown) => {
      if (done) return;
      failure = err instanceof Error && err.message ? err.message : "Não foi possível entrar na empresa.";
      answer();
    }
  );

  return finish;
}

export class HandoffError extends Error {
  constructor(public readonly reason: "timeout" | "failed", message: string) {
    super(message);
    this.name = "HandoffError";
  }
}

/** New tab: asks for the grant of `nonce`; rejects with {@link HandoffError} on timeout or when the platform tab failed. */
export function requestImpersonation(
  nonce: string,
  { timeoutMs = HANDOFF_TIMEOUT_MS, signal }: { timeoutMs?: number; signal?: AbortSignal } = {}
): Promise<ImpersonationGrant> {
  return new Promise((resolve, reject) => {
    const channel = new BroadcastChannel(IMPERSONATION_CHANNEL);
    const ask = () => channel.postMessage({ type: "request", nonce } satisfies HandoffMessage);

    const cleanup = () => {
      clearInterval(interval);
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
      channel.close();
    };
    // the page went away before an answer: stop asking, so no other request of this nonce is left behind
    const onAbort = () => {
      cleanup();
      reject(new HandoffError("timeout", "Pedido de acesso cancelado."));
    };
    signal?.addEventListener("abort", onAbort);

    channel.onmessage = (e: MessageEvent<HandoffMessage>) => {
      const m = e.data;
      if (!m || m.nonce !== nonce) return;
      if (m.type === "grant") {
        cleanup();
        resolve(m.grant);
      } else if (m.type === "fail") {
        cleanup();
        reject(new HandoffError("failed", m.message));
      }
    };

    const interval = setInterval(ask, ASK_INTERVAL_MS);
    const timer = setTimeout(() => {
      cleanup();
      reject(new HandoffError("timeout", "O acesso não chegou a tempo."));
    }, timeoutMs);
    ask();
  });
}
