using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence.Migrations;
using Nexo.Infrastructure.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Catalog;
using static Nexo.UnitTests.TaxIntelligence.TaxHardeningTests;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

/// <summary>
/// Delta from the tax review: compliance by period and scope, PNCT, an expressive professional review,
/// the fail-closed supported-operation matrix, scenario approval apart from execution, typed evidence,
/// the credit lifecycle, maker-checker by risk and materiality, the waiver note v2, and regressions
/// against statements the review documents got wrong. No test asserts a new tax thesis.
/// </summary>
public class TaxReviewDeltaTests
{
    // ── compliance and PNCT ─────────────────────────────────────────────────────

    [Fact]
    public void A_verification_states_its_period_and_scope()
    {
        FluentActions.Invoking(() => Assessment(ComplianceAssessmentStatus.Verified, scope: " ", evidence: "relatório", checkedList: ["NF-e"]))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_compliance_scope");
        FluentActions.Invoking(() => TaxComplianceAssessment.Record(Guid.NewGuid(), ComplianceAssessmentStatus.Verified, new DateOnly(2026, 6, 30),
                new DateOnly(2026, 1, 1), "estabelecimento", ["NF-e"], "relatório", "Contabilidade", DateTime.UtcNow, null, null, false, Guid.NewGuid(), "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_compliance_period");

        var a = Assessment(ComplianceAssessmentStatus.Verified, evidence: "relatório", checkedList: ["NF-e"]);
        a.Covers(new DateOnly(2026, 3, 1)).Should().BeTrue();
        a.Covers(new DateOnly(2026, 7, 1)).Should().BeFalse("verified for one period is not verified for another");
        TaxComplianceAssessment.InForce([a], new DateOnly(2026, 7, 1)).Should().BeNull();
    }

    [Fact]
    public void Declared_does_not_satisfy_a_rule()
        => V1.Run(Input(compliant: Assessment(ComplianceAssessmentStatus.Declared).ForRules), Catalogue())
            .Notes.Select(n => n.Code).Should().NotContain(TaxNoteCodes.Waiver2026);

    [Theory]
    [InlineData(ComplianceAssessmentStatus.InconsistencyIdentified)]
    [InlineData(ComplianceAssessmentStatus.UnderRemediation)]
    [InlineData(ComplianceAssessmentStatus.RemediationVerified)]
    [InlineData(ComplianceAssessmentStatus.NotApplicable)]
    public void Remediation_states_are_not_verified(ComplianceAssessmentStatus status)
    {
        var a = Assessment(status, notes: "inconsistência nas NF-e", evidence: "relatório", checkedList: ["NF-e"]);
        a.ForRules.Should().BeNull($"{status} never counts as verified");
        TaxComplianceAdvisories.For(a)[0].Should().NotStartWith("Obrigações verificadas");
    }

    [Fact]
    public void PNCT_participation_does_not_imply_full_compliance()
    {
        var pnct = Assessment(ComplianceAssessmentStatus.UnderRemediation, notes: "regularização em curso", pnct: true);
        pnct.ForRules.Should().BeNull();
        TaxComplianceAdvisories.For(pnct).Should().Contain(TaxComplianceAdvisories.Pnct);
        TaxComplianceAdvisories.Pnct.Should().Contain("não significa regularidade integral").And.Contain("Ato Conjunto RFB/CGIBS nº 5/2026");
        Assessment(ComplianceAssessmentStatus.Declared, pnct: true).ForRules.Should().BeNull("no automatic benefit from PNCT participation");
    }

    [Fact]
    public void The_newest_assessment_of_a_period_wins()
    {
        var older = Assessment(ComplianceAssessmentStatus.Verified, evidence: "relatório", checkedList: ["NF-e"]);
        Thread.Sleep(5);
        var newer = Assessment(ComplianceAssessmentStatus.InconsistencyIdentified, notes: "NF-e sem campos");
        TaxComplianceAssessment.InForce([older, newer], new DateOnly(2026, 3, 1)).Should().BeSameAs(newer);
    }

    // ── supported-operation matrix (fail closed) ────────────────────────────────

    private static TaxLineRequest Good(string? ncm = "61091000", string? cClassTrib = "TEST01", string? special = null)
        => new(TaxItemKind.Good, "Produto", ncm, 1m, 1000m, 100m, 50m, 20m, 80m, special, null, cClassTrib);

    private static TaxOperationRequest Op(DateOnly? date = null, TaxOperationTiming? timing = null, TaxDirection direction = TaxDirection.Outbound,
        TaxRegime? counterparty = null, params TaxLineRequest[] lines)
        => new(date ?? Mid2026, direction, counterparty, null, "op", lines.Length == 0 ? [Good()] : lines,
            timing ?? new TaxOperationTiming(TaxSupplyPattern.SingleSupply));

    [Fact]
    public void A_single_supply_classified_in_2026_is_supported_and_says_what_it_assumes()
    {
        var d = TaxCoveragePolicyV1.Assess(Op(), TaxRegime.LucroPresumido);
        d.Status.Should().Be(TaxCoverageStatus.Supported);
        d.Assumptions.Should().Contain(a => a.Contains("Fornecimento único")).And.Contain(a => a.Contains("não interpretado"));
    }

    [Fact]
    public void Unsupported_operations_are_refused_never_calculated_as_zero()
    {
        TaxCoveragePolicyV1.Assess(Op(lines: Good(special: "reducao-60")), TaxRegime.LucroPresumido).Status.Should().Be(TaxCoverageStatus.Unsupported);
        TaxCoveragePolicyV1.Assess(Op(new DateOnly(2025, 12, 31)), TaxRegime.LucroPresumido).Status.Should().Be(TaxCoverageStatus.Unsupported);
        TaxCoveragePolicyV1.Assess(Op(new DateOnly(2027, 1, 2)), TaxRegime.LucroPresumido).Status.Should().Be(TaxCoverageStatus.PendingRegulation);
        TaxCoveragePolicyV1.Assess(Op(direction: TaxDirection.Inbound), TaxRegime.LucroPresumido).Status
            .Should().Be(TaxCoverageStatus.Unsupported, "an inbound operation without the supplier's regime is not presumed");
        TaxCoveragePolicyV1.NotSupportedMessage.Should().Be("Operação não suportada pela versão atual da engine.");
    }

    [Fact]
    public void Missing_required_classification_requires_classification()
    {
        var noCClass = TaxCoveragePolicyV1.Assess(Op(lines: Good(cClassTrib: null)), TaxRegime.LucroPresumido);
        noCClass.Status.Should().Be(TaxCoverageStatus.RequiresClassification);
        noCClass.Findings.Single().Message.Should().Contain("cClassTrib").And.Contain("não calcula presumindo o regime geral");
        TaxCoveragePolicyV1.Assess(Op(lines: Good(ncm: null)), TaxRegime.LucroPresumido).Findings.Single().Message.Should().Contain("NCM");

        var service = new TaxLineRequest(TaxItemKind.Service, "Serviço", null, 1m, 100m, 0m, 0m, 0m, 0m, null, null, "TEST01");
        TaxCoveragePolicyV1.Assess(Op(lines: service), TaxRegime.LucroPresumido).Findings.Single().Message
            .Should().Contain("NBS").And.NotContain("NCM", "a service is not asked for a goods code");
        TaxCoveragePolicyV1.Assess(Op(lines: service with { Nbs = "1.0101.10.00" }), TaxRegime.LucroPresumido).Status
            .Should().Be(TaxCoverageStatus.Supported);
    }

    [Theory]
    [InlineData(TaxSupplyPattern.ContinuedSupply, TaxCoverageStatus.Unsupported)]
    [InlineData(TaxSupplyPattern.AdvancePayment, TaxCoverageStatus.Unsupported)]
    [InlineData(TaxSupplyPattern.FutureDelivery, TaxCoverageStatus.Unsupported)]
    [InlineData(TaxSupplyPattern.PublicProcurement, TaxCoverageStatus.Unsupported)]
    [InlineData(TaxSupplyPattern.Other, TaxCoverageStatus.RequiresProfessionalReview)]
    public void Temporal_patterns_V1_does_not_model_fail_closed(TaxSupplyPattern pattern, TaxCoverageStatus expected)
        => TaxCoveragePolicyV1.Assess(Op(timing: new TaxOperationTiming(pattern)), TaxRegime.LucroPresumido).Status.Should().Be(expected);

    [Fact]
    public void Dates_V1_cannot_reconcile_are_never_silently_assumed()
    {
        TaxCoveragePolicyV1.Assess(new TaxOperationRequest(Mid2026, TaxDirection.Outbound, null, null, "op", [Good()]), TaxRegime.LucroPresumido)
            .Status.Should().Be(TaxCoverageStatus.RequiresProfessionalReview, "a missing timing is not presumed to be a single supply");
        TaxCoveragePolicyV1.Assess(Op(timing: new(TaxSupplyPattern.SingleSupply, DocumentDate: Mid2026.AddDays(3))), TaxRegime.LucroPresumido)
            .Status.Should().Be(TaxCoverageStatus.RequiresProfessionalReview);
        TaxCoveragePolicyV1.Assess(Op(timing: new(TaxSupplyPattern.SingleSupply, PaymentOrAdvanceDate: Mid2026.AddDays(-10))), TaxRegime.LucroPresumido)
            .Status.Should().Be(TaxCoverageStatus.Unsupported);
        TaxCoveragePolicyV1.Assess(Op(timing: new(TaxSupplyPattern.SingleSupply, ExecutionFrom: Mid2026, ExecutionTo: Mid2026.AddMonths(1))),
            TaxRegime.LucroPresumido).Status.Should().Be(TaxCoverageStatus.Unsupported);
    }

    [Theory]
    [InlineData(TaxRegime.Mei)]
    [InlineData(TaxRegime.SimplesNacionalExcessoSublimite)]
    public void MEI_and_Simples_above_the_sublimit_are_not_assumed_equal_to_the_Simples(TaxRegime regime)
    {
        TaxCoveragePolicyV1.Assess(Op(), regime).Status.Should().Be(TaxCoverageStatus.RequiresProfessionalReview);
        TaxCoveragePolicyV1.Assess(Op(direction: TaxDirection.Inbound, counterparty: regime), TaxRegime.LucroReal).Status
            .Should().Be(TaxCoverageStatus.RequiresProfessionalReview);
        TaxCoveragePolicyV1.Assess(Op(), TaxRegime.SimplesNacional).Status.Should().Be(TaxCoverageStatus.Supported);
    }

    // ── scenario approval vs execution, evidence ────────────────────────────────

    private static TaxOpportunity DiscountOpportunity(TaxRiskLevel risk = TaxRiskLevel.Low, decimal impact = 0.5m)
    {
        var c = new UnconditionalDiscountDetector().Detect(V1, Input(), Catalogue()).Single();
        var o = TaxOpportunity.Create(Guid.NewGuid(), c.Type, c.Title, c.Summary, c.DetectorKey, V1.Version, null, "{}", "{}", impact, risk,
            c.Confidence, "", c.Caveats);
        foreach (var r in c.Requirements) o.AddRequirement(r.Code, r.Text, r.LegalSourceCode, r.Phase, r.RequiredEvidence);
        return o;
    }

    private static TaxOpportunityRequirement Req(TaxOpportunity o, string code) => o.Requirements.Single(r => r.Code == code);

    private static void PrepareScenario(TaxOpportunity o, Guid analyst, bool withEvidence = true)
    {
        o.StartReview(analyst, "Ana", DateTime.UtcNow);
        if (withEvidence)
            o.AddEvidence(TaxEvidenceType.CommercialInstrument, Req(o, UnconditionalDiscountDetector.RequirementCommercialTerms).Id,
                "Pedido com o preço já reduzido", "PED-1", "h", null, analyst, "Ana");
        foreach (var r in o.Requirements.Where(r => r.Phase == RequirementPhase.ScenarioApproval))
            o.AnswerRequirement(r.Id, true, null, analyst, "Ana", DateTime.UtcNow);
    }

    [Fact]
    public void The_scenario_needs_a_commercial_instrument_not_just_a_label()
    {
        var o = DiscountOpportunity();
        PrepareScenario(o, Guid.NewGuid(), withEvidence: false);
        o.Invoking(x => x.Approve(false, Guid.NewGuid(), "Bia", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_evidence_missing");
    }

    [Fact]
    public void A_rejected_evidence_supports_nothing()
    {
        var o = DiscountOpportunity();
        PrepareScenario(o, Guid.NewGuid());
        var e = o.Evidence.Single();
        o.ReviewEvidence(e.Id, false, "pedido sem assinatura", Guid.NewGuid(), "Bia", DateTime.UtcNow);
        o.HasRequiredEvidence(Req(o, UnconditionalDiscountDetector.RequirementCommercialTerms)).Should().BeFalse();
        e.Status.Should().Be(TaxEvidenceStatus.Rejected);
        e.ReviewNotes.Should().Be("pedido sem assinatura");
    }

    [Fact]
    public void Scenario_approval_does_not_imply_execution_verification()
    {
        var o = DiscountOpportunity();
        var ana = Guid.NewGuid();
        var onDocument = Req(o, UnconditionalDiscountDetector.RequirementOnDocument);

        o.Invoking(x => x.AddEvidence(TaxEvidenceType.FiscalDocument, onDocument.Id, "NF-e", "chave", "h", null, ana, "Ana"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_evidence_phase", "a future fiscal document cannot be evidence before the operation");
        PrepareScenario(o, ana);
        o.Approve(false, Guid.NewGuid(), "Bia", DateTime.UtcNow);

        o.Status.Should().Be(TaxOpportunityStatus.Approved);
        onDocument.Met.Should().BeNull("approving the scenario says nothing about the fiscal document");
        o.Status.Should().NotBe(TaxOpportunityStatus.ExecutionVerified);
    }

    [Fact]
    public void Execution_requires_the_final_evidence()
    {
        var o = DiscountOpportunity();
        var ana = Guid.NewGuid();
        PrepareScenario(o, ana);
        o.Approve(false, Guid.NewGuid(), "Bia", DateTime.UtcNow);
        foreach (var r in o.Requirements.Where(r => r.Phase == RequirementPhase.ExecutionVerification))
            o.AnswerRequirement(r.Id, true, null, ana, "Ana", DateTime.UtcNow);

        o.Invoking(x => x.VerifyExecution(TaxExecutionOutcome.Verified, null, ana, "Ana", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_execution_evidence_missing");
        o.Invoking(x => x.AddEvidence(TaxEvidenceType.Other, null, "solto", null, "h", null, ana, "Ana"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_evidence_phase");

        o.AddEvidence(TaxEvidenceType.FiscalDocument, Req(o, UnconditionalDiscountDetector.RequirementOnDocument).Id, "NF-e autorizada", "chave", "h", null, ana, "Ana");
        o.VerifyExecution(TaxExecutionOutcome.Verified, null, ana, "Ana", DateTime.UtcNow);
        o.Status.Should().Be(TaxOpportunityStatus.ExecutionVerified);
    }

    [Fact]
    public void A_divergent_execution_is_recorded_with_what_diverged()
    {
        var o = DiscountOpportunity();
        PrepareScenario(o, Guid.NewGuid());
        o.Approve(false, Guid.NewGuid(), "Bia", DateTime.UtcNow);
        o.Invoking(x => x.VerifyExecution(TaxExecutionOutcome.Diverged, " ", Guid.NewGuid(), "u", DateTime.UtcNow)).Should().Throw<DomainException>();
        o.VerifyExecution(TaxExecutionOutcome.Diverged, "A NF-e saiu sem o desconto", Guid.NewGuid(), "u", DateTime.UtcNow);
        o.Status.Should().Be(TaxOpportunityStatus.ExecutionDiverged);
    }

    [Fact]
    public void An_execution_verification_records_the_decision_it_seals()
    {
        var decision = Guid.NewGuid();
        var x = TaxExecutionVerification.Create(Guid.NewGuid(), Guid.NewGuid(), decision, TaxExecutionOutcome.Verified, " chave ", " 135 ", null,
            "{\"decision\":{}}", "hash", Guid.NewGuid(), "Bia", DateTime.UtcNow);
        (x.DecisionId, x.FiscalDocumentReference, x.AuthorizationProtocol, x.Outcome).Should().Be((decision, "chave", "135", TaxExecutionOutcome.Verified));
        TaxOpportunityServiceNotice().Should().Contain("não a veracidade material");
    }

    private static string TaxOpportunityServiceNotice() => TaxOpportunityService.IntegrityNotice;

    [Fact]
    public void The_no_recharge_answer_blocks_as_a_document_mismatch()
        => TaxGuardrails.OnRequirementAnswered(UnconditionalDiscountDetector.DetectorKey, UnconditionalDiscountDetector.RequirementNoRecharge, met: false)!
            .Structure.Should().Be(ProhibitedStructure.DocumentMismatch);

    // ── maker-checker ───────────────────────────────────────────────────────────

    [Fact]
    public void High_risk_requires_a_different_approver()
    {
        var policy = new TaxIntelligencePolicyOptions();
        policy.RequiredFor(TaxRiskLevel.High, 1m).Should().Be(ApprovalRequirement.IndependentApprover);
        policy.RequiredFor(TaxRiskLevel.Medium, 1m).Should().Be(ApprovalRequirement.IdentifiedReviewer);
        policy.RequiredFor(TaxRiskLevel.Low, 1m).Should().Be(ApprovalRequirement.SelfApprovalWithJustification);

        var ana = Guid.NewGuid();
        var o = DiscountOpportunity(TaxRiskLevel.High);
        PrepareScenario(o, ana);
        o.Invoking(x => x.Approve(true, ana, "Ana", DateTime.UtcNow, ApprovalRequirement.IndependentApprover, "sou só eu"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_approval_requires_independent_approver");
        o.Approve(true, Guid.NewGuid(), "Bia", DateTime.UtcNow, ApprovalRequirement.IndependentApprover);
        o.Status.Should().Be(TaxOpportunityStatus.Approved);
    }

    [Fact]
    public void Whoever_reviewed_the_evidence_is_not_an_independent_approver()
    {
        var o = DiscountOpportunity(TaxRiskLevel.High);
        PrepareScenario(o, Guid.NewGuid());
        var bia = Guid.NewGuid();
        o.ReviewEvidence(o.Evidence.Single().Id, true, null, bia, "Bia", DateTime.UtcNow);

        o.Analysts.Should().Contain(bia, "accepting evidence is taking part in the analysis");
        o.Invoking(x => x.Approve(true, bia, "Bia", DateTime.UtcNow, ApprovalRequirement.IndependentApprover))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_approval_requires_independent_approver");
    }

    [Fact]
    public void A_material_amount_requires_a_different_approver()
    {
        var policy = new TaxIntelligencePolicyOptions { MaterialityThreshold = 1000m };
        policy.RequiredFor(TaxRiskLevel.Low, 999.99m).Should().Be(ApprovalRequirement.SelfApprovalWithJustification);
        policy.RequiredFor(TaxRiskLevel.Low, 1000m).Should().Be(ApprovalRequirement.IndependentApprover);
        new TaxIntelligencePolicyOptions().RequiredFor(TaxRiskLevel.Low, 1_000_000m).Should().Be(ApprovalRequirement.SelfApprovalWithJustification,
            "without a configured threshold there is no materiality rule");
    }

    [Fact]
    public void A_self_approval_justification_does_not_bypass_independence_or_the_reviewer()
    {
        var ana = Guid.NewGuid();
        var medium = DiscountOpportunity(TaxRiskLevel.Medium);
        PrepareScenario(medium, ana);
        medium.Invoking(x => x.Approve(false, ana, "Ana", DateTime.UtcNow, ApprovalRequirement.IdentifiedReviewer, "equipe pequena"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_approval_requires_reviewer");

        var low = DiscountOpportunity();
        PrepareScenario(low, ana);
        low.Approve(false, ana, "Ana", DateTime.UtcNow, ApprovalRequirement.SelfApprovalWithJustification, "equipe pequena");
        low.Status.Should().Be(TaxOpportunityStatus.Approved, "self-approval stays possible, on record, where the policy allows it");
    }

    // ── professional review ─────────────────────────────────────────────────────

    private static TaxRuleVersion Version() => TaxRuleVersion.Create(Guid.NewGuid(), 1, new DateOnly(2026, 1, 1), null, 1, "[]", "{}", "h", "platform", "a", null, []);

    [Fact]
    public void A_professional_review_keeps_scope_validity_assumptions_and_caveats()
    {
        var v = Version();
        v.Invoking(x => x.RecordProfessionalReview(new(ProfessionalReviewStatus.ApprovedWithCaveats, "Contadora", null, "saídas", new DateOnly(2026, 1, 1),
            null, null, null, null), DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_review_caveats");
        v.Invoking(x => x.RecordProfessionalReview(new(ProfessionalReviewStatus.Approved, "Contadora", null, null, null, null, null, null, null), DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_review_scope");

        v.RecordProfessionalReview(new(ProfessionalReviewStatus.ApprovedWithCaveats, "Contadora", "CRC 0", "Saídas no regime regular",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Desconto destacado", "Não cobre regimes específicos", null), DateTime.UtcNow);
        (v.ProfessionalReviewScope, v.ProfessionalReviewCaveats, v.ProfessionalReviewAssumptions, v.ProfessionalReference)
            .Should().Be(("Saídas no regime regular", "Não cobre regimes específicos", "Desconto destacado", "CRC 0"));
        v.IsProfessionallyApprovedOn(new DateOnly(2026, 6, 1)).Should().BeTrue();
        v.IsProfessionallyApprovedOn(new DateOnly(2027, 1, 1)).Should().BeFalse("an approval holds only within its validity");
    }

    [Fact]
    public void ReviewReceived_is_not_Approved()
    {
        var v = Version();
        v.RecordProfessionalReview(new(ProfessionalReviewStatus.ReviewReceived, "Escritório", null, null, null, null, null, null, "apto após ajustes"),
            DateTime.UtcNow);
        v.ProfessionalReview.Should().Be(ProfessionalReviewStatus.ReviewReceived);
        v.IsProfessionallyApprovedOn(new DateOnly(2026, 6, 1)).Should().BeFalse();
        v.Invoking(x => x.RecordProfessionalReview(new(ProfessionalReviewStatus.Pending, "x", null, null, null, null, null, null, null), DateTime.UtcNow))
            .Should().Throw<DomainException>();
    }

    // ── credits ─────────────────────────────────────────────────────────────────

    private static TaxCredit Potential() => TaxCredit.Register(Guid.NewGuid(), CreditTax.Ibs, "NF-e", "35261012345678000190550010000012341000012345",
        null, new DateOnly(2026, 6, 1), null, 50m, 40m, "estimativa", null);

    [Fact]
    public void A_potential_credit_is_not_appropriated()
    {
        var c = Potential();
        c.Lifecycle().Should().Be(TaxCreditLifecycle.PotentialIdentified);
        c.Entries.Should().BeEmpty("an estimate writes nothing to the ledger");
        c.Balance().Available.Should().Be(0m);
        c.Invoking(x => x.Appropriate(10m, "x", new DateOnly(2026, 7, 1), Guid.NewGuid(), "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_credit_stage");
        c.Invoking(x => x.ConfirmToBeAppropriated(10m, "ok", new DateOnly(2026, 7, 1), Guid.NewGuid(), "u"))
            .Should().Throw<DomainException>("requirements are reviewed before they are confirmed");
    }

    [Fact]
    public void To_be_appropriated_is_not_potential()
    {
        var c = Potential();
        c.StartRequirementsReview();
        c.Lifecycle().Should().Be(TaxCreditLifecycle.RequirementsUnderReview);
        c.ConfirmToBeAppropriated(35m, "nota idônea conferida", new DateOnly(2026, 7, 1), Guid.NewGuid(), "u");

        c.Lifecycle().Should().Be(TaxCreditLifecycle.ToBeAppropriated);
        c.Balance().ToBeAppropriated.Should().Be(35m);
        c.Balance().Potential.Should().Be(40m, "the estimate stays on record, apart");
        c.RecognitionBasis.Should().Be("nota idônea conferida");
        c.Appropriate(35m, "apropriação", new DateOnly(2026, 7, 2), Guid.NewGuid(), "u");
        c.Lifecycle().Should().Be(TaxCreditLifecycle.Appropriated);
    }

    [Fact]
    public void A_discarded_potential_credit_never_moves_again()
    {
        var c = Potential();
        c.Discard("fornecedor no Simples");
        c.Lifecycle().Should().Be(TaxCreditLifecycle.Discarded);
        c.Invoking(x => x.StartRequirementsReview()).Should().Throw<DomainException>();
    }

    // ── split ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_split_expectation_is_not_a_settlement()
    {
        SplitReconciliation.Of(12.34m, [], liveSettlements: 0).Status.Should().Be(SplitReconciliationStatus.Pending,
            "an expectation alone never reads as retained or paid");
        SplitReconciliation.Of(12.34m, [12.34m], liveSettlements: 1).Status.Should().Be(SplitReconciliationStatus.Matched);
    }

    // ── waiver note v2 (literal migration) ──────────────────────────────────────

    private static IReadOnlyList<MigrationOperation> DeltaOps() => new TaxIntelligenceReviewDelta().UpOperations;

    [Fact]
    public void The_waiver_note_v2_is_a_new_version_and_v1_is_preserved()
    {
        var insert = DeltaOps().OfType<InsertDataOperation>().Single(o => o.Table == "tax_rule_versions");
        object? Col(string name) => insert.Values[0, Array.IndexOf(insert.Columns, name)];

        Col("id").Should().Be(TaxCatalogV1Amendments.WaiverV2Id);
        Col("version").Should().Be(2);
        Col("conditions").Should().Be(TaxCanonicalV1.Instance.Serialize(TaxCatalogV1Amendments.WaiverConditions));
        Col("conditions").Should().Be(TaxCanonicalV1.Instance.Serialize(TaxCatalogV1.Rules.Single(r => r.Key == TaxCatalogV1Amendments.WaiverKey).Conditions),
            "only the text changes");
        Col("result").Should().Be(TaxCanonicalV1.Instance.Serialize(TaxCatalogV1Amendments.WaiverResult));
        Col("content_hash").Should().Be(TaxCatalogV1Amendments.WaiverV2ContentHash).And.Be("87345221b058670aee375166c8e97025a230c28793ad18639261a40a47e55121");
        Col("professional_review").Should().Be("Pending");
        ((string)Col("result")!).Should().NotContain("Perfil declara");

        var update = DeltaOps().OfType<UpdateDataOperation>().Single();
        update.KeyValues[0, 0].Should().Be(TaxCatalogV1Amendments.WaiverV1Id);
        update.Columns.Should().Equal("status", "status_changed_at", "updated_at");
        update.Values[0, 0].Should().Be("Superseded", "v1 is never edited, only superseded — it still reproduces old calculations");
    }

    [Fact]
    public void The_delta_migration_is_self_contained()
    {
        var dir = Path.GetDirectoryName(MigrationFile("20261027081141_TaxIntelligenceReviewDelta.cs"))!;
        foreach (var file in Directory.GetFiles(dir, "*_TaxIntelligenceReviewDelta*.cs").Where(f => !f.EndsWith(".Designer.cs")))
            foreach (var forbidden in new[] { "TaxCatalogV1", "TaxCanonical", "TaxEngine", "Nexo.Application", "Nexo.Domain", "JsonSerializer" })
                File.ReadAllText(file).Should().NotContain(forbidden, Path.GetFileName(file));
    }

    // ── regressions against statements the review documents got wrong ───────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "nexo-backend", "src"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string MigrationFile(string name)
        => Path.Combine(RepoRoot(), "nexo-backend", "src", "Nexo.Infrastructure", "Persistence", "Migrations", name);

    /// <summary>Product code and catalogue texts of the tax module (backend and frontend), not the docs that explain the rejections.</summary>
    private static IEnumerable<(string File, string Text)> TaxCode()
    {
        var root = RepoRoot();
        var dirs = new[]
        {
            Path.Combine(root, "nexo-backend", "src"),
            Path.Combine(root, "nexo-main", "src", "modules", "tax"),
        };
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".cs") || f.EndsWith(".ts") || f.EndsWith(".tsx")) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
                yield return (f, File.ReadAllText(f));
    }

    private static IEnumerable<string> CatalogueTexts()
        => TaxCatalogV1.Rules.Select(r => r.Result).Append(TaxCatalogV1Amendments.WaiverResult).OfType<NoteResult>().Select(n => n.Text)
            .Concat(TaxCatalogV1.Rules.Select(r => r.Description))
            .Concat(new UnconditionalDiscountDetector().Detect(V1, Input(), Catalogue()).Select(c => c.Caveats ?? ""))
            .Concat([TaxComplianceAdvisories.Pnct, TaxComplianceAdvisories.Declared, TaxComplianceAdvisories.Remediation]);

    [Fact]
    public void IBS_CBS_2026_is_never_said_to_be_compensated_with_ICMS_or_ISS()
    {
        var wrong = new Regex(@"compens[^.;]{0,160}\b(ICMS|ISS)\b|\b(ICMS|ISS)\b[^.;]{0,160}compens", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        CatalogueTexts().Should().NotContain(t => wrong.IsMatch(t));
        TaxCode().Where(x => wrong.IsMatch(x.Text)).Select(x => x.File).Should().BeEmpty();
        TaxCatalogV1.Rules.Single(r => r.Key == "IBSCBS.NOTE.2026-COMPENSATION").Result.As<NoteResult>().Text.Should().Contain("PIS e a Cofins");
    }

    [Fact]
    public void Classification_uses_cClassTrib_and_cClassInt_does_not_exist()
    {
        typeof(TaxLineRequest).GetProperty(nameof(TaxLineRequest.CClassTrib)).Should().NotBeNull();
        typeof(TaxTransactionLine).GetProperty(nameof(TaxTransactionLine.CClassTrib)).Should().NotBeNull();
        TaxCode().Where(x => x.Text.Contains("cClassInt", StringComparison.OrdinalIgnoreCase)).Select(x => x.File).Should().BeEmpty();
    }

    [Fact]
    public void Personal_use_or_consumption_is_never_attributed_generically_to_article_48()
    {
        var wrong = new Regex(@"art(\.|igo)\s*48\b[^.;]{0,200}uso ou consumo pessoal|uso ou consumo pessoal[^.;]{0,200}art(\.|igo)\s*48\b",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        CatalogueTexts().Concat(TaxCatalogV1.Sources.Select(s => s.Excerpt)).Should().NotContain(t => wrong.IsMatch(t));
        TaxCode().Where(x => wrong.IsMatch(x.Text)).Select(x => x.File).Should().BeEmpty();
        TaxCatalogV1.Sources.Should().NotContain(s => s.Code.Contains("ART48"), "no rule rests on art. 48 without a verified reading");
    }

    // ── retention ───────────────────────────────────────────────────────────────

    [Fact]
    public void Nothing_purges_dossiers_or_tax_records_automatically()
    {
        var purge = new Regex(@"(TaxDecisions|TaxExecutionVerifications|TaxCreditLedgerEntries|TaxCalculations|TaxComplianceAssessments)\s*\.\s*(Remove|RemoveRange|ExecuteDelete)|DELETE\s+FROM\s+nexo\.tax_",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        TaxCode().Where(x => !x.File.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") && purge.IsMatch(x.Text))
            .Select(x => x.File).Should().BeEmpty("retention is pending legal definition: no automatic disposal");
        typeof(TaxOpportunityService).Assembly.GetTypes()
            .Where(t => t.Namespace?.Contains("TaxIntelligence") == true && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(t))
            .Should().BeEmpty("no background job disposes of tax records");
    }
}
