import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient } from "@/services/api-client";
import * as b from "./build.api";
import * as ai from "./build-ai.api";
import * as portal from "./build-portal.api";

// The HTTP layer is mocked: these tests pin the route, verb and body each API function sends.
vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: {
    get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn(), postForm: vi.fn(), upload: vi.fn(),
  },
}));

type Verb = "get" | "post" | "put" | "delete";
type Case = [name: string, call: () => Promise<unknown>, verb: Verb, url: string, body?: unknown];

const B = "/v1/build";
const ok = { ok: true };

function runCases(cases: Case[]) {
  it.each(cases)("%s", async (_name, call, verb, url, body) => {
    vi.mocked(apiClient[verb]).mockResolvedValue(ok as never);
    await expect(call()).resolves.toBe(ok);
    const args = vi.mocked(apiClient[verb]).mock.calls.at(-1)!;
    expect(args[0]).toBe(url);
    if (verb === "post" || verb === "put") expect(args[1]).toEqual(body);
  });
}

beforeEach(() => vi.clearAllMocks());

describe("build.api", () => {
  runCases([
    ["fetchProjects without params", () => b.fetchProjects(), "get", `${B}/projects`],
    ["fetchProjects with params", () => b.fetchProjects({ status: "InProgress" as never, page: 2, pageSize: 20 }), "get",
      `${B}/projects?status=InProgress&page=2&pageSize=20`],
    ["fetchProject", () => b.fetchProject("p1"), "get", `${B}/projects/p1`],
    ["fetchProjectDetails", () => b.fetchProjectDetails("p1"), "get", `${B}/projects/p1/details`],
    ["fetchProjectFinancialSummary", () => b.fetchProjectFinancialSummary("p1"), "get", `${B}/projects/p1/financial-summary`],
    ["fetchBuildDashboard", () => b.fetchBuildDashboard(), "get", `${B}/dashboard`],
    ["createProject", () => b.createProject({ name: "Casa" } as never), "post", `${B}/projects`, { name: "Casa" }],
    ["updateProject", () => b.updateProject("p1", { name: "Casa" } as never), "put", `${B}/projects/p1`, { name: "Casa" }],
    ["startProject", () => b.startProject("p1"), "post", `${B}/projects/p1/start`, undefined],
    ["pauseProject", () => b.pauseProject("p1"), "post", `${B}/projects/p1/pause`, undefined],
    ["completeProject", () => b.completeProject("p1"), "post", `${B}/projects/p1/complete`, undefined],
    ["cancelProject", () => b.cancelProject("p1"), "post", `${B}/projects/p1/cancel`, undefined],
    ["fetchStages", () => b.fetchStages("p1"), "get", `${B}/projects/p1/stages`],
    ["createStage", () => b.createStage("p1", { name: "E" }), "post", `${B}/projects/p1/stages`, { name: "E" }],
    ["updateStageProgress", () => b.updateStageProgress("s1", { progressPercent: 50 } as never), "put", `${B}/stages/s1/progress`,
      { progressPercent: 50 }],
    ["updateStage", () => b.updateStage("s1", { name: "E" } as never), "put", `${B}/stages/s1`, { name: "E" }],
    ["reorderStages", () => b.reorderStages("p1", { entries: [] }), "put", `${B}/projects/p1/stages/reorder`, { entries: [] }],
    ["deleteStage", () => b.deleteStage("s1"), "delete", `${B}/stages/s1`],
    ["fetchBudgets without params", () => b.fetchBudgets(), "get", `${B}/budgets`],
    ["fetchBudgets with params", () => b.fetchBudgets({ projectId: "p1", status: "Sent" as never, page: 1, pageSize: 50 }), "get",
      `${B}/budgets?projectId=p1&status=Sent&page=1&pageSize=50`],
    ["fetchBudget", () => b.fetchBudget("b1"), "get", `${B}/budgets/b1`],
    ["createBudget", () => b.createBudget({ name: "O" } as never), "post", `${B}/budgets`, { name: "O" }],
    ["sendBudget", () => b.sendBudget("b1"), "post", `${B}/budgets/b1/send`, undefined],
    ["approveBudget", () => b.approveBudget("b1"), "post", `${B}/budgets/b1/approve`, undefined],
    ["rejectBudget with reason", () => b.rejectBudget("b1", "caro"), "post", `${B}/budgets/b1/reject`, { reason: "caro" }],
    ["rejectBudget without reason", () => b.rejectBudget("b1"), "post", `${B}/budgets/b1/reject`, undefined],
    ["convertBudget", () => b.convertBudget("b1", { name: "Obra" } as never), "post", `${B}/budgets/b1/convert`, { name: "Obra" }],
    ["setBudgetMargin", () => b.setBudgetMargin("b1", { marginPercent: 12 } as never), "put", `${B}/budgets/b1/margin`, { marginPercent: 12 }],
    ["addBudgetItem", () => b.addBudgetItem("b1", { name: "I" } as never), "post", `${B}/budgets/b1/items`, { name: "I" }],
    ["updateBudgetItem", () => b.updateBudgetItem("it1", { name: "I" } as never), "put", `${B}/budget-items/it1`, { name: "I" }],
    ["removeBudgetItem", () => b.removeBudgetItem("it1"), "delete", `${B}/budget-items/it1`],
    ["fetchDailyLogs without params", () => b.fetchDailyLogs("p1"), "get", `${B}/projects/p1/daily-logs`],
    ["fetchDailyLogs with params", () => b.fetchDailyLogs("p1", { from: "2026-10-01", to: "2026-10-31", page: 2, pageSize: 10 }), "get",
      `${B}/projects/p1/daily-logs?from=2026-10-01&to=2026-10-31&page=2&pageSize=10`],
    ["fetchDailyLog", () => b.fetchDailyLog("d1"), "get", `${B}/daily-logs/d1`],
    ["createDailyLog", () => b.createDailyLog("p1", { date: "2026-10-01" } as never), "post", `${B}/projects/p1/daily-logs`,
      { date: "2026-10-01" }],
    ["updateDailyLog", () => b.updateDailyLog("d1", { notes: "n" } as never), "put", `${B}/daily-logs/d1`, { notes: "n" }],
    ["addDailyLogPhoto", () => b.addDailyLogPhoto("d1", { key: "k" } as never), "post", `${B}/daily-logs/d1/photos`, { key: "k" }],
    ["removeDailyLogPhoto", () => b.removeDailyLogPhoto("ph1"), "delete", `${B}/daily-log-photos/ph1`],
  ]);
});

describe("build-ai.api", () => {
  runCases([
    ["generateDailyLogCopilot", () => ai.generateDailyLogCopilot("p1", { text: "hoje" } as never), "post",
      `${B}/projects/p1/ai/daily-log-copilot`, { text: "hoje" }],
    ["generateProgressInterpretation", () => ai.generateProgressInterpretation("p1", { date: "2026-10-01" } as never), "post",
      `${B}/projects/p1/ai/progress-interpreter`, { date: "2026-10-01" }],
    ["generateModelMapping", () => ai.generateModelMapping("p1", { modelVersionId: "v1" }), "post", `${B}/projects/p1/ai/model-mapper`,
      { modelVersionId: "v1" }],
    ["generateClientUpdate", () => ai.generateClientUpdate("p1", { from: "2026-10-01" }), "post", `${B}/projects/p1/ai/client-update`,
      { from: "2026-10-01" }],
    ["generateWeeklyDigest", () => ai.generateWeeklyDigest("p1"), "post", `${B}/projects/p1/ai/weekly-digest`, {}],
    ["explainRisk", () => ai.explainRisk("p1", "Delay" as never), "post", `${B}/projects/p1/risk/explain`, { kind: "Delay" }],
    ["analyzeDocument", () => ai.analyzeDocument("doc1"), "post", `${B}/documents/doc1/analyze`, {}],
    ["fetchSuggestions all", () => ai.fetchSuggestions("p1"), "get", `${B}/projects/p1/ai/suggestions`],
    ["fetchSuggestions filtered", () => ai.fetchSuggestions("p1", { capability: "ModelMapper", status: "Pending" }), "get",
      `${B}/projects/p1/ai/suggestions?capability=ModelMapper&status=Pending`],
    ["fetchSuggestion", () => ai.fetchSuggestion("sg1"), "get", `${B}/ai/suggestions/sg1`],
    ["acceptSuggestion", () => ai.acceptSuggestion("sg1", { itemIds: ["a"] }), "post", `${B}/ai/suggestions/sg1/accept`, { itemIds: ["a"] }],
    ["rejectSuggestion", () => ai.rejectSuggestion("sg1", "não"), "post", `${B}/ai/suggestions/sg1/reject`, { note: "não" }],
    ["fetchDelayRisk", () => ai.fetchDelayRisk("p1"), "get", `${B}/projects/p1/risk/delay`],
    ["fetchCostRisk", () => ai.fetchCostRisk("p1"), "get", `${B}/projects/p1/risk/cost`],
    ["fetchDocuments all", () => ai.fetchDocuments("p1"), "get", `${B}/projects/p1/documents`],
    ["fetchDocuments by category (encoded)", () => ai.fetchDocuments("p1", "ART/RRT"), "get", `${B}/projects/p1/documents?category=ART%2FRRT`],
    ["updateDocument", () => ai.updateDocument("doc1", { title: "T" } as never), "put", `${B}/documents/doc1`, { title: "T" }],
    ["setDocumentVisibility", () => ai.setDocumentVisibility("doc1", false), "put", `${B}/documents/doc1/client-visibility`,
      { clientVisible: false }],
    ["deleteDocument", () => ai.deleteDocument("doc1"), "delete", `${B}/documents/doc1`],
  ]);

  it("uploadDocument sends only the given fields as multipart", async () => {
    vi.mocked(apiClient.upload).mockResolvedValue(ok);
    const file = new File(["%PDF"], "art.pdf");
    await ai.uploadDocument("p1", { file, category: "Art", title: "ART", validUntil: "2027-01-01", clientVisible: false } as never);
    const [method, url, form] = vi.mocked(apiClient.upload).mock.calls.at(-1)!;
    expect([method, url]).toEqual(["POST", `${B}/projects/p1/documents`]);
    expect(Object.fromEntries([...form.entries()].filter(([k]) => k !== "file"))).toEqual({
      category: "Art", title: "ART", validUntil: "2027-01-01", clientVisible: "false",
    });
    await ai.uploadDocument("p1", { file } as never);
    const [, , bare] = vi.mocked(apiClient.upload).mock.calls.at(-1)!;
    expect([...bare.keys()]).toEqual(["file"]);
  });
});

describe("build-portal.api", () => {
  runCases([
    ["fetchPortalCompany", () => portal.fetchPortalCompany(), "get", `${B}/portal/company`],
    ["updatePortalCompany", () => portal.updatePortalCompany({ displayName: "X" } as never), "put", `${B}/portal/company`, { displayName: "X" }],
    ["fetchProjectPortal", () => portal.fetchProjectPortal("p1"), "get", `${B}/projects/p1/portal`],
    ["updateProjectPortal", () => portal.updateProjectPortal("p1", { showStages: true } as never), "put", `${B}/projects/p1/portal`,
      { showStages: true }],
    ["setProjectPortalPassword", () => portal.setProjectPortalPassword("p1", "segredo"), "put", `${B}/projects/p1/portal/password`,
      { password: "segredo" }],
    ["removeProjectPortalPassword", () => portal.removeProjectPortalPassword("p1"), "delete", `${B}/projects/p1/portal/password`],
    ["publishProjectPortal", () => portal.publishProjectPortal("p1"), "post", `${B}/projects/p1/portal/publish`, undefined],
    ["unpublishProjectPortal", () => portal.unpublishProjectPortal("p1"), "post", `${B}/projects/p1/portal/unpublish`, undefined],
    ["setDailyLogClientVisibility", () => portal.setDailyLogClientVisibility("d1", { clientVisible: true, clientSummary: null }), "put",
      `${B}/daily-logs/d1/client-visibility`, { clientVisible: true, clientSummary: null }],
    ["setPhotoClientVisibility", () => portal.setPhotoClientVisibility("ph1", true), "put", `${B}/daily-log-photos/ph1/client-visibility`,
      { clientVisible: true }],
    ["fetchClientUpdates", () => portal.fetchClientUpdates("p1"), "get", `${B}/projects/p1/client-updates`],
    ["createClientUpdate", () => portal.createClientUpdate("p1", { title: "T", body: "B" }), "post", `${B}/projects/p1/client-updates`,
      { title: "T", body: "B" }],
    ["editClientUpdate", () => portal.editClientUpdate("cu1", { title: "T", body: "B" }), "put", `${B}/client-updates/cu1`,
      { title: "T", body: "B" }],
    ["publishClientUpdate", () => portal.publishClientUpdate("cu1"), "post", `${B}/client-updates/cu1/publish`, undefined],
    ["archiveClientUpdate", () => portal.archiveClientUpdate("cu1"), "post", `${B}/client-updates/cu1/archive`, undefined],
  ]);
});
