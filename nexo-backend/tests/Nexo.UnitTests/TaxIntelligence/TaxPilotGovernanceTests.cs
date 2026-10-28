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

    private static InternalApprovalInput Input(DateOnly? from = null, DateOnly? to = null, string caveats = "Ressalva aceita.")
        => new(TaxGovernance.PilotPolicy, "Product Owner — Andrade Systems", "Recorte 2026", from ?? From, to ?? To, caveats, "docs/decisao.md");

    [Fact]
    public void An_active_version_is_approved_for_the_pilot_and_its_professional_review_stays_pending()
    {
        var v = Version();
        v.ApproveForControlledPilot(Input(), DateTime.UtcNow);

        v.InternalApproval.Should().Be(InternalApprovalStatus.ApprovedForControlledPilot);
        v.IsApprovedForPilotOn(new DateOnly(2026, 6, 15)).Should().BeTrue();
        v.IsApprovedForPilotOn(new DateOnly(2027, 1, 1)).Should().BeFalse("the approval holds only inside its validity");
        v.ProfessionalReview.Should().Be(ProfessionalReviewStatus.Pending, "the internal approval is not a professional review");
        v.IsProfessionallyApprovedOn(new DateOnly(2026, 6, 15)).Should().BeFalse();
        v.ProfessionalReviewedBy.Should().BeNull();
        v.ProfessionalReference.Should().BeNull();
        v.InternalApprovalCaveats.Should().Be("Ressalva aceita.");
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
    public void The_approval_states_its_caveats_and_stays_inside_the_versions_validity()
    {
        var v = Version();
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
        v.IsApprovedForPilotOn(new DateOnly(2026, 6, 15)).Should().BeFalse();
        v.InternalApprovalCaveats.Should().NotBeNull("the record of what was approved is kept");
        v.Invoking(x => x.WithdrawPilotApproval("de novo", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(TaxApprovalMode.ControlledPilot, true, false, true)]
    [InlineData(TaxApprovalMode.ControlledPilot, false, true, true)]
    [InlineData(TaxApprovalMode.ControlledPilot, false, false, false)]
    [InlineData(TaxApprovalMode.ProfessionalFinal, true, false, false)]
    [InlineData(TaxApprovalMode.ProfessionalFinal, false, true, true)]
    public void Only_the_controlled_pilot_accepts_the_internal_approval(TaxApprovalMode mode, bool pilot, bool professional, bool satisfied)
        => TaxGovernance.Satisfies(mode, pilot, professional).Should().Be(satisfied);

    [Fact]
    public void A_governance_snapshot_lists_what_the_mode_does_not_accept_and_every_pending_professional_review()
    {
        TaxRuleGovernanceDto Rule(string key, bool pilot, bool professional) => new(Guid.NewGuid(), key, 1,
            pilot ? InternalApprovalStatus.ApprovedForControlledPilot : InternalApprovalStatus.NotApproved, pilot, pilot ? "ressalva" : null,
            professional ? ProfessionalReviewStatus.Approved : ProfessionalReviewStatus.Pending, professional, null);
        var rules = new[] { Rule("A", pilot: true, professional: false), Rule("B", pilot: false, professional: false) };

        var pilot = new TaxGovernanceDto(TaxApprovalMode.ControlledPilot, TaxGovernance.PilotPolicy, true, TaxGovernance.PilotNature, "", rules);
        pilot.NotApproved.Should().Equal("B v1");
        pilot.ProfessionalReviewPending.Should().Equal("A v1", "B v1");

        var final = pilot with { Mode = TaxApprovalMode.ProfessionalFinal, Policy = TaxGovernance.ProfessionalPolicy };
        final.NotApproved.Should().Equal("A v1", "B v1");
        TaxGovernance.PolicyFor(TaxApprovalMode.ControlledPilot).Should().NotBe(TaxGovernance.PolicyFor(TaxApprovalMode.ProfessionalFinal));
        TaxGovernance.NotAProfessionalOpinion.Should().Contain("Não constitui parecer");
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
