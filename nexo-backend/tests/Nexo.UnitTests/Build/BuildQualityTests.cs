using FluentAssertions;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>Wave 3 (Qualidade e pós-obra): pure rules of inspections, non-conformities, the stage 100% guard,
/// warranty coverage, handover acceptance and assistance.</summary>
public class BuildQualityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc);

    private static BuildInspectionItem Item(bool required, BuildInspectionItemResult result, bool requiresPhoto = false,
        params string[] photos)
    {
        var i = BuildInspectionItem.Create(Tenant, Guid.NewGuid(), 1, "Item", required, requiresPhoto);
        i.Record(result, null, photos, null);
        return i;
    }

    private static BuildInspection Inspection()
        => BuildInspection.Create(Tenant, Project, BuildInspectionKind.Quality, "Vistoria", null, null, null, null, null, User);

    // ── Inspection outcome ────────────────────────────────────────────────────

    [Fact]
    public void All_ok_or_not_applicable_passes()
        => BuildInspection.ComputeOutcome([Item(true, BuildInspectionItemResult.Ok), Item(true, BuildInspectionItemResult.NotApplicable)])
            .Should().Be(BuildInspectionStatus.Passed);

    [Fact]
    public void An_optional_not_ok_passes_with_remarks()
        => BuildInspection.ComputeOutcome([Item(true, BuildInspectionItemResult.Ok), Item(false, BuildInspectionItemResult.NotOk)])
            .Should().Be(BuildInspectionStatus.PassedWithRemarks);

    [Fact]
    public void A_required_not_ok_fails_even_with_other_remarks()
        => BuildInspection.ComputeOutcome([Item(true, BuildInspectionItemResult.NotOk), Item(false, BuildInspectionItemResult.NotOk)])
            .Should().Be(BuildInspectionStatus.Failed);

    [Fact]
    public void A_required_pending_item_blocks_completion_but_an_optional_one_does_not()
    {
        var act = () => BuildInspection.ComputeOutcome([Item(true, BuildInspectionItemResult.Pending), Item(false, BuildInspectionItemResult.Ok)]);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("inspection_required_items_pending");
        BuildInspection.ComputeOutcome([Item(false, BuildInspectionItemResult.Pending), Item(true, BuildInspectionItemResult.Ok)])
            .Should().Be(BuildInspectionStatus.Passed);
    }

    [Fact]
    public void An_inspection_without_items_cannot_be_completed()
        => FluentActions.Invoking(() => BuildInspection.ComputeOutcome([]))
            .Should().Throw<DomainException>().Which.Code.Should().Be("inspection_empty");

    [Theory]
    [InlineData(BuildInspectionItemResult.Ok)]
    [InlineData(BuildInspectionItemResult.NotOk)]
    public void An_item_that_requires_a_photo_needs_one_when_judged(BuildInspectionItemResult result)
    {
        var act = () => BuildInspection.ComputeOutcome([Item(false, result, requiresPhoto: true)]);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("inspection_photo_required");
        BuildInspection.ComputeOutcome([Item(false, result, true, "private/tenants/x/build/quality/a.jpg")])
            .Should().NotBe(BuildInspectionStatus.Failed);
    }

    [Fact]
    public void Not_applicable_needs_no_photo()
        => BuildInspection.ComputeOutcome([Item(true, BuildInspectionItemResult.NotApplicable, requiresPhoto: true)])
            .Should().Be(BuildInspectionStatus.Passed);

    [Fact]
    public void Completing_twice_is_a_conflict_and_a_cancelled_inspection_cannot_be_completed()
    {
        var i = Inspection();
        i.Complete([Item(true, BuildInspectionItemResult.Ok)], null, "Ana", Now).Should().Be(BuildInspectionStatus.Passed);
        i.CompletedAt.Should().Be(Now);
        i.Invoking(x => x.Complete([Item(true, BuildInspectionItemResult.Ok)], null, "Ana", Now)).Should().Throw<ConflictException>();
        i.Invoking(x => x.Cancel()).Should().Throw<ConflictException>();

        var cancelled = Inspection();
        cancelled.Cancel();
        cancelled.Invoking(x => x.Complete([Item(true, BuildInspectionItemResult.Ok)], null, "Ana", Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("inspection_closed");
    }

    [Fact]
    public void A_not_ok_item_opens_a_nc_with_its_severity_or_by_whether_it_is_required()
    {
        Item(true, BuildInspectionItemResult.NotOk).NonConformitySeverity.Should().Be(BuildNonConformitySeverity.High);
        Item(false, BuildInspectionItemResult.NotOk).NonConformitySeverity.Should().Be(BuildNonConformitySeverity.Medium);
        var critical = BuildInspectionItem.Create(Tenant, Guid.NewGuid(), 1, "Armadura", false, false);
        critical.Record(BuildInspectionItemResult.NotOk, null, null, BuildNonConformitySeverity.Critical);
        critical.NonConformitySeverity.Should().Be(BuildNonConformitySeverity.Critical);
    }

    // ── Non-conformities ──────────────────────────────────────────────────────

    private static BuildNonConformity Nc(BuildNonConformitySeverity severity = BuildNonConformitySeverity.Critical, DateOnly? due = null)
        => BuildNonConformity.Create(Tenant, Project, 1, "Fissura", null, severity, null, null, due, null, null, User, "Ana");

    [Fact]
    public void Nc_goes_open_treatment_resolved_verified()
    {
        var nc = Nc();
        nc.BlocksCompletion.Should().BeTrue();
        nc.StartTreatment();
        nc.Status.Should().Be(BuildNonConformityStatus.InTreatment);
        nc.BlocksCompletion.Should().BeTrue("in treatment is still open");
        nc.Resolve("Reforço com tela", "Retração", User, "Ana", Now);
        nc.Status.Should().Be(BuildNonConformityStatus.Resolved);
        nc.BlocksCompletion.Should().BeFalse();
        var verifier = Guid.NewGuid();
        nc.Verify(verifier, "Bruno", Now);
        nc.Status.Should().Be(BuildNonConformityStatus.Verified);
        nc.VerifiedBySameUser.Should().BeFalse();
    }

    [Fact]
    public void Nc_verification_only_after_resolved_and_same_user_is_recorded()
    {
        var nc = Nc();
        nc.Invoking(x => x.Verify(User, "Ana", Now)).Should().Throw<DomainException>().Which.Code.Should().Be("nc_not_resolved");
        nc.Resolve("Correção", null, User, "Ana", Now);
        nc.Verify(User, "Ana", Now);
        nc.VerifiedBySameUser.Should().BeTrue();
        nc.Invoking(x => x.Verify(User, "Ana", Now)).Should().Throw<ConflictException>();
    }

    [Fact]
    public void Nc_resolution_needs_a_corrective_action_and_is_a_conflict_when_repeated()
    {
        var nc = Nc();
        nc.Invoking(x => x.Resolve(null, null, User, "Ana", Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("nc_corrective_action_required");
        nc.Resolve("Refeito", null, User, "Ana", Now);
        nc.Invoking(x => x.Resolve("Refeito", null, User, "Ana", Now)).Should().Throw<ConflictException>();
        nc.Invoking(x => x.Cancel("duplicada")).Should().Throw<DomainException>().Which.Code.Should().Be("nc_invalid_transition");
        nc.Reopen();
        nc.Status.Should().Be(BuildNonConformityStatus.InTreatment);
        nc.ResolvedAt.Should().BeNull();
        nc.CorrectiveAction.Should().Be("Refeito", "kept for the next resolution");
    }

    [Fact]
    public void Nc_is_overdue_only_while_open_and_past_its_due_date()
    {
        var today = new DateOnly(2026, 10, 30);
        Nc(due: today.AddDays(-1)).IsOverdue(today).Should().BeTrue();
        Nc(due: today).IsOverdue(today).Should().BeFalse();
        Nc().IsOverdue(today).Should().BeFalse();
        var resolved = Nc(due: today.AddDays(-5));
        resolved.Resolve("ok", null, User, "Ana", Now);
        resolved.IsOverdue(today).Should().BeFalse();
    }

    // ── Stage 100% guard (shared progress path) ───────────────────────────────

    private sealed class FakeRecorder(bool critical) : IBuildProgressRecorder
    {
        public List<BuildStageProgressEvent> Events { get; } = [];
        public int Checks { get; private set; }
        public void Stage(BuildStageProgressEvent progressEvent) => Events.Add(progressEvent);
        public Task RefreshTodaySnapshotAsync(Guid projectId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasOpenCriticalNonConformityAsync(Guid tenantId, Guid stageId, CancellationToken ct = default)
        {
            Checks++;
            return Task.FromResult(critical);
        }
    }

    private static BuildStage Stage(int percent = 0)
    {
        var s = BuildStage.Create(Tenant, Project, "Estrutura", 1);
        if (percent > 0) s.UpdateProgress(percent, new DateOnly(2026, 10, 1));
        return s;
    }

    [Fact]
    public async Task A_stage_with_an_open_critical_nc_cannot_reach_100()
    {
        var recorder = new FakeRecorder(critical: true);
        var stage = Stage(80);
        var act = () => BuildStageProgressApplier.ApplyAsync(recorder, stage, 100, new DateOnly(2026, 10, 30), BuildProgressSource.Measurement,
            User, "Ana");
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("stage_has_critical_nonconformity");
        stage.ProgressPercent.Should().Be(80);
        stage.Status.Should().NotBe(BuildStageStatus.Completed);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Below_100_and_re_applying_100_are_not_checked()
    {
        var recorder = new FakeRecorder(critical: true);
        await BuildStageProgressApplier.ApplyAsync(recorder, Stage(), 99, new DateOnly(2026, 10, 30), BuildProgressSource.Manual, User, "Ana");
        recorder.Checks.Should().Be(0);
        var done = Stage(100);
        await BuildStageProgressApplier.ApplyAsync(recorder, done, 100, new DateOnly(2026, 10, 30), BuildProgressSource.Manual, User, "Ana", "nota");
        recorder.Checks.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_critical_nc_the_stage_completes()
    {
        var recorder = new FakeRecorder(critical: false);
        var stage = Stage(50);
        var e = await BuildStageProgressApplier.ApplyAsync(recorder, stage, 100, new DateOnly(2026, 10, 30), BuildProgressSource.Manual, User, "Ana");
        e.Should().NotBeNull();
        stage.Status.Should().Be(BuildStageStatus.Completed);
        recorder.Checks.Should().Be(1);
    }

    [Fact]
    public void Domain_rule_for_completion()
    {
        FluentActions.Invoking(() => BuildNonConformity.EnsureStageCanComplete(true))
            .Should().Throw<DomainException>().WithMessage("Etapa com não conformidade crítica aberta.");
        FluentActions.Invoking(() => BuildNonConformity.EnsureStageCanComplete(false)).Should().NotThrow();
    }

    // ── Warranty coverage ─────────────────────────────────────────────────────

    private static BuildWarrantyTerm Term(string system, int months, DateOnly? start)
        => BuildWarrantyTerm.Create(Tenant, Project, system, months, start, null);

    [Fact]
    public void Coverage_boundaries_are_the_start_day_and_the_last_day_inclusive()
    {
        var t = Term("Estrutura", 60, new DateOnly(2026, 1, 15));
        t.ExpiresAt.Should().Be(new DateOnly(2031, 1, 15), "CC art. 132 §3: same day number");
        t.Covers(new DateOnly(2026, 1, 14)).Should().BeFalse("before the delivery");
        t.Covers(new DateOnly(2026, 1, 15)).Should().BeTrue();
        t.Covers(new DateOnly(2031, 1, 15)).Should().BeTrue("last covered day");
        t.Covers(new DateOnly(2031, 1, 16)).Should().BeFalse();
    }

    [Fact]
    public void Month_end_start_without_a_matching_day_expires_on_the_following_day()
    {
        var t = Term("Pintura", 1, new DateOnly(2026, 1, 31));
        t.ExpiresAt.Should().Be(new DateOnly(2026, 3, 1), "February has no 31st (CC art. 132 §3)");
    }

    [Fact]
    public void A_term_without_a_start_covers_nothing_until_the_delivery()
    {
        var t = Term("Esquadrias", 24, null);
        t.Covers(new DateOnly(2026, 10, 30)).Should().BeFalse();
        t.ExpiresAt.Should().BeNull();
        t.StartOn(new DateOnly(2026, 10, 30));
        t.StartOn(new DateOnly(2027, 1, 1));
        t.StartsAt.Should().Be(new DateOnly(2026, 10, 30), "an existing start is kept");
        t.IsActive(new DateOnly(2026, 10, 30)).Should().BeTrue();
    }

    [Fact]
    public void Coverage_evaluation_by_category()
    {
        var start = new DateOnly(2026, 1, 15);
        var estrutura = Term("Estrutura", 60, start);
        var pintura = Term("Pintura", 12, start);
        BuildWarrantyTerm[] terms = [estrutura, pintura];

        BuildWarrantyCoverage.Evaluate(terms, " estrutura ", new DateOnly(2030, 6, 1)).Should().Be(new BuildWarrantyCoverage(true, estrutura.Id));
        BuildWarrantyCoverage.Evaluate(terms, "Pintura", new DateOnly(2027, 1, 15)).Should().Be(new BuildWarrantyCoverage(true, pintura.Id));
        BuildWarrantyCoverage.Evaluate(terms, "Pintura", new DateOnly(2027, 1, 16)).Should().Be(new BuildWarrantyCoverage(false, pintura.Id));
        BuildWarrantyCoverage.Evaluate(terms, "Outros", new DateOnly(2026, 6, 1)).Should().Be(new BuildWarrantyCoverage(null, null));
    }

    [Fact]
    public void Warranty_months_must_be_reasonable_and_defaults_start_with_the_structure()
    {
        FluentActions.Invoking(() => Term("X", 0, null)).Should().Throw<DomainException>().Which.Code.Should().Be("invalid_warranty_months");
        BuildWarrantyDefaults.Terms.Should().Contain(t => t.System == "Estrutura" && t.Months == 60);
        BuildWarrantyDefaults.Terms.Select(t => t.System).Should().OnlyHaveUniqueItems();
    }

    // ── Handover ──────────────────────────────────────────────────────────────

    [Fact]
    public void Handover_is_accepted_once_after_the_walkthrough()
    {
        var h = BuildHandover.Create(Tenant, Project, new DateOnly(2026, 11, 10));
        h.Invoking(x => x.Accept("Maria", null, BuildDecisionChannel.Portal, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("handover_not_ready");
        h.MarkInspectionDone();
        h.Accept("Maria", "Ok", BuildDecisionChannel.Portal, Now);
        h.AcceptanceChannel.Should().Be(BuildDecisionChannel.Portal);
        h.Invoking(x => x.Accept("Maria", null, BuildDecisionChannel.Portal, Now)).Should().Throw<ConflictException>();
        h.Deliver(true, "Ana", Now);
        h.Invoking(x => x.Deliver(true, "Ana", Now)).Should().Throw<ConflictException>();
        h.Invoking(x => x.LinkWalkthrough(Guid.NewGuid())).Should().Throw<DomainException>();
    }

    // ── Assistance ────────────────────────────────────────────────────────────

    private static BuildAssistanceRequest Assistance()
        => BuildAssistanceRequest.Open(Tenant, Project, 1, "Pintura", "Descascando", "Parede", null, BuildDecisionChannel.Portal,
            "Cliente", null, new DateOnly(2026, 10, 30));

    [Fact]
    public void Assistance_lifecycle_and_feedback_once_after_resolution()
    {
        var a = Assistance();
        a.Invoking(x => x.GiveFeedback(5, null, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("assistance_not_resolved");
        a.Invoking(x => x.Resolve("feito", Now)).Should().Throw<DomainException>().Which.Code.Should().Be("assistance_invalid_transition");
        a.Triage(true, Guid.NewGuid(), "interno", "Ana", Now);
        a.Schedule(Now.AddDays(1));
        a.Start(Now);
        a.Resolve("Repintado", Now);
        a.Invoking(x => x.GiveFeedback(0, null, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("invalid_rating");
        a.GiveFeedback(4, "Bom", Now);
        a.Invoking(x => x.GiveFeedback(5, null, Now)).Should().Throw<ConflictException>();
        a.Close(Now);
        a.Status.Should().Be(BuildAssistanceStatus.Closed);
        a.Invoking(x => x.Reject("x", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Assistance_accepts_up_to_six_photos_while_open()
    {
        var a = Assistance();
        for (var i = 0; i < BuildAssistanceRequest.MaxPhotos; i++) a.AddPhoto($"private/tenants/t/build/assistance/{i}.png");
        a.CanAddPhotos.Should().BeFalse();
        a.Invoking(x => x.AddPhoto("private/tenants/t/build/assistance/7.png"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("assistance_photo_limit");

        var rejected = Assistance();
        rejected.Reject("Fora da garantia", Now);
        rejected.Invoking(x => x.AddPhoto("k")).Should().Throw<DomainException>().Which.Code.Should().Be("assistance_closed");
        rejected.RejectionReason.Should().Be("Fora da garantia");
    }
}
