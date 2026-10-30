using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Integrations.Weather;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// The public client portal (no login): <c>/api/public/build/{companySlug}/{projectSlug}/…</c>.
///
/// Security posture:
///   - The tenant comes only from the company slug; every query then names that tenant and the portal's
///     project explicitly. This is the one Build service allowed to bypass the global tenant filter.
///   - Unknown company, unknown project and unpublished portal answer the same 404 (no enumeration).
///   - A password portal needs a session: <see cref="PublicBuildPortal.SessionHeader"/> carries an opaque
///     token whose SHA-256 is matched; expired, revoked or opened before a password change → 401.
///   - Only sections enabled in the portal's visibility are built; disabled ones are absent from the JSON.
///   - Public DTOs only: no internal ids, no internal notes (only the client summary of a log marked
///     visible), no password data; photos only when both the photo and its log are client-visible, as
///     signed URLs (15 min) for keys of the portal's tenant.
/// </summary>
public sealed partial class BuildPublicPortalService
{
    private const int DefaultSessionHours = 12;
    private const int MaxTokenLength = 128;
    private const int MaxPasswordLength = 128;

    private readonly NexoDbContext _db;
    private readonly BuildProgressEngine _engine;
    private readonly IPasswordHasher _hasher;
    private readonly BuildPortalMedia _media;
    private readonly IWeatherProvider _weather;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly IConfiguration _config;
    private readonly BuildAuditTrail _audit;
    private readonly ILogger<BuildPublicPortalService> _logger;
    private readonly BuildChangeOrderEffects _effects;
    private readonly IStorageProvider _storage;

    public BuildPublicPortalService(NexoDbContext db, BuildProgressEngine engine, IPasswordHasher hasher,
        BuildPortalMedia media, IWeatherProvider weather, IIntegrationFeatureFlags flags, IConfiguration config,
        BuildAuditTrail audit, ILogger<BuildPublicPortalService> logger, BuildChangeOrderEffects effects,
        IStorageProvider storage)
    {
        _effects = effects;
        _storage = storage;
        _db = db;
        _engine = engine;
        _hasher = hasher;
        _media = media;
        _weather = weather;
        _flags = flags;
        _config = config;
        _audit = audit;
        _logger = logger;
    }

    private sealed record Ctx(BuildPortalCompany Company, BuildProjectPortal Portal, BuildProject Project)
    {
        public Guid TenantId => Portal.TenantId;
        public BuildPortalVisibility Visibility { get; } = Portal.Visibility;
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    public async Task<PublicBuildBootstrapDto> GetBootstrapAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        var vis = ctx.Visibility;
        var today = BusinessClock.Today();

        var handover = await HandoverOfAsync(ctx, ct);
        BuildProgressEngine.Computation? c = null;
        if (vis.OverallProgress || vis.StageProgress || vis.Schedule || vis.ForecastDelivery)
            c = await _engine.ComputeAsync(ctx.TenantId, ctx.Project.Id, today, bypassFilters: true, ct);

        return new PublicBuildBootstrapDto
        {
            Sections = EnabledSections(vis),
            Portal = new PublicBuildPortalDto(ctx.Portal.Title, ctx.Portal.WelcomeMessage, ctx.Portal.PrimaryColor,
                ctx.Portal.AccentColor, _media.BrandingUrl(ctx.Portal.LogoKey, ctx.TenantId) ?? CompanyLogo(ctx),
                _media.BrandingUrl(ctx.Portal.CoverKey, ctx.TenantId), ctx.Portal.Access.ToString()),
            Company = new PublicBuildCompanyDto(ctx.Company.DisplayName, CompanyLogo(ctx)),
            Project = new PublicBuildProjectDto(ctx.Project.Name, ctx.Project.Type.ToString(), ctx.Project.Status.ToString(),
                ctx.Project.StartDate, ctx.Project.ExpectedEndDate),
            Progress = vis.OverallProgress && c is not null
                ? new PublicBuildProgressSummaryDto
                {
                    AsOf = today,
                    ActualPercent = c.Result.ActualPercent,
                    PlannedPercent = c.Result.PlannedPercent,
                    ForecastDelivery = vis.ForecastDelivery ? c.Result.ForecastDelivery : null,
                    Methodology = c.Result.Methodology,
                }
                : null,
            Stages = vis.StageProgress && c is not null
                ? c.Result.Stages.Select(s => new PublicBuildStageDto(s.Name, s.Order, s.Status, s.ActualPercent)).ToList()
                : null,
            Schedule = vis.Schedule && c is not null
                ? new PublicBuildScheduleDto(ctx.Project.StartDate, ctx.Project.ExpectedEndDate,
                    c.Result.Stages.Select(s => new PublicBuildScheduleStageDto(s.Name, s.Order, s.PlannedStart, s.PlannedEnd)).ToList())
                : null,
            Weather = vis.Weather ? await WeatherAsync(ctx.Project, ct) : null,
            FinancialSummary = vis.FinancialSummary ? await FinancialAsync(ctx, today, ct) : null,
            Contact = vis.Contact ? Contact(ctx) : null,
            LatestUpdates = vis.ClientUpdates ? await LatestUpdatesAsync(ctx, ct) : null,
            Phase = handover?.IsDelivered == true ? PublicBuildPortal.PhaseDelivered : PublicBuildPortal.PhaseConstruction,
            Property = vis.PostConstruction && handover?.IsDelivered == true ? await PropertyAsync(ctx, handover, ct) : null,
        };
    }

    // ── Session (password portals) ────────────────────────────────────────────

    public async Task<PublicBuildSessionDto> OpenSessionAsync(string companySlug, string projectSlug, string? password,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: true, ct);
        var portal = ctx.Portal;
        if (portal.Access != BuildPortalAccess.Password || !portal.HasPassword)
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodePasswordNotNeeded,
                "Este portal não pede senha.");

        var now = DateTime.UtcNow;
        if (portal.IsLocked(now))
            throw Locked(portal, now);

        if (string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength || !_hasher.Verify(password, portal.PasswordHash!))
        {
            portal.RegisterFailedAttempt(now);
            if (portal.IsLocked(now))
                _audit.RecordPortalVisitor(AuditActions.BuildPortalLocked, portal.TenantId, portal.Id,
                    $"Portal do cliente bloqueado por {BuildProjectPortal.LockMinutes} min após senhas erradas seguidas.");
            await _db.SaveChangesAsync(ct);
            throw new BuildPortalAccessException(401, PublicBuildPortal.CodePasswordInvalid, "Senha incorreta.", Teaser(ctx));
        }

        portal.RegisterSuccessfulAttempt();
        var token = NewToken();
        var hours = Math.Clamp(_config.GetValue("Build:Portal:SessionHours", DefaultSessionHours), 1, 24 * 30);
        var session = BuildPortalSession.Open(portal.TenantId, portal.Id, HashToken(token), portal.PasswordVersion, now,
            TimeSpan.FromHours(hours));
        _db.BldPortalSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return new PublicBuildSessionDto(token, new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero));
    }

    public async Task CloseSessionAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        if (!IsPlausibleToken(token)) return;
        var hash = HashToken(token!);
        var session = await _db.BldPortalSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == ctx.TenantId && s.PortalId == ctx.Portal.Id && s.TokenHash == hash, ct);
        if (session is null) return;
        session.Revoke(DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    public async Task<PublicBuildTimelineDto> GetTimelineAsync(string companySlug, string projectSlug, string? token,
        DateTimeOffset? before, int? take, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        var vis = ctx.Visibility;
        var n = Math.Clamp(take ?? 20, 1, 50);
        DateTime? cutoff = before?.UtcDateTime;
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var items = new List<PublicBuildTimelineItemDto>();

        if (vis.ClientUpdates)
        {
            var updates = await _db.BldClientUpdates.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.TenantId == tid && u.ProjectId == pid && u.Status == BuildClientUpdateStatus.Published
                         && u.PublishedAt != null && (cutoff == null || u.PublishedAt < cutoff))
                .OrderByDescending(u => u.PublishedAt).Take(n)
                .Select(u => new { u.Kind, u.Title, u.Body, u.PublishedAt })
                .ToListAsync(ct);
            items.AddRange(updates.Select(u => new PublicBuildTimelineItemDto
            {
                Type = "update", OccurredAt = Utc(u.PublishedAt!.Value), Kind = u.Kind.ToString(), Title = u.Title, Text = u.Body,
            }));
        }

        if (vis.DailyLogs)
        {
            var lastDay = cutoff is { } cu ? BusinessClock.Today(cu) : (DateOnly?)null;
            var logs = await _db.BldDailyLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == tid && l.ProjectId == pid && l.ClientVisible && l.ClientSummary != null
                         && (lastDay == null || l.Date <= lastDay))
                .OrderByDescending(l => l.Date).Take(n + 1)
                .Select(l => new
                {
                    l.Date,
                    l.ClientSummary,
                    Photos = _db.BldDailyLogPhotos.IgnoreQueryFilters()
                        .Count(ph => ph.TenantId == tid && ph.DailyLogId == l.Id && ph.ClientVisible),
                })
                .ToListAsync(ct);
            items.AddRange(logs
                .Select(l => new PublicBuildTimelineItemDto
                {
                    Type = "daily_log", OccurredAt = Utc(BusinessClock.NoonUtc(l.Date)), Date = l.Date, Text = l.ClientSummary,
                    PhotoCount = vis.Photos ? l.Photos : null,
                })
                .Where(i => cutoff == null || i.OccurredAt.UtcDateTime < cutoff));
        }

        if (vis.StageProgress)
        {
            var events = await (
                    from e in _db.BldStageProgressEvents.IgnoreQueryFilters()
                    join s in _db.BldStages.IgnoreQueryFilters() on e.StageId equals s.Id
                    where e.TenantId == tid && s.TenantId == tid && e.ProjectId == pid && s.ProjectId == pid
                       && e.Percent != e.PreviousPercent
                       && (cutoff == null || e.CreatedAt < cutoff)
                    orderby e.CreatedAt descending
                    select new { s.Name, e.Percent, e.PreviousPercent, e.EffectiveDate, e.CreatedAt })
                .Take(n).AsNoTracking().ToListAsync(ct);
            items.AddRange(events.Select(e => new PublicBuildTimelineItemDto
            {
                Type = e.Percent == 100 ? "stage_completed" : "stage_progress",
                OccurredAt = Utc(e.CreatedAt), Date = e.EffectiveDate, StageName = e.Name,
                Percent = e.Percent, PreviousPercent = e.PreviousPercent,
            }));
        }

        if (vis.Photos)
        {
            var photos = await ClientPhotos(tid, pid)
                .Where(x => cutoff == null || x.CreatedAt < cutoff)
                .OrderByDescending(x => x.CreatedAt).Take(n)
                .ToListAsync(ct);
            items.AddRange(photos.Select(p => new PublicBuildTimelineItemDto
            {
                Type = "photo", OccurredAt = Utc(p.CreatedAt), Date = p.Date, Caption = p.Caption,
                PhotoUrl = _media.SignedUrl(p.StorageKey, tid),
            }));
        }

        var page = items.OrderByDescending(i => i.OccurredAt).Take(n).ToList();
        return new PublicBuildTimelineDto(page, page.Count == n ? page[^1].OccurredAt : null);
    }

    // ── Photos ────────────────────────────────────────────────────────────────

    public async Task<PublicBuildPhotosDto> GetPhotosAsync(string companySlug, string projectSlug, string? token,
        int? skip, int? take, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Photos) throw BuildPortalAccessException.SectionDisabled();
        var s = Math.Max(0, skip ?? 0);
        var n = Math.Clamp(take ?? 24, 1, 50);
        var query = ClientPhotos(ctx.TenantId, ctx.Project.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Skip(s).Take(n).ToListAsync(ct);
        var expires = DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl);
        return new PublicBuildPhotosDto(
            rows.Select(r => new PublicBuildPhotoDto(_media.SignedUrl(r.StorageKey, ctx.TenantId), r.Caption, r.Date, expires)).ToList(),
            total, s, n);
    }

    // ── Progress curves ───────────────────────────────────────────────────────

    public async Task<PublicBuildProgressDto> GetProgressAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        var vis = ctx.Visibility;
        if (!vis.OverallProgress) throw BuildPortalAccessException.SectionDisabled();
        var today = BusinessClock.Today();
        var c = await _engine.ComputeAsync(ctx.TenantId, ctx.Project.Id, today, bypassFilters: true, ct)
            ?? throw BuildPortalAccessException.NotFound();
        var curves = await _engine.CurvesAsync(c, today, bypassFilters: true, ct);
        return new PublicBuildProgressDto
        {
            AsOf = today,
            ActualPercent = c.Result.ActualPercent,
            PlannedPercent = c.Result.PlannedPercent,
            ForecastDelivery = vis.ForecastDelivery ? c.Result.ForecastDelivery : null,
            Methodology = c.Result.Methodology,
            Curves = new PublicBuildCurvesDto
            {
                Actual = curves.Actual,
                Planned = curves.Planned,
                Forecast = vis.ForecastDelivery ? curves.Forecast : null,
            },
        };
    }

    // ── Change orders (v1.1) ──────────────────────────────────────────────────

    private static readonly BuildChangeOrderStatus[] ClientChangeOrderStatuses =
        [BuildChangeOrderStatus.SentToClient, BuildChangeOrderStatus.Approved, BuildChangeOrderStatus.Rejected];

    /// <summary>Change orders sent to the client (pending) and decided ones; drafts and cancelled never appear.</summary>
    public async Task<PublicBuildChangeOrdersDto> GetChangeOrdersAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.ChangeOrders) throw BuildPortalAccessException.SectionDisabled();
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var list = await _db.BldChangeOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tid && c.ProjectId == pid && ClientChangeOrderStatuses.Contains(c.Status))
            .OrderByDescending(c => c.Number)
            .ToListAsync(ct);
        return new PublicBuildChangeOrdersDto(await MapChangeOrdersAsync(ctx, list, ct));
    }

    /// <summary>
    /// The client approves or rejects a pending change order. Only on a password portal with a valid session (a
    /// public portal → 403 portal_decision_requires_password); a decided order → 409. On approval the contract
    /// value, the affected stage's planned end and (with a baseline) a schedule revision are updated.
    /// </summary>
    public async Task<PublicBuildChangeOrderDto> DecideChangeOrderAsync(string companySlug, string projectSlug, string? token,
        Guid publicId, PublicBuildChangeOrderDecisionRequest? request, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        if (!ctx.Visibility.ChangeOrders) throw BuildPortalAccessException.SectionDisabled();
        EnsureDecisionPortal(ctx);
        await EnsureAccessAsync(ctx, token, ct);

        var decision = request?.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("approve" or "reject"))
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidDecision, "Informe a decisão: approve ou reject.");
        var signer = request!.SignerName?.Trim();
        if (string.IsNullOrEmpty(signer))
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeSignerRequired, "Informe o nome de quem está decidindo.");

        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var co = await _db.BldChangeOrders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tid && c.ProjectId == pid && c.PublicId == publicId
                                   && ClientChangeOrderStatuses.Contains(c.Status), ct)
            ?? throw ItemNotFound();
        if (co.IsDecided)
            throw new BuildPortalAccessException(409, PublicBuildPortal.CodeAlreadyDecided, "Este aditivo já foi decidido.");

        var now = DateTime.UtcNow;
        var scheduleChanged = false;
        if (decision == "approve")
        {
            co.Approve(BuildDecisionChannel.Portal, BuildChangeOrder.PortalDecider, null, request.Comment, signer, now);
            scheduleChanged = await _effects.ApplyApprovalAsync(co, Guid.Empty, $"{BuildChangeOrder.PortalDecider}: {signer}", ct);
        }
        else
        {
            co.Reject(BuildDecisionChannel.Portal, BuildChangeOrder.PortalDecider, null, request.Comment, signer, now);
        }
        _audit.RecordPortalClient(decision == "approve" ? AuditActions.BuildChangeOrderApproved : AuditActions.BuildChangeOrderRejected,
            tid, "ChangeOrder", co.Id,
            $"Aditivo #{co.Number} {(decision == "approve" ? "aprovado" : "recusado")} pelo cliente no portal.", signer,
            new { co.CostImpact, co.ScheduleImpactDays, co.AppliedToBudget });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BuildPortalAccessException(409, PublicBuildPortal.CodeAlreadyDecided, "Este aditivo já foi decidido.");
        }
        if (scheduleChanged) await _effects.RefreshSnapshotAsync(tid, pid, ct);
        return (await MapChangeOrdersAsync(ctx, [co], ct))[0];
    }

    private async Task<IReadOnlyList<PublicBuildChangeOrderDto>> MapChangeOrdersAsync(Ctx ctx, IReadOnlyList<BuildChangeOrder> list,
        CancellationToken ct)
    {
        var tid = ctx.TenantId;
        var ids = list.Select(c => c.Id).ToList();
        var stageIds = list.Where(c => c.StageId.HasValue).Select(c => c.StageId!.Value).Distinct().ToList();
        var items = (await _db.BldChangeOrderItems.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.TenantId == tid && ids.Contains(i.ChangeOrderId)).ToListAsync(ct))
            .ToLookup(i => i.ChangeOrderId);
        var stages = await _db.BldStages.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tid && s.ProjectId == ctx.Project.Id && stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var canDecide = ctx.Portal.Access == BuildPortalAccess.Password;
        return list.Select(c => new PublicBuildChangeOrderDto
        {
            Id = c.PublicId,
            Number = c.Number,
            Title = c.Title,
            Description = c.Description,
            Reason = c.Reason.ToString(),
            CostImpact = c.CostImpact,
            ScheduleImpactDays = c.ScheduleImpactDays,
            StageName = c.StageId is { } sid ? stages.GetValueOrDefault(sid) : null,
            Status = c.Status == BuildChangeOrderStatus.SentToClient ? "Pending" : c.Status.ToString(),
            CanDecide = canDecide && c.Status == BuildChangeOrderStatus.SentToClient,
            SentAt = c.SentAt is { } sent ? Utc(sent) : null,
            DecidedAt = c.DecidedAt is { } d ? Utc(d) : null,
            DecisionChannel = c.DecisionChannel?.ToString(),
            ClientComment = c.ClientComment,
            SignerName = c.SignerName,
            Items = items[c.Id].OrderBy(i => i.Order)
                .Select(i => new PublicBuildChangeOrderItemDto(i.Name, i.Kind.ToString(), i.Quantity, i.Unit, i.UnitCost, i.TotalCost))
                .ToList(),
        }).ToList();
    }

    // ── Selections (v1.1) ─────────────────────────────────────────────────────

    public async Task<PublicBuildSelectionsDto> GetSelectionsAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Selections) throw BuildPortalAccessException.SectionDisabled();
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var list = await ClientSelections(tid, pid).AsNoTracking()
            .OrderBy(s => s.Deadline == null).ThenBy(s => s.Deadline).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        return new PublicBuildSelectionsDto(await MapSelectionsAsync(ctx, list, ct));
    }

    /// <summary>
    /// The client's choice of an option: password portal with a valid session (else 403), selection still Open
    /// (else 409 selection_closed) and not past its deadline (422 selection_deadline_passed).
    /// </summary>
    public async Task<PublicBuildSelectionDto> ChooseSelectionAsync(string companySlug, string projectSlug, string? token,
        Guid publicId, PublicBuildSelectionChoiceRequest? request, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        if (!ctx.Visibility.Selections) throw BuildPortalAccessException.SectionDisabled();
        EnsureDecisionPortal(ctx);
        await EnsureAccessAsync(ctx, token, ct);

        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var selection = await ClientSelections(tid, pid).FirstOrDefaultAsync(s => s.PublicId == publicId, ct)
            ?? throw ItemNotFound();
        var option = request?.OptionId is { } oid
            ? await _db.BldSelectionOptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.TenantId == tid && o.SelectionId == selection.Id && o.PublicId == oid, ct)
            : null;
        if (option is null)
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidOption, "Escolha uma das opções desta seleção.");
        if (selection.Status != BuildSelectionStatus.Open)
            throw new BuildPortalAccessException(409, PublicBuildPortal.CodeSelectionClosed, "Esta escolha já foi feita ou encerrada.");
        var today = BusinessClock.Today();
        if (selection.IsDeadlinePassed(today))
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeSelectionDeadlinePassed,
                $"O prazo desta escolha terminou em {selection.Deadline:dd/MM/yyyy}.");

        selection.Choose(option.Id, request!.Comment, today, DateTime.UtcNow, BuildDecisionChannel.Portal);
        _audit.RecordPortalClient(AuditActions.BuildSelectionChosen, tid, "Selection", selection.Id,
            $"Cliente escolheu {option.Name} em {selection.Title} pelo portal.", null, new { optionId = option.Id, option.PriceDelta });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BuildPortalAccessException(409, PublicBuildPortal.CodeSelectionClosed, "Esta escolha já foi feita ou encerrada.");
        }
        return (await MapSelectionsAsync(ctx, [selection], ct))[0];
    }

    private IQueryable<BuildSelection> ClientSelections(Guid tid, Guid pid)
        => _db.BldSelections.IgnoreQueryFilters()
            .Where(s => s.TenantId == tid && s.ProjectId == pid && s.ClientVisible && s.Status != BuildSelectionStatus.Cancelled);

    private async Task<IReadOnlyList<PublicBuildSelectionDto>> MapSelectionsAsync(Ctx ctx, IReadOnlyList<BuildSelection> list,
        CancellationToken ct)
    {
        var tid = ctx.TenantId;
        var ids = list.Select(s => s.Id).ToList();
        var stageIds = list.Where(s => s.StageId.HasValue).Select(s => s.StageId!.Value).Distinct().ToList();
        var options = (await _db.BldSelectionOptions.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.TenantId == tid && ids.Contains(o.SelectionId)).ToListAsync(ct))
            .ToLookup(o => o.SelectionId);
        var stages = await _db.BldStages.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tid && s.ProjectId == ctx.Project.Id && stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var today = BusinessClock.Today();
        var password = ctx.Portal.Access == BuildPortalAccess.Password;
        var expires = DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl);
        return list.Select(s =>
        {
            var opts = options[s.Id].OrderBy(o => o.Order).ThenBy(o => o.CreatedAt).ToList();
            var chosen = opts.FirstOrDefault(o => o.Id == s.ChosenOptionId);
            return new PublicBuildSelectionDto
            {
                Id = s.PublicId,
                Title = s.Title,
                Description = s.Description,
                StageName = s.StageId is { } sid ? stages.GetValueOrDefault(sid) : null,
                Deadline = s.Deadline,
                DeadlinePassed = s.IsDeadlinePassed(today),
                Status = s.Status.ToString(),
                CanChoose = password && s.Status == BuildSelectionStatus.Open && !s.IsDeadlinePassed(today),
                ChosenOptionId = chosen?.PublicId,
                ClientComment = s.ClientComment,
                DecidedAt = s.DecidedAt is { } d ? Utc(d) : null,
                Options = opts.Select(o => new PublicBuildSelectionOptionDto
                {
                    Id = o.PublicId,
                    Name = o.Name,
                    Description = o.Description,
                    PriceDelta = o.PriceDelta,
                    IsStandard = o.IsStandard,
                    // only selections the company marked visible reach here; the image stays private, signed per request
                    ImageUrl = _media.SignedUrl(o.ImageKey, tid),
                    LeadTimeDays = o.LeadTimeDays,
                }).ToList(),
                ImageUrlsExpireAt = opts.Any(o => o.ImageKey is not null) ? expires : null,
            };
        }).ToList();
    }

    // ── Measurements (v1.1) ───────────────────────────────────────────────────

    /// <summary>Approved measurements (flag measurements); values only when the financial summary is enabled too.</summary>
    public async Task<PublicBuildMeasurementsDto> GetMeasurementsAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Measurements) throw BuildPortalAccessException.SectionDisabled();
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var money = ctx.Visibility.FinancialSummary;
        var list = await _db.BldMeasurements.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tid && m.ProjectId == pid && m.Status == BuildMeasurementStatus.Approved)
            .OrderByDescending(m => m.Number)
            .ToListAsync(ct);
        var ids = list.Select(m => m.Id).ToList();
        var items = (await _db.BldMeasurementItems.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.TenantId == tid && ids.Contains(i.MeasurementId)).ToListAsync(ct))
            .ToLookup(i => i.MeasurementId);
        var stages = await _db.BldStages.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tid && s.ProjectId == pid)
            .ToDictionaryAsync(s => s.Id, s => new { s.Name, s.Order }, ct);
        return new PublicBuildMeasurementsDto(list.Select(m =>
        {
            var lines = items[m.Id].Where(i => stages.ContainsKey(i.StageId))
                .OrderBy(i => stages[i.StageId].Order).ToList();
            return new PublicBuildMeasurementDto
            {
                Number = m.Number,
                PeriodStart = m.PeriodStart,
                PeriodEnd = m.PeriodEnd,
                ApprovedAt = m.ApprovedAt is { } a ? Utc(a) : null,
                TotalAmount = money ? lines.Sum(i => i.Amount ?? 0m) : null,
                Items = lines.Select(i => new PublicBuildMeasurementItemDto
                {
                    StageName = stages[i.StageId].Name,
                    PreviousPercent = i.PreviousPercent,
                    MeasuredPercent = i.MeasuredPercent,
                    Amount = money ? i.Amount : null,
                }).ToList(),
            };
        }).ToList());
    }

    private static void EnsureDecisionPortal(Ctx ctx)
    {
        if (ctx.Portal.Access != BuildPortalAccess.Password)
            throw new BuildPortalAccessException(403, PublicBuildPortal.CodeDecisionRequiresPassword,
                "Decisões pelo portal exigem um portal com senha. Fale com a construtora.");
    }

    private static BuildPortalAccessException ItemNotFound()
        => new(404, PublicBuildPortal.CodeItemNotFound, "Item não encontrado neste portal.");

    // ── Resolution and access ─────────────────────────────────────────────────

    private async Task<Ctx> ResolveAsync(string companySlug, string projectSlug, bool tracking, CancellationToken ct)
    {
        var cs = BuildPortalSlug.Normalize(companySlug);
        var ps = BuildPortalSlug.Normalize(projectSlug);
        if (!BuildPortalSlug.IsValid(cs) || !BuildPortalSlug.IsValid(ps)) throw BuildPortalAccessException.NotFound();

        var company = await _db.BldPortalCompanies.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == cs, ct)
            ?? throw BuildPortalAccessException.NotFound();
        var portals = _db.BldProjectPortals.IgnoreQueryFilters();
        var portal = await (tracking ? portals : portals.AsNoTracking())
            .FirstOrDefaultAsync(p => p.TenantId == company.TenantId && p.Slug == ps && p.IsPublished, ct)
            ?? throw BuildPortalAccessException.NotFound();
        var project = await _db.BldProjects.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == company.TenantId && p.Id == portal.ProjectId, ct)
            ?? throw BuildPortalAccessException.NotFound();
        return new Ctx(company, portal, project);
    }

    private async Task EnsureAccessAsync(Ctx ctx, string? token, CancellationToken ct)
    {
        if (ctx.Portal.Access != BuildPortalAccess.Password) return;
        if (string.IsNullOrWhiteSpace(token))
            throw new BuildPortalAccessException(401, PublicBuildPortal.CodePasswordRequired,
                "Este portal pede senha.", Teaser(ctx));
        if (!IsPlausibleToken(token))
            throw new BuildPortalAccessException(401, PublicBuildPortal.CodeSessionExpired,
                "Sua sessão terminou. Digite a senha de novo.", Teaser(ctx));

        var hash = HashToken(token);
        var session = await _db.BldPortalSessions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == ctx.TenantId && s.PortalId == ctx.Portal.Id && s.TokenHash == hash, ct);
        if (session is null || !session.IsValid(DateTime.UtcNow, ctx.Portal.PasswordVersion))
            throw new BuildPortalAccessException(401, PublicBuildPortal.CodeSessionExpired,
                "Sua sessão terminou. Digite a senha de novo.", Teaser(ctx));
    }

    private PublicBuildPortalTeaserDto Teaser(Ctx ctx)
        => new(ctx.Company.DisplayName, _media.BrandingUrl(ctx.Portal.LogoKey, ctx.TenantId) ?? CompanyLogo(ctx), ctx.Portal.Title);

    private static BuildPortalAccessException Locked(BuildProjectPortal portal, DateTime now)
        => new(429, PublicBuildPortal.CodeLocked,
            "Muitas tentativas de senha. Tente de novo em alguns minutos.",
            retryAfterSeconds: (int)Math.Ceiling(((portal.LockedUntil ?? now) - now).TotalSeconds));

    // ── Sections ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> EnabledSections(BuildPortalVisibility v)
    {
        var list = new List<string>();
        if (v.OverallProgress) list.Add("overallProgress");
        if (v.StageProgress) list.Add("stageProgress");
        if (v.Schedule) list.Add("schedule");
        if (v.ForecastDelivery) list.Add("forecastDelivery");
        if (v.DailyLogs) list.Add("dailyLogs");
        if (v.Photos) list.Add("photos");
        if (v.Weather) list.Add("weather");
        if (v.FinancialSummary) list.Add("financialSummary");
        if (v.ClientUpdates) list.Add("clientUpdates");
        if (v.Contact) list.Add("contact");
        if (v.ChangeOrders) list.Add("changeOrders");
        if (v.Selections) list.Add("selections");
        if (v.Measurements) list.Add("measurements");
        if (v.PostConstruction) list.Add("postConstruction");
        return list;
    }

    private string? CompanyLogo(Ctx ctx) => _media.BrandingUrl(ctx.Company.LogoKey, ctx.TenantId);

    private async Task<PublicBuildWeatherDto?> WeatherAsync(BuildProject project, CancellationToken ct)
    {
        if (!_flags.WeatherEnabled || !project.HasSite) return null;
        try
        {
            var w = await _weather.GetCurrentAsync((double)project.Latitude!.Value, (double)project.Longitude!.Value, ct);
            return w is null ? null : new PublicBuildWeatherDto(w.Date, w.TemperatureMax, w.TemperatureMin, w.PrecipitationMm,
                w.Description, "Open-Meteo", "Previsão do tempo para o local da obra — informativa, pode mudar ao longo do dia.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Weather for the client portal unavailable.");
            return null;
        }
    }

    private async Task<PublicBuildFinancialSummaryDto> FinancialAsync(Ctx ctx, DateOnly today, CancellationToken ct)
    {
        var pid = ctx.Project.Id;
        var realized = await _db.IntMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == ctx.TenantId && m.ContextType == FinancialContextType.Obra && m.ContextId == pid
                     && m.Status == MovementStatus.Confirmed && m.Direction == MovementDirection.Out)
            .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        return new PublicBuildFinancialSummaryDto(ctx.Project.BudgetApproved, realized, "BRL", today);
    }

    private static PublicBuildContactDto? Contact(Ctx ctx)
    {
        var p = ctx.Portal;
        var dto = new PublicBuildContactDto(p.ContactName ?? ctx.Company.DisplayName, p.ContactPhone ?? ctx.Company.ContactPhone,
            p.ContactEmail ?? ctx.Company.ContactEmail, p.ContactWhatsApp);
        return dto.Phone is null && dto.Email is null && dto.WhatsApp is null && p.ContactName is null ? null : dto;
    }

    private async Task<IReadOnlyList<PublicBuildUpdateDto>> LatestUpdatesAsync(Ctx ctx, CancellationToken ct)
    {
        var pid = ctx.Project.Id;
        var rows = await _db.BldClientUpdates.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == ctx.TenantId && u.ProjectId == pid && u.Status == BuildClientUpdateStatus.Published
                     && u.PublishedAt != null)
            .OrderByDescending(u => u.PublishedAt).Take(5)
            .Select(u => new { u.Kind, u.Title, u.Body, u.PublishedAt })
            .ToListAsync(ct);
        return rows.Select(u => new PublicBuildUpdateDto(u.Kind.ToString(), u.Title, u.Body, Utc(u.PublishedAt!.Value))).ToList();
    }

    private sealed class PhotoRow
    {
        public string StorageKey { get; init; } = string.Empty;
        public string? Caption { get; init; }
        public DateOnly Date { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>Client-visible photos of client-visible logs of the project (explicit tenant on both tables).</summary>
    private IQueryable<PhotoRow> ClientPhotos(Guid tid, Guid pid)
        => from ph in _db.BldDailyLogPhotos.IgnoreQueryFilters().AsNoTracking()
           join l in _db.BldDailyLogs.IgnoreQueryFilters().AsNoTracking() on ph.DailyLogId equals l.Id
           where ph.TenantId == tid && l.TenantId == tid && l.ProjectId == pid && ph.ClientVisible && l.ClientVisible
           select new PhotoRow { StorageKey = ph.StorageKey, Caption = ph.Caption, Date = l.Date, CreatedAt = ph.CreatedAt };

    // ── Tokens ────────────────────────────────────────────────────────────────

    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static bool IsPlausibleToken(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxTokenLength;

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc), TimeSpan.Zero);
}
