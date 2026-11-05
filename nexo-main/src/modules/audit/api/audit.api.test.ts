import { describe, it, expect, vi } from "vitest";
import { apiClient } from "@/services/api-client";
import { fetchAuditActors } from "./audit.api";

vi.mock("@/services/api-client", () => ({ apiClient: { get: vi.fn() } }));

const record = (actorName: string, id: string) => ({
  id,
  timestamp: "2026-11-04T12:00:00Z",
  actionType: "Login",
  severity: "Info",
  actorName,
  actorType: "User",
});

describe("fetchAuditActors", () => {
  it("lists each actor once, in Portuguese alphabetical order", async () => {
    vi.mocked(apiClient.get).mockResolvedValue([
      record("Zeca", "1"), record("Álvaro", "2"), record("bruno", "3"), record("Zeca", "4"),
    ]);
    await expect(fetchAuditActors()).resolves.toEqual(["Álvaro", "bruno", "Zeca"]);
  });
});
