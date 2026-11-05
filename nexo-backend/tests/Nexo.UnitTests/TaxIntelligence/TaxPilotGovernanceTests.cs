using System.Text.RegularExpressions;
using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.UnitTests.TaxIntelligence;

/// <summary>
/// The internal pilot approval (INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT) is a
/// dimension of its own: it never changes the professional review, never reaches a version that is not
/// active, carries its caveats, and only the controlled pilot accepts it.
/// </summary>
public class TaxPilotGovernanceTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static TaxRuleVersion Version(TaxRuleStatus status = TaxRuleStatus.Active, DateOnly? validTo = null)
    {
        var v = TaxRuleVersion.Create(Guid.NewGuid(), 1, From, validTo ?? To, 10, "[]", "{}", "hash", "test", "test", null, []);
        if (status != TaxRuleStatus.Draft) v.ChangeStatus(status, DateTime.UtcNow);
        return v;
    }

    private static readonly Guid Actor = Guid.NewGuid();

    private static InternalApprovalInput Input(DateOnly? from = null, DateOnly? to = null, string caveats = "Ressalva aceita.", string reference = "docs/decisao.md")
        => new(TaxGovernance.PilotPolicy, Actor, "Operador da plataforma", "Platform", "Recorte 2026", from ?? From, to ?? To, caveats, reference);

    private static readonly DateOnly Mid = new(2026, 6, 15);

    [Fact]
    public void An_active_version_is_approved_for_the_pilot_and_its_professional_review_stays_pending()
    {
        var v = Version();
        v.ApproveForControlledPilot(Input(), DateTime.UtcNow);

        v.InternalApproval.Should().Be(InternalApprovalStatus.ApprovedForControlledPilot);
        v.IsApprovedForPilotOn(Mid).Should().BeTrue("Active + approved + inside the validity");
        v.IsApprovedForPilotOn(new DateOnly(2027, 1, 1)).Should().BeFalse("the approval holds only inside its validity");
        v.ProfessionalReview.Should().Be(ProfessionalReviewStatus.Pending, "the internal approval is not a professional review");
        v.IsProfessionallyApprovedOn(Mid).Should().BeFalse();
        v.ProfessionalReviewedBy.Should().BeNull();
        v.ProfessionalReference.Should().BeNull();
        v.InternalApprovalCaveats.Should().Be("Ressalva aceita.");
        v.InternalApprovedById.Should().Be(Actor, "the actor is the authenticated user");
        v.InternalApprovedByRole.Should().Be("Platform");
    }

    [Theory]
    [InlineData(TaxRuleStatus.Revoked)]
    [InlineData(TaxRuleStatus.Superseded)]
    public void A_version_revoked_or_superseded_after_approval_keeps_the_record_but_authorises_nothing_new(TaxRuleStatus later)
    {
        var v = Version();
        v.ApproveForControlledPilot(Input(reference: "DEC-1"), DateTime.UtcNow);
        v.ChangeStatus(later, DateTime.UtcNow);

        v.IsApprovedForPilotOn(Mid).Should().BeFalse("only an Active version authorises new work");
        v.InternalApproval.Should().Be(InternalApprovalStatus.ApprovedForControlledPilot, "the history of the approval is kept");
        v.InternalApprovalDecisionRef.Should().Be("DEC-1");
        TaxGovernance.Satisfies(TaxApprovalMode.ControlledPilot, technicallyActive: false, approvedForPilot: true, professionallyApproved: true)
            .Should().BeFalse("not even a professional approval revives a version that is no longer active");
    }

    [Theory]
    [InlineData(TaxRuleStatus.Draft)]
    [InlineData(TaxRuleStatus.PendingRegulation)]
    [InlineData(TaxRuleStatus.Unverified)]
    [InlineData(TaxRuleStatus.Revoked)]
    public void A_version_that_is_not_active_is_never_approved_for_the_pilot(TaxRuleStatus status)
    {
        var v = Version(status);
        v.Invoking(x => x.ApproveForControlledPilot(Input(), DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_approval_status");
        v.InternalApproval.Should().Be(InternalApprovalStatus.NotApproved);
    }

    [Fact]
    public void The_approval_names_its_actor_states_its_caveats_and_stays_inside_the_versions_validity()
    {
        var v = Version();
        v.Invoking(x => x.ApproveForControlledPilot(Input() with { ApprovedById = Guid.Empty }, DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_approval_fields");
        v.Invoking(x => x.ApproveForControlledPilot(Input(caveats: " "), DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_approval_caveats");
        v.Invoking(x => x.ApproveForControlledPilot(Input(to: new DateOnly(2027, 12, 31)), DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_approval_validity");
        v.Invoking(x => x.ApproveForControlledPilot(Input(from: new DateOnly(2025, 12, 1)), DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_approval_validity");
    }

    [Fact]
    public void A_withdrawn_approval_no_longer_counts_and_keeps_its_record()
    {
        var v = Version();
        v.ApproveForControlledPilot(Input(), DateTime.UtcNow);
        v.WithdrawPilotApproval("Critério de suspensão atingido", DateTime.UtcNow);

        v.InternalApproval.Should().Be(InternalApprovalStatus.Withdrawn);
        v.IsApprovedForPilotOn(Mid).Should().BeFalse();
        v.InternalApprovalCaveats.Should().NotBeNull("the record of what was approved is kept");
        v.Invoking(x => x.WithdrawPilotApproval("de novo", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(TaxApprovalMode.ControlledPilot, true, false, true)]
    [InlineData(TaxApprovalMode.ControlledPilot, false, true, true)]
    [InlineData(TaxApprovalMode.ControlledPilot, false, false, false)]
    [InlineData(TaxApprovalMode.ProfessionalFinal, true, false, false)]
    [InlineData(TaxApprovalMode.ProfessionalFinal, false, true, true)]
    [InlineData(TaxApprovalMode.Disabled, true, true, false)]
    public void Only_the_controlled_pilot_accepts_the_internal_approval_and_disabled_accepts_nothing(
        TaxApprovalMode mode, bool pilot, bool professional, bool satisfied)
        => TaxGovernance.Satisfies(mode, technicallyActive: true, pilot, professional).Should().Be(satisfied);

    [Fact]
    public void With_nothing_configured_the_module_is_off_and_no_approval_mode_is_set()
    {
        var defaults = new Nexo.Infrastructure.TaxIntelligence.TaxIntelligencePolicyOptions();
        defaults.Enabled.Should().BeFalse("the kill switch is off unless configured");
        defaults.Mode.Should().Be(TaxApprovalMode.Disabled, "the controlled pilot is never a fallback");
        defaults.RequireApprovedRulesForCalculations.Should().BeTrue();
        defaults.RequireApprovedRulesForDecisions.Should().BeTrue();
        default(TaxApprovalMode).Should().Be(TaxApprovalMode.Disabled);
    }

    private static TaxRuleGovernanceDto Rule(string key, bool pilot, bool professional, string? reference = null,
        TaxRuleStatus status = TaxRuleStatus.Active) => new(
        Guid.NewGuid(), key, 1, status,
        pilot ? InternalApprovalStatus.ApprovedForControlledPilot : InternalApprovalStatus.NotApproved, pilot, pilot ? TaxGovernance.PilotPolicy : null,
        pilot ? Actor : null, pilot ? "Operador" : null, pilot ? "Platform" : null, pilot ? DateTime.UtcNow : null, pilot ? "Recorte" : null,
        pilot ? From : null, pilot ? To : null, pilot ? "ressalva" : null, reference,
        professional ? ProfessionalReviewStatus.Approved : ProfessionalReviewStatus.Pending, professional, null, null);

    [Fact]
    public void A_governance_snapshot_lists_what_the_mode_does_not_accept_every_pending_review_and_every_decision()
    {
        var rules = new[]
        {
            Rule("A", pilot: true, professional: false, reference: "DEC-1"),
            Rule("B", pilot: false, professional: false),
            Rule("C", pilot: true, professional: false, reference: "DEC-2"),
            Rule("D", pilot: true, professional: false, reference: "DEC-1", status: TaxRuleStatus.Revoked),
        };

        var pilot = new TaxGovernanceDto(TaxApprovalMode.ControlledPilot, TaxGovernance.PilotPolicy, true, TaxGovernance.PilotNature, "",
            TaxGovernance.ShadowNotice, null, rules);
        pilot.NotApproved.Should().Equal("B v1", "D v1");
        pilot.ProfessionalReviewPending.Should().Equal("A v1", "B v1", "C v1", "D v1");
        pilot.DecisionReferences.Should().Equal("DEC-1", "DEC-2");

        var final = pilot with { Mode = TaxApprovalMode.ProfessionalFinal, Policy = TaxGovernance.ProfessionalPolicy };
        final.NotApproved.Should().Equal("A v1", "B v1", "C v1", "D v1");
        TaxGovernance.PolicyFor(TaxApprovalMode.ControlledPilot).Should().NotBe(TaxGovernance.PolicyFor(TaxApprovalMode.ProfessionalFinal));
        TaxGovernance.NotAProfessionalOpinion.Should().Contain("Não constitui parecer");
    }

    // ── pilot scope ─────────────────────────────────────────────────────────────

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid StoreA1 = Guid.NewGuid();
    private static readonly Guid StoreA2 = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static TaxPilotScope OpenScope(DateOnly? to = null)
    {
        var s = TaxPilotScope.Create();
        s.Configure(true, From, to, "docs/decisao.md", Actor, "Operador", DateTime.UtcNow);
        return s;
    }

    [Fact]
    public void No_tenant_is_in_the_pilot_by_default_and_a_new_scope_is_closed()
    {
        TaxPilotScope.Create().Refusal(TenantA, StoreA1, Today)!.Value.Code.Should().Be("tax_pilot_scope_disabled");
        OpenScope().Refusal(TenantA, StoreA1, Today)!.Value.Code.Should().Be("tax_pilot_scope_tenant");
    }

    [Fact]
    public void An_allowlisted_tenant_is_admitted_while_the_window_is_open_and_not_after()
    {
        var s = OpenScope(to: new DateOnly(2026, 12, 31));
        s.AddParticipant(TenantA, null, Actor, "Operador", DateTime.UtcNow);
        s.Refusal(TenantA, StoreA1, Today).Should().BeNull();
        s.Refusal(TenantA, StoreA1, new DateOnly(2027, 1, 1))!.Value.Code.Should().Be("tax_pilot_scope_expired");
        s.Refusal(Guid.NewGuid(), StoreA1, Today)!.Value.Code.Should().Be("tax_pilot_scope_tenant");
    }

    [Fact]
    public void A_store_allowlist_admits_only_its_stores_and_removal_closes_it()
    {
        var s = OpenScope();
        var p = s.AddParticipant(TenantA, StoreA1, Actor, "Operador", DateTime.UtcNow);
        s.Refusal(TenantA, StoreA1, Today).Should().BeNull();
        s.Refusal(TenantA, StoreA2, Today)!.Value.Code.Should().Be("tax_pilot_scope_store");
        s.Invoking(x => x.AddParticipant(TenantA, StoreA1, Actor, "Operador", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_participant_duplicate");

        s.RemoveParticipant(p.Id, "Fim da participação", Actor, "Operador", DateTime.UtcNow);
        s.Refusal(TenantA, StoreA1, Today)!.Value.Code.Should().Be("tax_pilot_scope_tenant");
        s.Participants.Single().RemovalReason.Should().Be("Fim da participação", "removal is recorded, never deleted");
    }

    [Fact]
    public void Opening_the_pilot_states_who_did_it_and_the_decision_record()
    {
        var s = TaxPilotScope.Create();
        s.Invoking(x => x.Configure(true, From, null, " ", Actor, "Operador", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_scope_decision");
        s.Invoking(x => x.Configure(true, From, null, "docs/decisao.md", Guid.Empty, "Operador", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_scope_actor");
        s.Invoking(x => x.Configure(true, To, From, "docs/decisao.md", Actor, "Operador", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_pilot_scope_validity");
    }

    // ── shadow mode: the tax module reaches no external fiscal effect ──────────

    [Fact]
    public void Tax_services_depend_on_no_adapter_that_acts_on_anything_fiscal()
    {
        var forbidden = new[] { typeof(Nexo.Application.Features.Fiscal.IFiscalDocumentProvider) };
        var services = typeof(Nexo.Infrastructure.TaxIntelligence.TaxCalculationService).Assembly.GetTypes()
            .Where(t => t.Namespace == "Nexo.Infrastructure.TaxIntelligence" && t.IsClass)
            .ToList();
        services.Should().NotBeEmpty();
        foreach (var t in services)
            foreach (var ctor in t.GetConstructors())
                foreach (var param in ctor.GetParameters())
                {
                    forbidden.Should().NotContain(param.ParameterType, $"{t.Name} must not reach a fiscal document adapter");
                    (param.ParameterType.Namespace ?? "").Should().NotStartWith("Nexo.Application.Features.Sales",
                        $"{t.Name} must not depend on the sales services (it never changes a sale or a price)");
                }

        // The split payment provider only reports (configuration status and, once a real one exists, the retentions it did): no member
        // can settle, retain, pay or transmit anything.
        typeof(Nexo.Infrastructure.TaxIntelligence.ISplitPaymentProvider).GetMethods().Where(m => !m.IsSpecialName)
            .Select(m => m.Name).Should().OnlyContain(n => n.StartsWith("Fetch", StringComparison.Ordinal), "the pilot never settles a split with a provider");
    }

    [Fact]
    public void No_migration_approves_a_rule_or_opens_the_pilot()
    {
        var migrations = Path.Combine(RepoRoot(), "nexo-backend", "src", "Nexo.Infrastructure", "Persistence", "Migrations");
        foreach (var file in Directory.EnumerateFiles(migrations, "*.cs").Where(f => !f.EndsWith(".Designer.cs") && !f.EndsWith("ModelSnapshot.cs")))
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("\"ApprovedForControlledPilot\"", $"{Path.GetFileName(file)}: a governance decision is never a migration");
            Regex.IsMatch(text, @"InsertData\([^;]*tax_pilot_", RegexOptions.Singleline).Should().BeFalse($"{Path.GetFileName(file)} seeds no pilot scope");
        }
    }

    // ── no record pretends a professional opinion exists ────────────────────────

    private static readonly Regex ProfessionalIdentity = new(@"\b(OAB|CRC)\b\s*(/\s*[A-Z]{2})?\s*(n[º°o.]*\s*)?\d", RegexOptions.IgnoreCase);

    // Exact phrases (as a label or a claim would carry them); code comments that negate them are fine.
    private static readonly string[] FalseClaims =
    [
        "parecer profissional aprovado", "Professionally Approved", "Consultoria Tributária Especializada", "Parecerista Responsável",
        "homologação profissional concluída", "economia garantida", "tax saving garantido", "garantia de economia",
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "nexo-backend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SourceFiles(string root, string relative, params string[] extensions)
        => Directory.EnumerateFiles(Path.Combine(root, relative), "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains(".test.", StringComparison.Ordinal));

    [Fact]
    public void No_seed_migration_or_product_text_invents_a_professional_identity_or_opinion()
    {
        var root = RepoRoot();
        var files = SourceFiles(root, Path.Combine("nexo-backend", "src"), ".cs")
            .Concat(SourceFiles(root, Path.Combine("nexo-main", "src"), ".ts", ".tsx"))
            .ToList();
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            ProfessionalIdentity.IsMatch(text).Should().BeFalse($"{file} must not carry an OAB/CRC registration");
            foreach (var claim in FalseClaims)
                text.Should().NotContain(claim, $"{file} must not claim \"{claim}\"");
        }
    }
}
