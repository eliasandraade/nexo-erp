using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Public;

/// <summary>
/// The single, preset-adaptive engine behind the public booking site (no auth). A store is
/// resolved from its <see cref="Store.PublicSlug"/> (globally unique), and tenant/store are then
/// passed explicitly to every read/write — the EF global query filter is bypassed and the
/// TenantSaveChangesInterceptor is a no-op without a resolved tenant.
///
/// Security posture: the client never chooses a CustomerId, SubjectId, StoreId or price; the
/// customer is resolved/created from the phone, the subject is created server-side, the price is
/// snapshotted from the catalog, and availability/overlap are recomputed from the database. No
/// internal identifiers, costs, commissions or other customers' data leave this service.
/// </summary>
public sealed class PublicServicePortalService
{
    private readonly IStoreRepository           _stores;
    private readonly ISvcSettingsRepository     _settings;
    private readonly ISvcCatalogItemRepository  _catalog;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ISvcAppointmentRepository  _appointments;
    private readonly ICustomerRepository        _customers;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly IUnitOfWork                _uow;
    private readonly ILogger<PublicServicePortalService> _logger;

    public PublicServicePortalService(
        IStoreRepository stores,
        ISvcSettingsRepository settings,
        ISvcCatalogItemRepository catalog,
        ISvcProfessionalRepository professionals,
        ISvcAppointmentRepository appointments,
        ICustomerRepository customers,
        ISvcSubjectRepository subjects,
        ILogger<PublicServicePortalService> logger,
        IUnitOfWork uow)
    {
        _uow = uow;
        _stores        = stores;
        _settings      = settings;
        _catalog       = catalog;
        _professionals = professionals;
        _appointments  = appointments;
        _customers     = customers;
        _subjects      = subjects;
        _logger        = logger;
    }

    // ── Header ──────────────────────────────────────────────────────────────────

    public async Task<PublicServicePortalDto> GetPortalAsync(string slug, CancellationToken ct = default)
    {
        var ctx = await ResolveAsync(slug, ct);
        return new PublicServicePortalDto(
            StoreName:                     ctx.Store.Name,
            PresetKey:                     ctx.Preset.Key,
            PresetDisplayName:             ctx.Preset.DisplayName,
            Labels:                        ctx.Preset.Labels,
            Capabilities:                  ctx.Preset.Capabilities,
            ShowPrices:                    ctx.Settings.ShowPrices,
            RequiresProfessionalSelection: true, // v1 decision: a professional is always chosen first
            IsBookingEnabled:              ctx.Settings.PublicBookingEnabled,
            DisplayName:                   ctx.Settings.DisplayName,
            Description:                   ctx.Settings.Description,
            LogoUrl:                       ctx.Settings.LogoUrl,
            CoverImageUrl:                 ctx.Settings.CoverImageUrl,
            BrandColor:                    ctx.Settings.BrandColor,
            WhatsApp:                      ctx.Settings.WhatsApp,
            Address:                       ctx.Settings.Address);
    }

    // ── Catalog ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PublicServiceCatalogItemDto>> GetCatalogAsync(
        string slug, CancellationToken ct = default)
    {
        var ctx = await ResolveBookableAsync(slug, ct);
        var items = await _catalog.GetActivePublicAsync(ctx.Store.TenantId, ctx.Store.Id, ct);

        var subjectAlwaysRequired = ctx.Preset.Capabilities.SubjectKind is not null;
        return items.Select(i => new PublicServiceCatalogItemDto(
            Id:              i.Id,
            Name:            i.Name,
            Description:     i.Description,
            Category:        i.Category,
            DurationMinutes: i.DurationMinutes,
            Price:           ctx.Settings.ShowPrices ? i.Price : null,
            RequiresSubject: i.RequiresSubject || subjectAlwaysRequired)).ToList();
    }

    // ── Professionals ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PublicServiceProfessionalDto>> GetProfessionalsAsync(
        string slug, CancellationToken ct = default)
    {
        var ctx = await ResolveBookableAsync(slug, ct);
        var pros = await _professionals.GetActivePublicAsync(ctx.Store.TenantId, ctx.Store.Id, ct);
        return pros.Select(p => new PublicServiceProfessionalDto(
            p.Id, p.Name, p.Role, p.Specialty, p.Color)).ToList();
    }

    // ── Availability ────────────────────────────────────────────────────────────

    public async Task<PublicAvailabilityDto> GetAvailabilityAsync(
        string slug, Guid catalogItemId, Guid professionalId,
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var ctx = await ResolveBookableAsync(slug, ct);

        if (professionalId == Guid.Empty)
            throw new DomainException("A professional must be selected to view availability.");

        var catalog = await LoadActiveCatalogAsync(ctx, catalogItemId, ct);
        var professional = await LoadActiveProfessionalAsync(ctx, professionalId, ct);

        var hours = ServiceWorkingHours.Parse(professional.WorkingHoursJson);
        var tz    = ResolveTimeZone(ctx.Settings.TimeZoneId);
        var now   = DateTime.UtcNow;

        var slots = new List<PublicAvailabilitySlotDto>();
        if (!hours.IsEmpty)
        {
            var horizonUtc = now.AddDays(ctx.Settings.BookingDaysAhead);
            var blocking = await _appointments.GetBlockingForProfessionalPublicAsync(
                professional.Id, ctx.Store.TenantId, ctx.Store.Id, now, horizonUtc, ct);
            var blocks = await _appointments.GetBlocksPublicAsync(
                professional.Id, ctx.Store.TenantId, ctx.Store.Id, now, horizonUtc, ct);
            var busy = blocking.Select(a => new BusyInterval(a.StartsAt, a.EndsAt))
                .Concat(blocks.Select(b => new BusyInterval(b.StartsAt, b.EndsAt)))
                .ToList();

            var starts = AvailabilityCalculator.GenerateSlots(
                hours, tz, now,
                ctx.Settings.MinLeadMinutes, ctx.Settings.BookingDaysAhead,
                ctx.Settings.SlotIntervalMinutes, catalog.DurationMinutes,
                busy, NormalizeUtc(fromUtc), NormalizeUtc(toUtc));

            slots = starts
                .Select(s => new PublicAvailabilitySlotDto(s, s.AddMinutes(catalog.DurationMinutes)))
                .ToList();
        }

        return new PublicAvailabilityDto(professional.Id, catalog.Id, catalog.DurationMinutes, slots);
    }

    // ── Booking ─────────────────────────────────────────────────────────────────

    public async Task<PublicAppointmentCreatedDto> CreateAppointmentAsync(
        string slug, CreatePublicAppointmentRequest request, CancellationToken ct = default)
    {
        var ctx = await ResolveBookableAsync(slug, ct);

        var catalog = await LoadActiveCatalogAsync(ctx, request.CatalogItemId, ct);
        var professional = await LoadActiveProfessionalAsync(ctx, request.ProfessionalId, ct);

        if (request.StartsAt.Kind != DateTimeKind.Utc)
            throw new DomainException("StartsAt must be UTC (use a trailing Z).");

        var startsAt = request.StartsAt;
        var endsAt   = startsAt.AddMinutes(catalog.DurationMinutes);
        var now      = DateTime.UtcNow;

        if (startsAt < now.AddMinutes(ctx.Settings.MinLeadMinutes))
            throw new DomainException("This time is too soon to book.");
        if (startsAt > now.AddDays(ctx.Settings.BookingDaysAhead))
            throw new DomainException("This time is beyond the booking window.");

        var hours = ServiceWorkingHours.Parse(professional.WorkingHoursJson);
        var tz    = ResolveTimeZone(ctx.Settings.TimeZoneId);
        if (!AvailabilityCalculator.IsWithinWorkingHours(hours, tz, startsAt, endsAt))
            throw new DomainException("The selected time is outside the professional's working hours.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _appointments.LockProfessionalAsync(professional.Id, ct);   // a concurrent booking waits here
        if (await _appointments.HasOverlapPublicAsync(
                professional.Id, ctx.Store.TenantId, ctx.Store.Id, startsAt, endsAt, ct)
            || await _appointments.IsBlockedPublicAsync(
                professional.Id, ctx.Store.TenantId, ctx.Store.Id, startsAt, endsAt, ct))
            throw new ConflictException("This time was just taken. Please pick another slot.");

        var subjectRequired = catalog.RequiresSubject || ctx.Preset.Capabilities.SubjectKind is not null;
        if (subjectRequired && request.Subject is null)
            throw new DomainException("This service requires additional details (pet / vehicle / etc.).");

        // Resolve or create the customer from the phone (never a client-supplied CustomerId).
        var customer = await ResolveOrCreateCustomerAsync(ctx.Store.TenantId, request, ct);

        Guid? subjectId = null;
        if (subjectRequired)
        {
            var kind = ResolveSubjectKind(request.Subject!.Kind, ctx.Preset.Capabilities.SubjectKind);
            var subject = SvcSubject.Create(
                ctx.Store.TenantId, customer.Id, kind,
                request.Subject.DisplayName, metadataJson: null, notes: request.Subject.Notes);
            await _subjects.AddAsync(subject, ct);
            subjectId = subject.Id;
        }

        var appt = SvcAppointment.CreateForStore(
            ctx.Store.TenantId, ctx.Store.Id, customer.Id, professional.Id, catalog.Id,
            subjectId, startsAt, endsAt, catalog.Price, request.Notes,
            SvcCommissionPolicy.ResolvePercent(catalog.CommissionPercent, professional.DefaultCommissionPercent));

        if (ctx.Settings.AutoConfirmAppointments)
            appt.ChangeStatus(SvcAppointmentStatus.Confirmed, null);

        await _appointments.AddAsync(appt, ct);
        // Single commit: EF orders customer → subject → appointment via the configured FKs.
        await _appointments.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Public appointment {ApptId} created via portal '{Slug}' (TenantId: {TenantId}, StoreId: {StoreId}, Professional: {ProfId})",
            appt.Id, slug, ctx.Store.TenantId, ctx.Store.Id, professional.Id);

        return new PublicAppointmentCreatedDto(
            AppointmentId:    appt.Id,
            Status:           appt.Status.ToString(),
            StartsAt:         appt.StartsAt,
            EndsAt:           appt.EndsAt,
            ServiceName:      catalog.Name,
            ProfessionalName: professional.Name,
            CustomerName:     request.CustomerName.Trim(),
            ManageToken:      appt.PublicToken);
    }

    // ── Customer's own link ─────────────────────────────────────────────────────

    public async Task<PublicAppointmentViewDto> GetByTokenAsync(string slug, string token, CancellationToken ct = default)
    {
        var ctx = await ResolveAsync(slug, ct);
        var appt = await LoadByTokenAsync(ctx, token, ct);
        return await ViewAsync(ctx, appt, ct);
    }

    /// <summary>
    /// The customer cancels their own booking. Allowed while it has not started and up to the same
    /// lead a booking needs (MinLeadMinutes); later than that, they talk to the store. Works even if
    /// online booking was switched off since — it only frees a slot.
    /// </summary>
    public async Task<PublicAppointmentViewDto> CancelByTokenAsync(string slug, string token, CancellationToken ct = default)
    {
        var ctx = await ResolveAsync(slug, ct);
        var appt = await LoadByTokenAsync(ctx, token, ct);
        appt.CancelByCustomer(DateTime.UtcNow, ctx.Settings.MinLeadMinutes);
        await _appointments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Public appointment {ApptId} cancelled by the customer via portal '{Slug}' (TenantId: {TenantId}, StoreId: {StoreId})",
            appt.Id, slug, ctx.Store.TenantId, ctx.Store.Id);
        return await ViewAsync(ctx, appt, ct);
    }

    private async Task<SvcAppointment> LoadByTokenAsync(PortalContext ctx, string token, CancellationToken ct)
    {
        var key = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length != 32 || !key.All(Uri.IsHexDigit))
            throw new NotFoundException("Appointment", token ?? string.Empty);
        return await _appointments.GetByPublicTokenAsync(key, ctx.Store.TenantId, ctx.Store.Id, ct)
            ?? throw new NotFoundException("Appointment", key);
    }

    private async Task<PublicAppointmentViewDto> ViewAsync(PortalContext ctx, SvcAppointment appt, CancellationToken ct)
    {
        var catalog = await _catalog.GetByIdPublicAsync(appt.CatalogItemId, ctx.Store.TenantId, ctx.Store.Id, ct);
        var professional = await _professionals.GetByIdPublicAsync(appt.ProfessionalId, ctx.Store.TenantId, ctx.Store.Id, ct);
        return new PublicAppointmentViewDto(
            Status:           appt.Status.ToString(),
            StartsAt:         appt.StartsAt,
            EndsAt:           appt.EndsAt,
            ServiceName:      catalog?.Name ?? "Serviço",
            ProfessionalName: professional?.Name ?? "Profissional",
            StoreName:        ctx.Settings.DisplayName ?? ctx.Store.Name,
            CanCancel:        appt.CustomerCanCancel(DateTime.UtcNow, ctx.Settings.MinLeadMinutes),
            CancelDeadline:   appt.CustomerCancelDeadline(ctx.Settings.MinLeadMinutes));
    }

    // ── Resolution helpers ──────────────────────────────────────────────────────

    private sealed record PortalContext(Store Store, SvcSettings Settings, ServicePreset Preset);

    /// <summary>Resolves the slug to a configured Service store. 404 when the slug is unknown or not a Service store.</summary>
    private async Task<PortalContext> ResolveAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new NotFoundException("ServicePortal", slug);

        var store = await _stores.GetByPublicSlugAsync(slug.Trim().ToLowerInvariant(), ct)
            ?? throw new NotFoundException("ServicePortal", slug);

        if (store.PublicSlug is null)
            throw new NotFoundException("ServicePortal", slug);

        // Only a store that has configured Service (has a settings row) exposes a Service portal.
        // This also keeps restaurant slugs from resolving here.
        var settings = await _settings.GetByStorePublicAsync(store.TenantId, store.Id, ct)
            ?? throw new NotFoundException("ServicePortal", slug);

        var preset = ServicePresetRegistry.GetByKey(settings.PresetKey)
            ?? throw new NotFoundException("ServicePortal", slug);

        return new PortalContext(store, settings, preset);
    }

    /// <summary>Like <see cref="ResolveAsync"/> but additionally requires booking to be enabled (403 otherwise).</summary>
    private async Task<PortalContext> ResolveBookableAsync(string slug, CancellationToken ct)
    {
        var ctx = await ResolveAsync(slug, ct);
        if (!ctx.Settings.PublicBookingEnabled)
            throw new ForbiddenException("Public booking is not enabled for this store.");
        return ctx;
    }

    private async Task<SvcCatalogItem> LoadActiveCatalogAsync(PortalContext ctx, Guid catalogItemId, CancellationToken ct)
    {
        var catalog = await _catalog.GetByIdPublicAsync(catalogItemId, ctx.Store.TenantId, ctx.Store.Id, ct)
            ?? throw new NotFoundException("Service", catalogItemId);
        if (!catalog.IsActive)
            throw new DomainException("This service is not available.");
        return catalog;
    }

    private async Task<SvcProfessional> LoadActiveProfessionalAsync(PortalContext ctx, Guid professionalId, CancellationToken ct)
    {
        var professional = await _professionals.GetByIdPublicAsync(professionalId, ctx.Store.TenantId, ctx.Store.Id, ct)
            ?? throw new NotFoundException("Professional", professionalId);
        if (!professional.IsActive)
            throw new DomainException("This professional is not available.");
        return professional;
    }

    private async Task<Customer> ResolveOrCreateCustomerAsync(
        Guid tenantId, CreatePublicAppointmentRequest request, CancellationToken ct)
    {
        var phone = NormalizePhone(request.Phone);
        if (phone.Length == 0)
            throw new DomainException("A valid phone number is required.");

        var existing = await _customers.GetByPhonePublicAsync(tenantId, phone, ct);
        if (existing is not null) return existing;

        var document = await BuildUniqueDocumentAsync(tenantId, phone, ct);
        var customer = Customer.Create(
            tenantId:       tenantId,
            personType:     PersonType.Individual,
            name:           request.CustomerName,
            documentType:   DocumentType.Cpf,
            documentNumber: document,
            email:          request.Email,
            phone:          phone);

        await _customers.AddAsync(customer, ct);
        return customer;
    }

    /// <summary>
    /// (tenant, document_number) is UNIQUE. Portal customers carry no real CPF, so we use the phone
    /// as the document when free, else a random token — guaranteeing the insert never trips the index.
    /// </summary>
    private async Task<string> BuildUniqueDocumentAsync(Guid tenantId, string phone, CancellationToken ct)
    {
        if (!await _customers.DocumentExistsPublicAsync(tenantId, phone, ct))
            return phone;
        return ("P" + Guid.NewGuid().ToString("N"))[..20];
    }

    private static SvcSubjectKind ResolveSubjectKind(string? requested, ServiceSubjectKind? presetDefault)
    {
        if (!string.IsNullOrWhiteSpace(requested)
            && Enum.TryParse<SvcSubjectKind>(requested.Trim(), ignoreCase: true, out var parsed))
            return parsed;
        return MapPresetSubjectKind(presetDefault);
    }

    /// <summary>Maps the preset capability kind (ServiceSubjectKind) to the entity kind (SvcSubjectKind).</summary>
    private static SvcSubjectKind MapPresetSubjectKind(ServiceSubjectKind? kind) => kind switch
    {
        ServiceSubjectKind.Pet     => SvcSubjectKind.Pet,
        ServiceSubjectKind.Vehicle => SvcSubjectKind.Vehicle,
        ServiceSubjectKind.Student => SvcSubjectKind.Student,
        _                          => SvcSubjectKind.Other,
    };

    private static string NormalizePhone(string? phone)
        => new(string.IsNullOrEmpty(phone) ? [] : phone.Where(char.IsDigit).ToArray());

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is not { } v) return null;
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }

    private TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, "America/Sao_Paulo" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        _logger.LogWarning("Unknown timezone '{TimeZoneId}' — falling back to UTC.", timeZoneId);
        return TimeZoneInfo.Utc;
    }
}
