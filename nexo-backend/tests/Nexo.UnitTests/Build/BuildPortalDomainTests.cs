using System.Text.Json;
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.UnitTests.Build;

public class BuildPortalSlugTests
{
    [Theory]
    [InlineData("construtora-silva")]
    [InlineData("obra-123")]
    [InlineData("abc")]
    [InlineData("a1b")]
    public void Valid_slugs(string slug) => BuildPortalSlug.Validate(slug).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("ab")]                      // too short
    [InlineData("Construtora")]            // uppercase (callers normalize first)
    [InlineData("obra_1")]                 // underscore
    [InlineData("obra 1")]                 // space
    [InlineData("-obra")]
    [InlineData("obra-")]
    [InlineData("obra--nova")]
    [InlineData("construção")]             // accent
    [InlineData("obra/1")]
    [InlineData("obra.1")]
    public void Invalid_slugs(string slug) => BuildPortalSlug.Validate(slug).Should().NotBeNull();

    [Fact]
    public void Too_long_slug_is_refused()
        => BuildPortalSlug.Validate(new string('a', 61)).Should().NotBeNull();

    [Theory]
    [InlineData("login")]
    [InlineData("build")]
    [InlineData("agendar")]
    [InlineData("platform")]
    [InlineData("api")]
    [InlineData("portal")]
    [InlineData("obras")]
    [InlineData("tributos")]
    [InlineData("health")]
    public void Reserved_words_are_refused(string slug)
        => BuildPortalSlug.Validate(slug).Should().Be("Este endereço é reservado. Escolha outro.");

    [Fact]
    public void Normalize_lowercases_and_trims()
        => BuildPortalSlug.Normalize("  Minha-Obra ").Should().Be("minha-obra");

    [Fact]
    public void Company_rejects_an_invalid_slug_with_a_code()
    {
        var act = () => BuildPortalCompany.Create(Guid.NewGuid(), "admin", "Construtora", null, null, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("portal_slug_invalid");
    }
}

public class BuildPortalVisibilityTests
{
    [Fact]
    public void Defaults_show_progress_photos_updates_and_contact_but_nothing_financial()
    {
        var v = BuildPortalVisibility.Defaults;
        v.OverallProgress.Should().BeTrue();
        v.StageProgress.Should().BeTrue();
        v.Schedule.Should().BeTrue();
        v.Photos.Should().BeTrue();
        v.ClientUpdates.Should().BeTrue();
        v.Contact.Should().BeTrue();

        v.FinancialSummary.Should().BeFalse();
        v.FinancialCategories.Should().BeFalse();
        v.FinancialTransactions.Should().BeFalse();
        v.Receipts.Should().BeFalse();
        v.Documents.Should().BeFalse();
        v.DailyLogs.Should().BeFalse();
        v.Risks.Should().BeFalse();
        v.Team.Should().BeFalse();
        v.Model3d.Should().BeFalse();
    }

    [Fact]
    public void A_new_portal_starts_with_the_safe_defaults()
        => BuildProjectPortal.Create(Guid.NewGuid(), Guid.NewGuid()).Visibility.Should().Be(BuildPortalVisibility.Defaults);

    [Fact]
    public void Flags_missing_from_stored_json_take_their_safe_default()
    {
        var v = JsonSerializer.Deserialize<BuildPortalVisibility>("{\"financialSummary\":true}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        v.FinancialSummary.Should().BeTrue();
        v.OverallProgress.Should().BeTrue();
        v.Team.Should().BeFalse();
    }
}

public class BuildProjectPortalTests
{
    private static BuildProjectPortal Portal(BuildPortalAccess access = BuildPortalAccess.Public)
    {
        var p = BuildProjectPortal.Create(Guid.NewGuid(), Guid.NewGuid());
        p.Configure("minha-obra", access, "Minha obra", null, null, null, "#112233", null, null, null, null, null,
            BuildPortalVisibility.Defaults);
        return p;
    }

    [Fact]
    public void Password_portal_cannot_be_published_without_a_password()
    {
        var p = Portal(BuildPortalAccess.Password);
        p.Invoking(x => x.Publish(DateTime.UtcNow)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("portal_password_missing");
        p.SetPasswordHash("hash");
        p.Publish(DateTime.UtcNow);
        p.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Portal_without_slug_cannot_be_published()
    {
        var p = BuildProjectPortal.Create(Guid.NewGuid(), Guid.NewGuid());
        p.Invoking(x => x.Publish(DateTime.UtcNow)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("portal_slug_missing");
    }

    [Fact]
    public void Each_password_change_bumps_the_version_that_sessions_depend_on()
    {
        var p = Portal(BuildPortalAccess.Password);
        p.SetPasswordHash("h1");
        var session = BuildPortalSession.Open(p.TenantId, p.Id, new string('a', 64), p.PasswordVersion, DateTime.UtcNow, TimeSpan.FromHours(1));
        session.IsValid(DateTime.UtcNow, p.PasswordVersion).Should().BeTrue();

        p.SetPasswordHash("h2");
        session.IsValid(DateTime.UtcNow, p.PasswordVersion).Should().BeFalse();
    }

    [Fact]
    public void Session_expires_and_can_be_revoked()
    {
        var now = DateTime.UtcNow;
        var s = BuildPortalSession.Open(Guid.NewGuid(), Guid.NewGuid(), new string('b', 64), 1, now, TimeSpan.FromHours(12));
        s.IsValid(now.AddHours(11), 1).Should().BeTrue();
        s.IsValid(now.AddHours(13), 1).Should().BeFalse();
        s.Revoke(now);
        s.IsValid(now.AddMinutes(1), 1).Should().BeFalse();
    }

    [Fact]
    public void Wrong_passwords_lock_one_client_long_before_the_whole_portal()
    {
        // per client IP (cache) 20 → that IP waits 15 min; the portal-wide counter (database) only bounds a distributed attack
        BuildProjectPortal.MaxFailedAttemptsPerClient.Should().Be(20);
        BuildProjectPortal.MaxFailedAttemptsGlobal.Should().BeGreaterThanOrEqualTo(10 * BuildProjectPortal.MaxFailedAttemptsPerClient);
        BuildProjectPortal.LockMinutes.Should().Be(15);
    }

    [Fact]
    public void A_lock_in_the_past_does_not_lock()
    {
        var p = Portal(BuildPortalAccess.Password);
        p.IsLocked(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Removing_the_password_of_a_published_password_portal_is_refused()
    {
        var p = Portal(BuildPortalAccess.Password);
        p.SetPasswordHash("h");
        p.Publish(DateTime.UtcNow);
        p.Invoking(x => x.ClearPassword()).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("123456")]
    [InlineData("#12345g")]
    [InlineData("red")]
    public void Colors_must_be_hex(string color)
    {
        var p = BuildProjectPortal.Create(Guid.NewGuid(), Guid.NewGuid());
        p.Invoking(x => x.Configure("obra-x", BuildPortalAccess.Public, null, null, null, null, color, null,
            null, null, null, null, BuildPortalVisibility.Defaults)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Visible_daily_log_needs_a_client_summary_and_never_exposes_the_notes()
    {
        var log = BuildDailyLog.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 5), Guid.NewGuid(), "Nota interna");
        log.Invoking(l => l.SetClientVisibility(true, null)).Should().Throw<DomainException>();
        log.SetClientVisibility(true, "Concretagem da laje concluída.");
        log.ClientVisible.Should().BeTrue();
        log.ClientSummary.Should().Be("Concretagem da laje concluída.");
        log.Notes.Should().Be("Nota interna");
    }

    [Fact]
    public void Stage_progress_marks_actual_start_and_end()
    {
        var stage = BuildStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Fundação", 1);
        stage.UpdateProgress(30, new DateOnly(2026, 2, 1));
        stage.ActualStartDate.Should().Be(new DateOnly(2026, 2, 1));
        stage.Status.Should().Be(BuildStageStatus.InProgress);
        stage.UpdateProgress(100, new DateOnly(2026, 2, 10));
        stage.Status.Should().Be(BuildStageStatus.Completed);
        stage.ActualEndDate.Should().Be(new DateOnly(2026, 2, 10));
        stage.Invoking(s => s.SetWeight(0m)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Past_snapshots_are_immutable()
    {
        var snap = BuildProgressSnapshot.Take(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), 10m, 5m, null, null, "[]", "weighted-stages/1");
        snap.Invoking(s => s.Refresh(new DateOnly(2026, 1, 2), 20m, 10m, null, null, "[]", "weighted-stages/1"))
            .Should().Throw<DomainException>();
        snap.Refresh(new DateOnly(2026, 1, 1), 20m, 10m, null, null, "[]", "weighted-stages/1");
        snap.ActualPercent.Should().Be(10m);
    }

    [Fact]
    public void Project_site_validates_coordinates_and_area()
    {
        var p = BuildProject.Create(Guid.NewGuid(), Guid.NewGuid(), "Casa", "Cliente", BuildProjectType.House);
        p.TrackingMode.Should().Be(BuildTrackingMode.Standard);
        p.Invoking(x => x.SetSite(91m, 0m, null)).Should().Throw<DomainException>();
        p.Invoking(x => x.SetSite(0m, 181m, null)).Should().Throw<DomainException>();
        p.Invoking(x => x.SetSite(-23.5m, null, null)).Should().Throw<DomainException>();
        p.Invoking(x => x.SetSite(null, null, -1m)).Should().Throw<DomainException>();
        p.SetSite(-23.55m, -46.63m, 120m);
        p.HasSite.Should().BeTrue();
        p.ChooseTracking(BuildTrackingMode.ThreeD);
        p.TrackingMode.Should().Be(BuildTrackingMode.ThreeD);
    }
}
