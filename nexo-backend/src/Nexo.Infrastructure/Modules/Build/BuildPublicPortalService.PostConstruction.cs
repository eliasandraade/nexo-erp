using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Client portal v1.2 — "Meu Imóvel" (flag postConstruction): handover, warranty and technical assistance.
/// Reads follow the portal's access rules; every write (accept the handover, open a request, add a photo, rate)
/// requires a password portal with a valid session (public portal → 403 portal_decision_requires_password).
/// Requests are always looked up by public id AND the portal's tenant and project (IDOR-safe). Internal notes
/// (handover notes, walkthrough comments, triage notes) and the request's contact data never leave this file.
/// </summary>
public sealed partial class BuildPublicPortalService
{
    /// <summary>Open requests opened through the portal, per project — beyond it the client is asked to call the company.</summary>
    public const int MaxOpenPortalRequests = 20;

    private const string WarrantyNotice =
        "Prazos de referência informados pela construtora. Consulte o termo de garantia do seu contrato.";

    private Task<BuildHandover?> HandoverOfAsync(Ctx ctx, CancellationToken ct)
        => _db.BldHandovers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(h => h.TenantId == ctx.TenantId && h.ProjectId == ctx.Project.Id, ct);

    private async Task<PublicBuildPropertyDto> PropertyAsync(Ctx ctx, BuildHandover handover, CancellationToken ct)
    {
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var open = await _db.BldAssistanceRequests.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.TenantId == tid && a.ProjectId == pid && a.Status != BuildAssistanceStatus.Resolved
                          && a.Status != BuildAssistanceStatus.Closed && a.Status != BuildAssistanceStatus.Rejected, ct);
        return new PublicBuildPropertyDto
        {
            HandoverDate = handover.DeliveredAt is { } d ? BusinessClock.Today(d) : null,
            HandoverAccepted = handover.IsAccepted,
            WarrantyTerms = await WarrantyTermsAsync(ctx, ct),
            OpenAssistanceCount = open,
        };
    }

    private async Task<IReadOnlyList<PublicBuildWarrantyTermDto>> WarrantyTermsAsync(Ctx ctx, CancellationToken ct)
    {
        var today = BusinessClock.Today();
        var terms = await _db.BldWarrantyTerms.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == ctx.TenantId && t.ProjectId == ctx.Project.Id && t.StartsAt != null)
            .OrderByDescending(t => t.Months).ThenBy(t => t.System)
            .ToListAsync(ct);
        return terms.Select(t => new PublicBuildWarrantyTermDto(t.System, t.Months, t.StartsAt!.Value, t.ExpiresAt!.Value,
            t.IsActive(today), t.Description)).ToList();
    }

    private async Task<Ctx> PostConstructionReadAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.PostConstruction) throw BuildPortalAccessException.SectionDisabled();
        return ctx;
    }

    private async Task<Ctx> PostConstructionWriteAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        if (!ctx.Visibility.PostConstruction) throw BuildPortalAccessException.SectionDisabled();
        EnsureDecisionPortal(ctx);
        await EnsureAccessAsync(ctx, token, ct);
        return ctx;
    }

    // ── Handover ──────────────────────────────────────────────────────────────

    /// <summary>The handover (status, dates, documents as signed URLs, walkthrough summary); 404 while none is scheduled.</summary>
    public async Task<PublicBuildHandoverDto> GetHandoverAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await PostConstructionReadAsync(companySlug, projectSlug, token, ct);
        var handover = await HandoverOfAsync(ctx, ct) ?? throw ItemNotFound();
        return await MapHandoverAsync(ctx, handover, ct);
    }

    /// <summary>The client accepts the handover (signer name typed in the portal). Only once the walkthrough is done or the
    /// property delivered (422 handover_not_ready); accepting again → 409 handover_already_accepted.</summary>
    public async Task<PublicBuildHandoverDto> AcceptHandoverAsync(string companySlug, string projectSlug, string? token,
        PublicBuildHandoverAcceptRequest? request, CancellationToken ct)
    {
        var ctx = await PostConstructionWriteAsync(companySlug, projectSlug, token, ct);
        var signer = request?.SignerName?.Trim();
        if (string.IsNullOrEmpty(signer) || signer.Length > 200)
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeSignerRequired, "Informe o nome de quem está assinando o aceite.");
        if (request!.Comment is { Length: > 2000 })
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidDecision, "O comentário pode ter até 2000 caracteres.");

        var tid = ctx.TenantId;
        var handover = await _db.BldHandovers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.TenantId == tid && h.ProjectId == ctx.Project.Id, ct) ?? throw ItemNotFound();
        if (handover.IsAccepted) throw AlreadyAccepted();
        if (!handover.CanBeAccepted)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeHandoverNotReady,
                "O aceite fica disponível depois da vistoria de entrega.");

        handover.Accept(signer, request.Comment, BuildDecisionChannel.Portal, DateTime.UtcNow);
        _audit.RecordPortalClient(AuditActions.BuildHandoverAccepted, tid, "Handover", handover.Id,
            "Cliente aceitou a entrega pelo portal.", signer, new { channel = "Portal" });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AlreadyAccepted();
        }
        return await MapHandoverAsync(ctx, handover, ct);
    }

    private static BuildPortalAccessException AlreadyAccepted()
        => new(409, PublicBuildPortal.CodeHandoverAlreadyAccepted, "A entrega já foi aceita.");

    private async Task<PublicBuildHandoverDto> MapHandoverAsync(Ctx ctx, BuildHandover h, CancellationToken ct)
    {
        var tid = ctx.TenantId;
        PublicBuildWalkthroughDto? walkthrough = null;
        if (h.WalkthroughInspectionId is { } wid)
        {
            var inspection = await _db.BldInspections.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(i => i.TenantId == tid && i.ProjectId == ctx.Project.Id && i.Id == wid
                                       && i.Kind == BuildInspectionKind.ClientWalkthrough
                                       && i.Status != BuildInspectionStatus.Cancelled, ct);
            if (inspection is not null)
            {
                var items = await _db.BldInspectionItems.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tid && x.InspectionId == wid).OrderBy(x => x.Order)
                    .Select(x => new { x.Text, x.Result })
                    .ToListAsync(ct);
                walkthrough = new PublicBuildWalkthroughDto
                {
                    Status = inspection.Status.ToString(),
                    ScheduledFor = inspection.ScheduledFor,
                    CompletedAt = inspection.CompletedAt is { } c ? Utc(c) : null,
                    TotalItems = items.Count,
                    OkItems = items.Count(x => x.Result == BuildInspectionItemResult.Ok),
                    PendingItems = items.Count(x => x.Result == BuildInspectionItemResult.NotOk),
                    // only the item and its result: comments, photos, inspector and notes are internal
                    Items = items.Select(x => new PublicBuildWalkthroughItemDto(x.Text, x.Result.ToString())).ToList(),
                };
            }
        }
        var documents = h.Documents.Select(d => new PublicBuildHandoverDocumentDto(d.Name, _media.SignedUrl(d.FileKey, tid))).ToList();
        var password = ctx.Portal.Access == BuildPortalAccess.Password;
        return new PublicBuildHandoverDto
        {
            Status = h.Status.ToString(),
            ScheduledFor = h.ScheduledFor,
            DeliveredAt = h.DeliveredAt is { } d ? Utc(d) : null,
            KeysDelivered = h.KeysDelivered,
            Documents = documents,
            DocumentUrlsExpireAt = documents.Any(x => x.Url is not null) ? DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl) : null,
            Walkthrough = walkthrough,
            AcceptedAt = h.AcceptedAt is { } a ? Utc(a) : null,
            AcceptedByName = h.AcceptedByName,
            AcceptanceChannel = h.AcceptanceChannel?.ToString(),
            AcceptanceComment = h.AcceptanceComment,
            CanAccept = password && h.CanBeAccepted,
        };
    }

    // ── Warranty ──────────────────────────────────────────────────────────────

    /// <summary>The project's warranty terms that already started (on the delivery), with expiry and whether active today.</summary>
    public async Task<PublicBuildWarrantyDto> GetWarrantyAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await PostConstructionReadAsync(companySlug, projectSlug, token, ct);
        return new PublicBuildWarrantyDto(await WarrantyTermsAsync(ctx, ct), WarrantyNotice);
    }

    // ── Assistance ────────────────────────────────────────────────────────────

    /// <summary>The project's requests (opened by the client or by the company): status and timeline, never internal notes.</summary>
    public async Task<PublicBuildAssistanceListDto> GetAssistanceAsync(string companySlug, string projectSlug, string? token,
        CancellationToken ct)
    {
        var ctx = await PostConstructionReadAsync(companySlug, projectSlug, token, ct);
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var list = await _db.BldAssistanceRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tid && a.ProjectId == pid)
            .OrderByDescending(a => a.Number).Take(200)
            .ToListAsync(ct);
        var terms = await _db.BldWarrantyTerms.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tid && t.ProjectId == pid).ToListAsync(ct);
        var handover = await HandoverOfAsync(ctx, ct);
        return new PublicBuildAssistanceListDto(list.Select(a => MapAssistance(ctx, a)).ToList(),
            BuildPostConstructionRules.Categories(terms),
            ctx.Portal.Access == BuildPortalAccess.Password && handover?.IsDelivered == true);
    }

    /// <summary>
    /// The client opens a request — password portal with a session, only after the delivery (422 property_not_delivered),
    /// at most <see cref="MaxOpenPortalRequests"/> open portal requests (422 assistance_limit_reached). Category: a warranty
    /// system of the project or "Outros" (400 invalid_category).
    /// </summary>
    public async Task<PublicBuildAssistanceDto> OpenAssistanceAsync(string companySlug, string projectSlug, string? token,
        PublicBuildAssistanceOpenRequest? request, CancellationToken ct)
    {
        var ctx = await PostConstructionWriteAsync(companySlug, projectSlug, token, ct);
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var handover = await HandoverOfAsync(ctx, ct);
        if (handover?.IsDelivered != true)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodePropertyNotDelivered,
                "Os chamados de assistência ficam disponíveis depois da entrega do imóvel.");

        var r = request ?? new PublicBuildAssistanceOpenRequest(null, null, null, null, null, null);
        string? Invalid(string? text, int max, bool required) => (required && string.IsNullOrWhiteSpace(text)) || text?.Trim().Length > max
            ? "invalid" : null;
        if (Invalid(r.Title, 200, true) is not null || Invalid(r.Description, 4000, true) is not null
            || Invalid(r.Location, 200, false) is not null || Invalid(r.ContactPhone, 30, false) is not null
            || Invalid(r.OpenedByName, 200, false) is not null)
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidAssistance,
                "Informe título (até 200) e descrição (até 4000); local até 200, telefone até 30 e nome até 200 caracteres.");
        var terms = await _db.BldWarrantyTerms.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tid && t.ProjectId == pid).ToListAsync(ct);
        var category = BuildPostConstructionRules.NormalizeCategory(terms, r.Category)
            ?? throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidCategory,
                "Escolha uma das categorias da garantia ou \"Outros\".");

        var open = await _db.BldAssistanceRequests.IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tid && a.ProjectId == pid && a.OpenedVia == BuildDecisionChannel.Portal
                          && a.Status != BuildAssistanceStatus.Resolved && a.Status != BuildAssistanceStatus.Closed
                          && a.Status != BuildAssistanceStatus.Rejected, ct);
        if (open >= MaxOpenPortalRequests)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistanceLimit,
                $"Há {open} chamados abertos. Aguarde o atendimento ou fale com a construtora.");

        for (var attempt = 0; ; attempt++)
        {
            var number = await BuildPostConstructionRules.NextAssistanceNumberAsync(_db, tid, pid, ct);
            var created = BuildAssistanceRequest.Open(tid, pid, number, category, r.Title!, r.Description!, r.Location,
                BuildDecisionChannel.Portal, string.IsNullOrWhiteSpace(r.OpenedByName) ? "Cliente (portal)" : r.OpenedByName,
                r.ContactPhone, BusinessClock.Today());
            _db.BldAssistanceRequests.Add(created);
            _audit.RecordPortalClient(AuditActions.BuildAssistanceOpened, tid, "AssistanceRequest", created.Id,
                $"Cliente abriu o chamado de assistência #{number} pelo portal: {created.Title}.", r.OpenedByName,
                new { category, channel = "Portal" });
            try
            {
                await _db.SaveChangesAsync(ct);
                return MapAssistance(ctx, created);
            }
            catch (DbUpdateException ex) when (attempt == 0 && BuildScope.IsUniqueViolation(ex))
            {
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Checks everything that does not need the file (portal, session, request of THIS project, open, below the photo
    /// limit, storage on) before the controller reads and checks the upload.
    /// </summary>
    public async Task<PublicBuildAssistanceUploadTarget> AuthorizeAssistanceUploadAsync(string companySlug, string projectSlug,
        string? token, Guid publicId, CancellationToken ct)
    {
        var ctx = await PostConstructionWriteAsync(companySlug, projectSlug, token, ct);
        var request = await PortalRequestAsync(ctx, publicId, tracking: false, ct);
        if (!request.IsOpen)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistanceClosed, "Este chamado não aceita mais fotos.");
        if (request.PhotoKeys.Count >= BuildAssistanceRequest.MaxPhotos)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistancePhotoLimit,
                $"Cada chamado aceita até {BuildAssistanceRequest.MaxPhotos} fotos.");
        if (!_flags.StorageEnabled)
            throw new BuildPortalAccessException(503, PublicBuildPortal.CodeStorageUnavailable,
                "O envio de fotos está indisponível no momento. Tente mais tarde.");
        return new PublicBuildAssistanceUploadTarget(ctx.TenantId, ctx.Project.Id, request.Id, companySlug, projectSlug);
    }

    /// <summary>Stores an already validated image privately under the tenant's build/assistance folder and attaches it.</summary>
    public async Task<PublicBuildAssistanceDto> StoreAssistancePhotoAsync(PublicBuildAssistanceUploadTarget target, Stream content,
        string contentType, string extension, long length, CancellationToken ct)
    {
        var ctx = await ResolveAsync(target.CompanySlug, target.ProjectSlug, tracking: false, ct);
        if (ctx.TenantId != target.TenantId || ctx.Project.Id != target.ProjectId) throw BuildPortalAccessException.NotFound();
        var key = StorageKeys.Build(target.TenantId, BuildPostConstructionService.AssistanceFolder,
            Guid.NewGuid().ToString("N") + extension, isPrivate: true);
        try
        {
            await _storage.UploadAsync(new StorageUploadRequest(content, "assistencia" + extension, contentType, key, length), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Build portal] Assistance photo upload failed.");
            throw new BuildPortalAccessException(503, PublicBuildPortal.CodeStorageUnavailable,
                "O envio de fotos está indisponível no momento. Tente mais tarde.");
        }

        var request = await _db.BldAssistanceRequests.IgnoreQueryFilters()
            .FirstAsync(a => a.TenantId == target.TenantId && a.ProjectId == target.ProjectId && a.Id == target.RequestId, ct);
        try
        {
            request.AddPhoto(key);
            _audit.RecordPortalClient(AuditActions.BuildAssistancePhotoAdded, target.TenantId, "AssistanceRequest", request.Id,
                $"Cliente anexou uma foto ao chamado #{request.Number}.", null);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DomainException or DbUpdateException)
        {
            await DeleteQuietlyAsync(key);
            if (ex is DomainException { Code: "assistance_photo_limit" })
                throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistancePhotoLimit,
                    $"Cada chamado aceita até {BuildAssistanceRequest.MaxPhotos} fotos.");
            if (ex is DomainException)
                throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistanceClosed, "Este chamado não aceita mais fotos.");
            throw;
        }
        return MapAssistance(ctx, request);
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await _storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Build portal] Orphan assistance photo could not be deleted.");
        }
    }

    /// <summary>The client rates a resolved request (1–5) once: not resolved → 422, again → 409 feedback_already_given.</summary>
    public async Task<PublicBuildAssistanceDto> GiveAssistanceFeedbackAsync(string companySlug, string projectSlug, string? token,
        Guid publicId, PublicBuildAssistanceFeedbackRequest? body, CancellationToken ct)
    {
        var ctx = await PostConstructionWriteAsync(companySlug, projectSlug, token, ct);
        if (body?.Rating is not (>= 1 and <= 5))
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidRating, "A nota precisa ficar entre 1 e 5.");
        if (body.Comment is { Length: > 2000 })
            throw new BuildPortalAccessException(400, PublicBuildPortal.CodeInvalidRating, "O comentário pode ter até 2000 caracteres.");
        var request = await PortalRequestAsync(ctx, publicId, tracking: true, ct);
        if (request.ClientRating is not null) throw FeedbackGiven();
        if (request.Status != BuildAssistanceStatus.Resolved)
            throw new BuildPortalAccessException(422, PublicBuildPortal.CodeAssistanceNotResolved,
                "A avaliação fica disponível quando o chamado for resolvido.");
        request.GiveFeedback(body.Rating.Value, body.Comment, DateTime.UtcNow);
        _audit.RecordPortalClient(AuditActions.BuildAssistanceFeedback, ctx.TenantId, "AssistanceRequest", request.Id,
            $"Cliente avaliou o chamado #{request.Number} com nota {body.Rating}.", null, new { rating = body.Rating });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw FeedbackGiven();
        }
        return MapAssistance(ctx, request);
    }

    private static BuildPortalAccessException FeedbackGiven()
        => new(409, PublicBuildPortal.CodeFeedbackAlreadyGiven, "A avaliação deste chamado já foi enviada.");

    /// <summary>A request of the portal's own project, by public id — another project's or tenant's id is a 404.</summary>
    private async Task<BuildAssistanceRequest> PortalRequestAsync(Ctx ctx, Guid publicId, bool tracking, CancellationToken ct)
    {
        var q = _db.BldAssistanceRequests.IgnoreQueryFilters()
            .Where(a => a.TenantId == ctx.TenantId && a.ProjectId == ctx.Project.Id && a.PublicId == publicId);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw ItemNotFound();
    }

    private PublicBuildAssistanceDto MapAssistance(Ctx ctx, BuildAssistanceRequest a)
    {
        var password = ctx.Portal.Access == BuildPortalAccess.Password;
        var urls = a.PhotoKeys.Select(k => _media.SignedUrl(k, ctx.TenantId)).Where(u => u is not null).Select(u => u!).ToList();
        var timeline = new List<PublicBuildAssistanceEventDto> { new(nameof(BuildAssistanceStatus.Open), Utc(a.CreatedAt)) };
        void Add(BuildAssistanceStatus s, DateTime? at)
        {
            if (at is { } v) timeline.Add(new PublicBuildAssistanceEventDto(s.ToString(), Utc(v)));
        }
        Add(BuildAssistanceStatus.Triaged, a.TriagedAt);
        Add(BuildAssistanceStatus.InProgress, a.StartedAt);
        Add(BuildAssistanceStatus.Resolved, a.ResolvedAt);
        Add(BuildAssistanceStatus.Closed, a.ClosedAt);
        Add(BuildAssistanceStatus.Rejected, a.RejectedAt);
        return new PublicBuildAssistanceDto
        {
            Id = a.PublicId,
            Number = a.Number,
            Category = a.Category,
            Title = a.Title,
            Description = a.Description,
            Location = a.Location,
            OpenedVia = a.OpenedVia.ToString(),
            OpenedAt = Utc(a.CreatedAt),
            Status = a.Status.ToString(),
            WarrantyCovered = a.TriagedAt is not null ? a.WarrantyCovered : null,
            ScheduledFor = a.ScheduledFor is { } s ? Utc(s) : null,
            ResolutionNotes = a.ResolutionNotes,
            RejectionReason = a.RejectionReason,
            Rating = a.ClientRating,
            Feedback = a.ClientFeedback,
            PhotoUrls = urls,
            PhotoUrlsExpireAt = urls.Count > 0 ? DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl) : null,
            CanAddPhotos = password && a.CanAddPhotos,
            CanGiveFeedback = password && a.CanGiveFeedback,
            Timeline = timeline.OrderBy(t => t.At).ToList(),
        };
    }
}
