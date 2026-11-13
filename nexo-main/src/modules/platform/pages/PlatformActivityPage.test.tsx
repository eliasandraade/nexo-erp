import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import PlatformActivityPage from "./PlatformActivityPage";

const ACTOR = "11111111-1111-1111-1111-111111111111";
const TARGET = "22222222-2222-2222-2222-222222222222";

const record = {
  id: "r1", tenantId: "t1", actionType: "user_last_director_refused", severity: "warning", actorId: ACTOR, actorType: "user",
  entityType: "User", entityId: TARGET, description: "Alteração recusada: deixaria a empresa sem Diretoria ativa.",
  createdAt: "2026-11-05T12:00:00Z",
};

vi.mock("../hooks/usePlatformSystem", () => ({
  useAuditLog: () => ({ data: { total: 1, page: 1, pageSize: 25, records: [record] }, isLoading: false, isFetching: false, refetch: vi.fn() }),
}));
vi.mock("../hooks/usePlatformTenants", () => ({ usePlatformTenants: () => ({ data: [] }) }));

describe("the platform activity log", () => {
  it("names the refused change of the last Diretoria and can filter by it", () => {
    render(<PlatformActivityPage />);

    const label = "Alteração recusada (última Diretoria)";
    expect(within(screen.getByRole("table")).getByText(label)).toBeInTheDocument();
    expect(screen.getByRole("option", { name: label })).toBeInTheDocument();
  });

  it("identifies by the id an actor recorded without a name", () => {
    render(<PlatformActivityPage />);

    expect(within(screen.getByRole("table")).getByText(ACTOR)).toBeInTheDocument();
  });
});
