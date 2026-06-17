using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcAppointment aggregate. Resolves and validates the referenced customer,
/// professional, catalog item, and (optional) subject through tenant/store-filtered repositories
/// (cross-tenant rows are invisible → 404). Enforces active professional/catalog (422),
/// RequiresSubject + subject-belongs-to-customer (422), and per-professional overlap (409). The
/// price is snapshotted from the catalog at create/reschedule time.
/// </summary>
public class SvcAppointmentService
{
    private readonly ISvcAppointmentRepository  _repo;
    private readonly ICustomerRepository        _customers;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ISvcCatalogItemRepository  _catalog;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ICurrentTenant             _currentTenant;

    public SvcAppointmentService(
        ISvcAppointmentRepository repo,
        ICustomerRepository customers,
        ISvcProfessionalRepository professionals,
        ISvcCatalogItemRepository catalog,
        ISvcSubjectRepository subjects,
        ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _customers     = customers;
        _professionals = professionals;
        _catalog       = catalog;
        _subjects      = subjects;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcAppointmentDto>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default)
        => (await _repo.GetAllAsync(from, to, professionalId, status, customerId, subjectId, ct))
            .Select(MapToDto).ToList();

    public async Task<SvcAppointmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id));

    public async Task<SvcAppointmentDto> CreateAsync(CreateSvcAppointmentRequest r, CancellationToken ct = default)
    {
        var price = await ResolveAndValidateRefsAsync(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, ct);
        await EnsureNoOverlapAsync(r.ProfessionalId, r.StartsAt, r.EndsAt, null, ct);

        var appt = SvcAppointment.Create(
            _currentTenant.Id, r.CustomerId, r.ProfessionalId, r.CatalogItemId,
            r.SubjectId, r.StartsAt, r.EndsAt, price, r.Notes);

        await _repo.AddAsync(appt, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    public async Task<SvcAppointmentDto> UpdateAsync(Guid id, UpdateSvcAppointmentRequest r, CancellationToken ct = default)
    {
        var appt = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id);
        if (appt.IsTerminal)
            throw new DomainException($"Cannot edit a {appt.Status} appointment.");

        var price = await ResolveAndValidateRefsAsync(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, ct);
        await EnsureNoOverlapAsync(r.ProfessionalId, r.StartsAt, r.EndsAt, id, ct);

        appt.Reschedule(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, r.StartsAt, r.EndsAt, price, r.Notes);
        _repo.Update(appt);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    public async Task<SvcAppointmentDto> ChangeStatusAsync(
        Guid id, ChangeSvcAppointmentStatusRequest r, CancellationToken ct = default)
    {
        var appt = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id);
        appt.ChangeStatus(r.Status!.Value, r.Reason);   // Status is NotNull-validated upstream
        _repo.Update(appt);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    private async Task<decimal> ResolveAndValidateRefsAsync(
        Guid customerId, Guid professionalId, Guid catalogItemId, Guid? subjectId, CancellationToken ct)
    {
        _ = await _customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException(nameof(Customer), customerId);

        var professional = await _professionals.GetByIdAsync(professionalId, ct)
            ?? throw new NotFoundException("SvcProfessional", professionalId);
        if (!professional.IsActive) throw new DomainException("Professional is not active.");

        var catalog = await _catalog.GetByIdAsync(catalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", catalogItemId);
        if (!catalog.IsActive) throw new DomainException("Catalog item is not active.");

        if (catalog.RequiresSubject && subjectId is null)
            throw new DomainException("This service requires a subject.");

        if (subjectId is { } sid)
        {
            var subject = await _subjects.GetByIdAsync(sid, ct)
                ?? throw new NotFoundException("SvcSubject", sid);
            if (subject.CustomerId != customerId)
                throw new DomainException("Subject does not belong to the customer.");
        }

        return catalog.Price;
    }

    private async Task EnsureNoOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct)
    {
        if (await _repo.HasOverlapAsync(professionalId, startsAt, endsAt, excludeId, ct))
            throw new ConflictException("The professional already has an appointment in this time range.");
    }

    internal static SvcAppointmentDto MapToDto(SvcAppointment a) => new(
        Id:                 a.Id,
        StoreId:            a.StoreId,
        CustomerId:         a.CustomerId,
        ProfessionalId:     a.ProfessionalId,
        CatalogItemId:      a.CatalogItemId,
        SubjectId:          a.SubjectId,
        StartsAt:           a.StartsAt,
        EndsAt:             a.EndsAt,
        Status:             a.Status,
        Notes:              a.Notes,
        CancellationReason: a.CancellationReason,
        PriceSnapshot:      a.PriceSnapshot,
        CreatedAt:          a.CreatedAt,
        UpdatedAt:          a.UpdatedAt);
}
