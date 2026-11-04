import { describe, it, expect, afterEach } from "vitest";
import { HandoffError, newHandoffNonce, offerImpersonation, requestImpersonation } from "./impersonation-handoff";
import type { ImpersonationGrant } from "@/modules/auth/services/authService";

const grant: ImpersonationGrant = {
  accessToken: "imp-token",
  expiresAt: "2026-10-30T12:30:00Z",
  session: {
    userId: "u1", tenantId: "t1", name: "Ana", role: "diretoria", login: "ana", email: "ana@x.com",
    activeModules: ["varejo"], storeId: "s1", storeIds: ["s1"], companyName: "Padaria", type: "tenant",
  } as ImpersonationGrant["session"],
};

describe("impersonation hand-off (BroadcastChannel)", () => {
  const cancels: (() => void)[] = [];
  afterEach(() => { cancels.splice(0).forEach(c => c()); });

  it("creates unguessable hex nonces", () => {
    const a = newHandoffNonce();
    expect(a).toMatch(/^[0-9a-f]{32}$/);
    expect(newHandoffNonce()).not.toBe(a);
  });

  it("hands the grant only to the tab that asks with the same nonce, after the server answered", async () => {
    let resolveGrant!: (g: ImpersonationGrant) => void;
    cancels.push(offerImpersonation("nonce-1", new Promise(r => { resolveGrant = r; })));

    const other = requestImpersonation("other-nonce", { timeoutMs: 400 });
    const mine = requestImpersonation("nonce-1", { timeoutMs: 2000 });
    setTimeout(() => resolveGrant(grant), 50);

    await expect(mine).resolves.toEqual(grant);
    await expect(other).rejects.toMatchObject({ reason: "timeout" });
  });

  it("passes the failure message on when the server refused", async () => {
    cancels.push(offerImpersonation("nonce-2", Promise.reject(new Error("A empresa não está ativa."))));
    await expect(requestImpersonation("nonce-2", { timeoutMs: 2000 }))
      .rejects.toEqual(new HandoffError("failed", "A empresa não está ativa."));
  });

  it("drops the token after the timeout: a late request gets nothing", async () => {
    offerImpersonation("nonce-3", Promise.resolve(grant), 30);
    await new Promise(r => setTimeout(r, 80));
    await expect(requestImpersonation("nonce-3", { timeoutMs: 300 })).rejects.toMatchObject({ reason: "timeout" });
  });
});
