import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient } from "@/services/api-client";
import * as api from "./taxAdminApi";

vi.mock("@/services/api-client", () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), postForm: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);
const postForm = vi.mocked(apiClient.postForm);

/** The platform client must address the routes PlatformTaxController exposes, with the bodies it validates. */
describe("platform tax API client", () => {
  beforeEach(() => vi.clearAllMocks());

  it("reads the module state, checklist, readiness, refusals and audit", () => {
    api.getModuleState();
    api.getActivationChecklist("ProfessionalFinal");
    api.getReadiness();
    api.getRefusals();
    api.getRefusals(10);
    api.getTaxAudit("");
    api.getTaxAudit("tax_module_activated", 50, 25);

    expect(get.mock.calls.map((c) => c[0])).toEqual([
      "/platform/tax/module",
      "/platform/tax/module/checklist?mode=ProfessionalFinal",
      "/platform/tax/readiness",
      "/platform/tax/refusals?take=50",
      "/platform/tax/refusals?take=10",
      "/platform/tax/audit?skip=0&take=50",
      "/platform/tax/audit?action=tax_module_activated&skip=50&take=25",
    ]);
  });

  it("sends activation, suspension and deactivation exactly as typed", () => {
    const body = { mode: "ControlledPilot" as const, decisionReference: "REF-TESTE", reason: "Motivo", confirmation: "FRASE", acknowledgeShadowMode: true };
    api.activateModule(body);
    api.suspendModule("Motivo de suspensão", api.SUSPEND_PHRASE);
    api.deactivateModule("Motivo de desativação", api.DEACTIVATE_PHRASE);

    expect(post.mock.calls).toEqual([
      ["/platform/tax/module/activate", body],
      ["/platform/tax/module/suspend", { reason: "Motivo de suspensão", confirmation: "SUSPENDER" }],
      ["/platform/tax/module/deactivate", { reason: "Motivo de desativação", confirmation: "DESATIVAR" }],
    ]);
  });

  it("reads and changes the pilot scope and its participants", () => {
    api.getPilotScope();
    api.configurePilotScope({ enabled: true, validFrom: "2026-01-01", validTo: null, decisionReference: "REF-TESTE" });
    api.addPilotParticipant("t1", null);
    api.removePilotParticipant("p1", "Saiu do piloto");

    expect(get).toHaveBeenCalledWith("/platform/tax/pilot");
    expect(put).toHaveBeenCalledWith("/platform/tax/pilot", { enabled: true, validFrom: "2026-01-01", validTo: null, decisionReference: "REF-TESTE" });
    expect(post.mock.calls).toEqual([
      ["/platform/tax/pilot/participants", { tenantId: "t1", storeId: null }],
      ["/platform/tax/pilot/participants/p1/remove", { reason: "Saiu do piloto" }],
    ]);
  });

  it("addresses rule versions: status, professional review, history, pilot approval and its withdrawal", () => {
    const review: api.RecordProfessionalReviewRequest = {
      status: "Approved", reviewer: "Profissional Teste", notes: null, professionalReference: null, scope: "Escopo", validFrom: "2026-01-01",
      validTo: null, assumptions: null, caveats: null, coveredRegimes: ["LucroReal"], reviewedOn: null, supportingDocumentId: null,
    };
    const approval = { scope: "Recorte", validFrom: "2026-01-01", validTo: null, caveats: "Ressalva", decisionReference: "REF-TESTE" };
    api.listRules();
    api.listProfessionalReviews("v1");
    api.changeRuleVersionStatus("v1", "Revoked");
    api.recordProfessionalReview("v1", review);
    api.approveForPilot("v1", approval);
    api.withdrawPilotApproval("v1", "Motivo");

    expect(get.mock.calls.map((c) => c[0])).toEqual(["/platform/tax/rules", "/platform/tax/rule-versions/v1/professional-reviews"]);
    expect(post.mock.calls).toEqual([
      ["/platform/tax/rule-versions/v1/status", { status: "Revoked" }],
      ["/platform/tax/rule-versions/v1/professional-review", review],
      ["/platform/tax/rule-versions/v1/pilot-approval", approval],
      ["/platform/tax/rule-versions/v1/pilot-approval/withdraw", { reason: "Motivo" }],
    ]);
  });

  it("lists, creates and verifies legal sources, encoding the code in the path", () => {
    const body: api.CreateLegalSourceRequest = {
      code: "FONTE-TESTE", kind: "Other", title: "Título", reference: "art. 1", url: "https://exemplo.test/x", excerpt: "Trecho", publishedOn: null, notes: null,
    };
    api.listLegalSources();
    api.createLegalSource(body);
    api.verifyLegalSource("A B/1", "Pessoa Teste");

    expect(get).toHaveBeenCalledWith("/platform/tax/legal-sources");
    expect(post.mock.calls).toEqual([
      ["/platform/tax/legal-sources", body],
      ["/platform/tax/legal-sources/A%20B%2F1/verify", { verifiedBy: "Pessoa Teste" }],
    ]);
  });

  it("filters classifications only by the values given, with no trailing '?' when there are none", () => {
    api.listPlatformClassifications({});
    api.listPlatformClassifications({ kind: "CClassTrib", status: "", search: "", skip: 0, take: 50 });
    api.listPlatformClassifications({ kind: "CClassTrib", status: "Active", search: "000", skip: 50, take: 50 });

    expect(get.mock.calls.map((c) => c[0])).toEqual([
      "/platform/tax/classifications",
      "/platform/tax/classifications?kind=CClassTrib&skip=0&take=50",
      "/platform/tax/classifications?kind=CClassTrib&status=Active&search=000&skip=50&take=50",
    ]);
  });

  it("imports classifications, changes their status and records their review", () => {
    const body = { kind: "CClassTrib" as const, source: "Tabela de teste", sourceVersion: null, sourceDocumentId: null, items: [] };
    api.importClassifications(body);
    api.changeClassificationStatus("c1", "Inactive", "Motivo");
    api.recordClassificationReview("c1", "ChangesRequired", "Notas");

    expect(post.mock.calls).toEqual([
      ["/platform/tax/classifications/import", body],
      ["/platform/tax/classifications/c1/status", { status: "Inactive", reason: "Motivo" }],
      ["/platform/tax/classifications/c1/professional-review", { status: "ChangesRequired", notes: "Notas" }],
    ]);
  });

  it("reads and changes obligations and retention defaults", () => {
    const retention = {
      evidenceType: null, retentionMonths: 60, startsAt: "EndOfUploadYear" as const, enabled: true, allowTenantOverride: false, rationale: "Motivo", reference: "REF",
    };
    api.listPlatformObligations();
    api.defineObligation({ code: "OB-1", name: "Obrigação", description: null, reference: "REF" });
    api.setObligationActive("o1", false);
    api.listPlatformRetention();
    api.configurePlatformRetention(retention);

    expect(get.mock.calls.map((c) => c[0])).toEqual(["/platform/tax/obligations", "/platform/tax/retention-policies"]);
    expect(post.mock.calls).toEqual([
      ["/platform/tax/obligations", { code: "OB-1", name: "Obrigação", description: null, reference: "REF" }],
      ["/platform/tax/obligations/o1/active", { active: false }],
    ]);
    expect(put).toHaveBeenCalledWith("/platform/tax/retention-policies", retention);
  });

  it("lists documents by purpose, with no trailing '?' for all purposes", () => {
    api.listDocuments();
    api.listDocuments("professional-review");
    api.revokeDocument("d1", "Motivo");

    expect(get.mock.calls.map((c) => c[0])).toEqual(["/platform/tax/documents", "/platform/tax/documents?purpose=professional-review"]);
    expect(post).toHaveBeenCalledWith("/platform/tax/documents/d1/revoke", { reason: "Motivo" });
    expect(api.documentUrl("d1")).toBe("/platform/tax/documents/d1/content");
  });

  it("uploads a document as multipart with its purpose, and the description only when there is one", () => {
    const file = new File(["%PDF-1.4"], "revisao.pdf", { type: "application/pdf" });
    api.uploadDocument("professional-review", file, "Revisão de TESTE v1");
    api.uploadDocument("classification-source", file, null);

    expect(postForm.mock.calls.map((c) => c[0])).toEqual(["/platform/tax/documents", "/platform/tax/documents"]);
    const withDescription = postForm.mock.calls[0][1];
    expect(withDescription.get("file")).toBeInstanceOf(File);
    expect((withDescription.get("file") as File).name).toBe("revisao.pdf");
    expect(withDescription.get("purpose")).toBe("professional-review");
    expect(withDescription.get("description")).toBe("Revisão de TESTE v1");
    const withoutDescription = postForm.mock.calls[1][1];
    expect(withoutDescription.get("purpose")).toBe("classification-source");
    expect(withoutDescription.has("description")).toBe(false);
  });

  it("keys every platform query under the platform tax prefix", () => {
    expect(api.PLATFORM_TAX_KEYS.checklist("ControlledPilot")).toEqual(["platform", "tax", "checklist", "ControlledPilot"]);
    expect(api.PLATFORM_TAX_KEYS.reviews("v1")).toEqual(["platform", "tax", "reviews", "v1"]);
    expect(api.PLATFORM_TAX_KEYS.classifications({ search: "x" })).toEqual(["platform", "tax", "classifications", { search: "x" }]);
    expect(api.PLATFORM_TAX_KEYS.documents("")).toEqual(["platform", "tax", "documents", ""]);
    expect(api.PLATFORM_TAX_KEYS.audit("a", 50)).toEqual(["platform", "tax", "audit", "a", 50]);
  });
});

describe("parseClassificationLines", () => {
  it("parses valid lines, trimming fields, skipping blank lines and accepting CRLF and an optional end date", () => {
    const { items, errors } = api.parseClassificationLines(" 000001 ; Tributação integral ; 2026-01-01 \r\n\n000002;Redução;2026-01-01;2026-12-31\n");
    expect(errors).toEqual([]);
    expect(items).toEqual([
      { code: "000001", description: "Tributação integral", validFrom: "2026-01-01", validTo: null, notes: null },
      { code: "000002", description: "Redução", validFrom: "2026-01-01", validTo: "2026-12-31", notes: null },
    ]);
  });

  it("reports malformed lines by number and never guesses them", () => {
    const { items, errors } = api.parseClassificationLines([
      "000001;Válida;2026-01-01",
      "000002;Sem data",
      ";Sem código;2026-01-01",
      "000004;Data em outro formato;01/01/2026",
      "000005;Fim inválido;2026-01-01;31/12/2026",
      "000006",
    ].join("\n"));

    expect(items).toEqual([{ code: "000001", description: "Válida", validFrom: "2026-01-01", validTo: null, notes: null }]);
    expect(errors).toHaveLength(5);
    expect(errors.map((e) => e.split(":")[0])).toEqual(["Linha 2", "Linha 3", "Linha 4", "Linha 5", "Linha 6"]);
    expect(errors[0]).toBe("Linha 2: use código;descrição;AAAA-MM-DD;AAAA-MM-DD(opcional).");
  });

  it("numbers a bad line as it appears in the text, blank lines included", () => {
    const { errors } = api.parseClassificationLines("000001;Válida;2026-01-01\n\n\n000002;Sem data");
    expect(errors.map((e) => e.split(":")[0])).toEqual(["Linha 4"]);
  });

  it("returns nothing for empty input", () => {
    expect(api.parseClassificationLines("  \n \n")).toEqual({ items: [], errors: [] });
  });
});
