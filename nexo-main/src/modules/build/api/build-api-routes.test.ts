import { describe, it, expect, vi, beforeEach } from "vitest";
import { ApiError, apiClient } from "@/services/api-client";
import * as q from "./build-quality.api";
import * as c from "./build-commercial.api";
import * as t from "./build-twin.api";

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

describe("build-quality.api", () => {
  const tpl = { name: "Alvenaria", items: [] };
  runCases([
    ["fetchChecklistTemplates", () => q.fetchChecklistTemplates(), "get", `${B}/checklist-templates`],
    ["createChecklistTemplate", () => q.createChecklistTemplate(tpl), "post", `${B}/checklist-templates`, tpl],
    ["updateChecklistTemplate", () => q.updateChecklistTemplate("t1", tpl), "put", `${B}/checklist-templates/t1`, tpl],
    ["deleteChecklistTemplate", () => q.deleteChecklistTemplate("t1"), "delete", `${B}/checklist-templates/t1`],
    ["fetchInspections without filters", () => q.fetchInspections("p1"), "get", `${B}/projects/p1/inspections`],
    ["fetchInspections with filters", () => q.fetchInspections("p1", { kind: "Quality", status: "Failed" }), "get",
      `${B}/projects/p1/inspections?kind=Quality&status=Failed`],
    ["createInspection", () => q.createInspection("p1", { title: "V" }), "post", `${B}/projects/p1/inspections`, { title: "V" }],
    ["fetchInspection", () => q.fetchInspection("i1"), "get", `${B}/inspections/i1`],
    ["updateInspection", () => q.updateInspection("i1", { title: "V2" }), "put", `${B}/inspections/i1`, { title: "V2" }],
    ["startInspection", () => q.startInspection("i1"), "post", `${B}/inspections/i1/start`, undefined],
    ["recordInspectionItem", () => q.recordInspectionItem("i1", "it1", { result: "Ok" }), "put", `${B}/inspections/i1/items/it1`, { result: "Ok" }],
    ["completeInspection with notes", () => q.completeInspection("i1", "fim"), "post", `${B}/inspections/i1/complete`, { notes: "fim" }],
    ["completeInspection without notes", () => q.completeInspection("i1"), "post", `${B}/inspections/i1/complete`, {}],
    ["cancelInspection", () => q.cancelInspection("i1"), "post", `${B}/inspections/i1/cancel`, undefined],
    ["fetchNonConformities filtered (overdue)", () => q.fetchNonConformities("p1", { status: "Open", severity: "Critical", overdue: true }),
      "get", `${B}/projects/p1/non-conformities?status=Open&severity=Critical&overdue=true`],
    ["fetchNonConformities (overdue false is not sent)", () => q.fetchNonConformities("p1", { overdue: false }), "get",
      `${B}/projects/p1/non-conformities`],
    ["createNonConformity", () => q.createNonConformity("p1", { title: "F" }), "post", `${B}/projects/p1/non-conformities`, { title: "F" }],
    ["fetchNonConformity", () => q.fetchNonConformity("n1"), "get", `${B}/non-conformities/n1`],
    ["updateNonConformity", () => q.updateNonConformity("n1", { title: "F" }), "put", `${B}/non-conformities/n1`, { title: "F" }],
    ["startNonConformity", () => q.startNonConformity("n1"), "post", `${B}/non-conformities/n1/start`, undefined],
    ["resolveNonConformity", () => q.resolveNonConformity("n1", { rootCause: "r", correctiveAction: "a" }), "post",
      `${B}/non-conformities/n1/resolve`, { rootCause: "r", correctiveAction: "a" }],
    ["verifyNonConformity", () => q.verifyNonConformity("n1"), "post", `${B}/non-conformities/n1/verify`, undefined],
    ["reopenNonConformity", () => q.reopenNonConformity("n1"), "post", `${B}/non-conformities/n1/reopen`, undefined],
    ["cancelNonConformity", () => q.cancelNonConformity("n1", "dup"), "post", `${B}/non-conformities/n1/cancel`, { reason: "dup" }],
    ["fetchQualitySummary", () => q.fetchQualitySummary("p1"), "get", `${B}/projects/p1/quality/summary`],
    ["saveHandover", () => q.saveHandover("p1", { scheduledFor: null }), "put", `${B}/projects/p1/handover`, { scheduledFor: null }],
    ["createWalkthrough", () => q.createWalkthrough("p1", { title: "W" }), "post", `${B}/projects/p1/handover/walkthrough`, { title: "W" }],
    ["deliverHandover with keys", () => q.deliverHandover("p1", true), "post", `${B}/projects/p1/handover/deliver`, { keysDelivered: true }],
    ["deliverHandover without keys info", () => q.deliverHandover("p1"), "post", `${B}/projects/p1/handover/deliver`, {}],
    ["acceptHandover", () => q.acceptHandover("p1", { signerName: "J" }), "post", `${B}/projects/p1/handover/accept`, { signerName: "J" }],
    ["fetchWarranty", () => q.fetchWarranty("p1"), "get", `${B}/projects/p1/warranty`],
    ["createWarranty", () => q.createWarranty("p1", { system: "S", months: 12 } as never), "post", `${B}/projects/p1/warranty`,
      { system: "S", months: 12 }],
    ["createDefaultWarranty", () => q.createDefaultWarranty("p1"), "post", `${B}/projects/p1/warranty/defaults`, undefined],
    ["updateWarranty", () => q.updateWarranty("w1", { system: "S" } as never), "put", `${B}/warranty-terms/w1`, { system: "S" }],
    ["deleteWarranty", () => q.deleteWarranty("w1"), "delete", `${B}/warranty-terms/w1`],
    ["fetchAssistance filtered", () => q.fetchAssistance("p1", { status: "Open", covered: false }), "get",
      `${B}/projects/p1/assistance?status=Open&covered=false`],
    ["openAssistance", () => q.openAssistance("p1", { title: "I" } as never), "post", `${B}/projects/p1/assistance`, { title: "I" }],
    ["fetchAssistanceRequest", () => q.fetchAssistanceRequest("a1"), "get", `${B}/assistance/a1`],
    ["triageAssistance", () => q.triageAssistance("a1", { warrantyCovered: true }), "post", `${B}/assistance/a1/triage`, { warrantyCovered: true }],
    ["scheduleAssistance", () => q.scheduleAssistance("a1", "2027-01-02"), "post", `${B}/assistance/a1/schedule`, { scheduledFor: "2027-01-02" }],
    ["startAssistance", () => q.startAssistance("a1"), "post", `${B}/assistance/a1/start`, undefined],
    ["resolveAssistance", () => q.resolveAssistance("a1", "ok"), "post", `${B}/assistance/a1/resolve`, { resolutionNotes: "ok" }],
    ["closeAssistance", () => q.closeAssistance("a1"), "post", `${B}/assistance/a1/close`, undefined],
    ["rejectAssistance", () => q.rejectAssistance("a1", "mau uso"), "post", `${B}/assistance/a1/reject`, { reason: "mau uso" }],
    ["addAssistancePhoto", () => q.addAssistancePhoto("a1", "k"), "post", `${B}/assistance/a1/photos`, { key: "k" }],
  ]);

  it("fetchHandover returns the handover, null on 404, and rethrows other errors", async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({ id: "h1" });
    await expect(q.fetchHandover("p1")).resolves.toEqual({ id: "h1" });
    expect(apiClient.get).toHaveBeenCalledWith(`${B}/projects/p1/handover`);

    vi.mocked(apiClient.get).mockRejectedValueOnce(new ApiError(404, "nf"));
    await expect(q.fetchHandover("p1")).resolves.toBeNull();

    const boom = new ApiError(500, "boom");
    vi.mocked(apiClient.get).mockRejectedValueOnce(boom);
    await expect(q.fetchHandover("p1")).rejects.toBe(boom);
  });
});

describe("build-commercial.api", () => {
  runCases([
    ["setBudgetTerms", () => c.setBudgetTerms("b1", { validUntil: "2026-12-31" } as never), "put", `${B}/budgets/b1/terms`, { validUntil: "2026-12-31" }],
    ["duplicateBudget with name", () => c.duplicateBudget("b1", "Cópia"), "post", `${B}/budgets/b1/duplicate`, { name: "Cópia" }],
    ["duplicateBudget without name", () => c.duplicateBudget("b1"), "post", `${B}/budgets/b1/duplicate`, undefined],
    ["createBudgetVersion", () => c.createBudgetVersion("b1"), "post", `${B}/budgets/b1/versions`, undefined],
    ["fetchBudgetVersions", () => c.fetchBudgetVersions("b1"), "get", `${B}/budgets/b1/versions`],
    ["saveBudgetAsTemplate", () => c.saveBudgetAsTemplate("b1", { name: "M", perSquareMeter: true }), "post",
      `${B}/budgets/b1/save-as-template`, { name: "M", perSquareMeter: true }],
    ["fetchBudgetTemplates", () => c.fetchBudgetTemplates(), "get", `${B}/budget-templates`],
    ["createBudgetTemplate", () => c.createBudgetTemplate({ name: "M", defaultMarginPercent: 10, items: [] }), "post",
      `${B}/budget-templates`, { name: "M", defaultMarginPercent: 10, items: [] }],
    ["updateBudgetTemplate", () => c.updateBudgetTemplate("t1", { name: "M", defaultMarginPercent: 10, items: [] }), "put",
      `${B}/budget-templates/t1`, { name: "M", defaultMarginPercent: 10, items: [] }],
    ["deleteBudgetTemplate", () => c.deleteBudgetTemplate("t1"), "delete", `${B}/budget-templates/t1`],
    ["createBudgetFromTemplate", () => c.createBudgetFromTemplate("t1", { name: "N", areaM2: 90 }), "post",
      `${B}/budget-templates/t1/budgets`, { name: "N", areaM2: 90 }],
    ["fetchChangeOrders all", () => c.fetchChangeOrders("p1"), "get", `${B}/projects/p1/change-orders`],
    ["fetchChangeOrders by status", () => c.fetchChangeOrders("p1", "Sent" as never), "get", `${B}/projects/p1/change-orders?status=Sent`],
    ["createChangeOrder", () => c.createChangeOrder("p1", { title: "A" } as never), "post", `${B}/projects/p1/change-orders`, { title: "A" }],
    ["updateChangeOrder", () => c.updateChangeOrder("co1", { title: "A" } as never), "put", `${B}/change-orders/co1`, { title: "A" }],
    ["sendChangeOrder", () => c.sendChangeOrder("co1"), "post", `${B}/change-orders/co1/send`, undefined],
    ["approveChangeOrder with comment", () => c.approveChangeOrder("co1", "ok"), "post", `${B}/change-orders/co1/approve`, { comment: "ok" }],
    ["approveChangeOrder without comment", () => c.approveChangeOrder("co1"), "post", `${B}/change-orders/co1/approve`, undefined],
    ["rejectChangeOrder with comment", () => c.rejectChangeOrder("co1", "caro"), "post", `${B}/change-orders/co1/reject`, { comment: "caro" }],
    ["rejectChangeOrder without comment", () => c.rejectChangeOrder("co1"), "post", `${B}/change-orders/co1/reject`, undefined],
    ["cancelChangeOrder", () => c.cancelChangeOrder("co1"), "post", `${B}/change-orders/co1/cancel`, undefined],
    ["fetchSelections", () => c.fetchSelections("p1"), "get", `${B}/projects/p1/selections`],
    ["createSelection", () => c.createSelection("p1", { title: "Piso" } as never), "post", `${B}/projects/p1/selections`, { title: "Piso" }],
    ["updateSelection", () => c.updateSelection("s1", { title: "Piso" } as never), "put", `${B}/selections/s1`, { title: "Piso" }],
    ["addSelectionOption", () => c.addSelectionOption("s1", { name: "A" } as never), "post", `${B}/selections/s1/options`, { name: "A" }],
    ["updateSelectionOption", () => c.updateSelectionOption("o1", { name: "B" } as never), "put", `${B}/selection-options/o1`, { name: "B" }],
    ["removeSelectionOption", () => c.removeSelectionOption("o1"), "delete", `${B}/selection-options/o1`],
    ["confirmSelection with option", () => c.confirmSelection("s1", "o1"), "post", `${B}/selections/s1/confirm`, { optionId: "o1" }],
    ["confirmSelection without option", () => c.confirmSelection("s1"), "post", `${B}/selections/s1/confirm`, undefined],
    ["cancelSelection", () => c.cancelSelection("s1"), "post", `${B}/selections/s1/cancel`, undefined],
    ["fetchMaterials without filters", () => c.fetchMaterials("p1"), "get", `${B}/projects/p1/materials`],
    ["fetchMaterials with filters", () => c.fetchMaterials("p1", { status: "Ordered" as never, stageId: "s1", late: true }), "get",
      `${B}/projects/p1/materials?status=Ordered&stageId=s1&late=true`],
    ["createMaterial", () => c.createMaterial("p1", { name: "Cimento" } as never), "post", `${B}/projects/p1/materials`, { name: "Cimento" }],
    ["updateMaterial", () => c.updateMaterial("m1", { name: "Cimento" } as never), "put", `${B}/materials/m1`, { name: "Cimento" }],
    ["quoteMaterial", () => c.quoteMaterial("m1", { estimatedUnitCost: 30 }), "post", `${B}/materials/m1/quote`, { estimatedUnitCost: 30 }],
    ["orderMaterial", () => c.orderMaterial("m1", { unitCost: 29 }), "post", `${B}/materials/m1/order`, { unitCost: 29 }],
    ["deliverMaterial", () => c.deliverMaterial("m1", { quantity: 10, receivedBy: "Zé" }), "post", `${B}/materials/m1/deliveries`,
      { quantity: 10, receivedBy: "Zé" }],
    ["cancelMaterial", () => c.cancelMaterial("m1"), "post", `${B}/materials/m1/cancel`, undefined],
    ["linkMaterialMovement", () => c.linkMaterialMovement("m1", null), "put", `${B}/materials/m1/movement`, { movementId: null }],
    ["fetchMeasurements", () => c.fetchMeasurements("p1"), "get", `${B}/projects/p1/measurements`],
    ["createMeasurement", () => c.createMeasurement("p1", { title: "M1" } as never), "post", `${B}/projects/p1/measurements`, { title: "M1" }],
    ["updateMeasurement", () => c.updateMeasurement("me1", { title: "M1" } as never), "put", `${B}/measurements/me1`, { title: "M1" }],
    ["submitMeasurement", () => c.submitMeasurement("me1"), "post", `${B}/measurements/me1/submit`, undefined],
    ["approveMeasurement", () => c.approveMeasurement("me1"), "post", `${B}/measurements/me1/approve`, undefined],
    ["rejectMeasurement with reason", () => c.rejectMeasurement("me1", "erro"), "post", `${B}/measurements/me1/reject`, { reason: "erro" }],
    ["rejectMeasurement without reason", () => c.rejectMeasurement("me1"), "post", `${B}/measurements/me1/reject`, undefined],
  ]);
});

describe("build-twin.api", () => {
  runCases([
    ["fetchBuildSettings", () => t.fetchBuildSettings(), "get", `${B}/settings`],
    ["fetchTwinOverview", () => t.fetchTwinOverview("p1"), "get", `${B}/projects/p1/twin`],
    ["fetchModels", () => t.fetchModels("p1"), "get", `${B}/projects/p1/models`],
    ["fetchModelVersion", () => t.fetchModelVersion("v1"), "get", `${B}/models/versions/v1`],
    ["fetchModelViewer", () => t.fetchModelViewer("v1"), "get", `${B}/models/versions/v1/viewer`],
    ["reprocessModelVersion", () => t.reprocessModelVersion("v1"), "post", `${B}/models/versions/v1/reprocess`, undefined],
    ["fetchElements without filters", () => t.fetchElements("v1"), "get", `${B}/models/versions/v1/elements`],
    ["fetchElements with filters (empty values dropped)", () => t.fetchElements("v1", {
      category: "IfcWall", zoneId: "z1", state: "", date: "2026-10-01", skip: 0, take: 500, externalId: "G 1",
    }), "get", `${B}/models/versions/v1/elements?category=IfcWall&zoneId=z1&date=2026-10-01&skip=0&take=500&externalId=G+1`],
    ["fetchElementEvents (id encoded)", () => t.fetchElementEvents("v1", "a/b"), "get", `${B}/models/versions/v1/elements/a%2Fb/events`],
    ["fetchVersionZones", () => t.fetchVersionZones("v1"), "get", `${B}/models/versions/v1/zones`],
    ["fetchTwinState with date", () => t.fetchTwinState("v1", "2026-10-01"), "get", `${B}/models/versions/v1/state?date=2026-10-01`],
    ["fetchTwinState without date", () => t.fetchTwinState("v1"), "get", `${B}/models/versions/v1/state`],
    ["fetchProjectZones", () => t.fetchProjectZones("p1"), "get", `${B}/projects/p1/zones`],
    ["createZone", () => t.createZone("p1", { kind: "Floor", name: "T" }), "post", `${B}/projects/p1/zones`, { kind: "Floor", name: "T" }],
    ["updateZone", () => t.updateZone("z1", { kind: "Floor", name: "T" }), "put", `${B}/zones/z1`, { kind: "Floor", name: "T" }],
    ["deleteZone", () => t.deleteZone("z1"), "delete", `${B}/zones/z1`],
    ["fetchBindings", () => t.fetchBindings("p1"), "get", `${B}/projects/p1/twin/bindings`],
    ["createBinding", () => t.createBinding("p1", { stageId: "s1", scope: "Zone", zoneId: "z1" }), "post", `${B}/projects/p1/twin/bindings`,
      { stageId: "s1", scope: "Zone", zoneId: "z1" }],
    ["bulkBind", () => t.bulkBind("p1", { stageId: "s1", modelVersionId: "v1", category: "IfcWall" }), "post",
      `${B}/projects/p1/twin/bindings/bulk`, { stageId: "s1", modelVersionId: "v1", category: "IfcWall" }],
    ["deleteBinding", () => t.deleteBinding("b1"), "delete", `${B}/twin/bindings/b1`],
    ["recordElementEvents", () => t.recordElementEvents("p1", { modelVersionId: "v1", externalIds: ["a"], state: "Completed" }), "post",
      `${B}/projects/p1/twin/element-events`, { modelVersionId: "v1", externalIds: ["a"], state: "Completed" }],
    ["fetchRealityCaptures filtered", () => t.fetchRealityCaptures("p1", { type: "Photo", stageId: "s1", from: "2026-10-01" }), "get",
      `${B}/projects/p1/reality-captures?type=Photo&stageId=s1&from=2026-10-01`],
    ["fetchRealityCaptures all", () => t.fetchRealityCaptures("p1"), "get", `${B}/projects/p1/reality-captures`],
    ["updateRealityCapture", () => t.updateRealityCapture("c1", { title: "L" }), "put", `${B}/reality-captures/c1`, { title: "L" }],
    ["setRealityCaptureVisibility", () => t.setRealityCaptureVisibility("c1", true), "put", `${B}/reality-captures/c1/client-visibility`,
      { clientVisible: true }],
    ["deleteRealityCapture", () => t.deleteRealityCapture("c1"), "delete", `${B}/reality-captures/c1`],
  ]);

  const formOf = () => vi.mocked(apiClient.upload).mock.calls.at(-1)!;

  it("uploadModel sends the file, the trimmed name and the discipline as multipart with progress", async () => {
    const file = new File(["ifc"], "casa.ifc");
    const onProgress = vi.fn();
    vi.mocked(apiClient.upload).mockResolvedValue(ok);
    await t.uploadModel("p1", { file, name: "  Casa  ", discipline: "Estrutura" }, onProgress);
    const [method, url, form, progress] = formOf();
    expect([method, url, progress]).toEqual(["POST", `${B}/projects/p1/models`, onProgress]);
    expect(form.get("file")).toBeInstanceOf(File);
    expect(form.get("name")).toBe("Casa");
    expect(form.get("discipline")).toBe("Estrutura");

    await t.uploadModel("p1", { file, name: "   " });
    expect(formOf()[2].has("name")).toBe(false);
    expect(formOf()[2].has("discipline")).toBe(false);
  });

  it("uploadModelVersion and uploadViewer use their routes and verbs", async () => {
    vi.mocked(apiClient.upload).mockResolvedValue(ok);
    await t.uploadModelVersion("m1", new File(["x"], "v2.ifc"));
    expect(formOf().slice(0, 2)).toEqual(["POST", `${B}/models/m1/versions`]);
    expect(formOf()[2].get("file")).toBeInstanceOf(File);

    const form = new FormData();
    await t.uploadViewer("v1", form);
    expect(formOf().slice(0, 3)).toEqual(["PUT", `${B}/models/versions/v1/viewer`, form]);
  });

  it("uploadRealityCapture sends every given field (trimmed) and omits the empty ones", async () => {
    vi.mocked(apiClient.upload).mockResolvedValue(ok);
    const file = new File(["img"], "laje.jpg");
    await t.uploadRealityCapture("p1", {
      file, type: "Photo", title: "Laje", capturedAt: "2026-10-05T12:00:00", stageId: "s1", zoneId: "z1",
      elementExternalId: " G1 ", notes: " ok ", clientVisible: true,
    });
    const [method, url, form] = formOf();
    expect([method, url]).toEqual(["POST", `${B}/projects/p1/reality-captures`]);
    expect(Object.fromEntries([...form.entries()].filter(([k]) => k !== "file"))).toEqual({
      type: "Photo", title: "Laje", capturedAt: "2026-10-05T12:00:00", stageId: "s1", zoneId: "z1",
      elementExternalId: "G1", notes: "ok", clientVisible: "true",
    });

    await t.uploadRealityCapture("p1", { file, type: "Video", title: "V", elementExternalId: "  ", notes: null });
    const [, , minimal] = formOf();
    expect(Object.fromEntries([...minimal.entries()].filter(([k]) => k !== "file"))).toEqual({
      type: "Video", title: "V", clientVisible: "false",
    });
  });
});
