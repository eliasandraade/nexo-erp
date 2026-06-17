using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcSubject aggregate. Tenant isolation is enforced by the EF global query
/// filter; the referenced Customer is validated through ICustomerRepository (also tenant-filtered),
/// so a customer from another tenant is invisible → NotFound → 404 (blocks cross-tenant linking).
/// </summary>
public class SvcSubjectService
{
    private readonly ISvcSubjectRepository _repo;
    private readonly ICustomerRepository   _customers;
    private readonly ICurrentTenant        _currentTenant;

    public SvcSubjectService(
        ISvcSubjectRepository repo, ICustomerRepository customers, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _customers     = customers;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcSubjectDto>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default)
        => (await _repo.GetAllAsync(customerId, kind, active, ct)).Select(MapToDto).ToList();

    public async Task<SvcSubjectDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id));

    public async Task<SvcSubjectDto> CreateAsync(CreateSvcSubjectRequest request, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(request.CustomerId, ct);

        var subject = SvcSubject.Create(
            tenantId:     _currentTenant.Id,
            customerId:   request.CustomerId,
            kind:         request.Kind,
            displayName:  request.DisplayName,
            metadataJson: request.MetadataJson,
            notes:        request.Notes);

        await _repo.AddAsync(subject, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    public async Task<SvcSubjectDto> UpdateAsync(Guid id, UpdateSvcSubjectRequest request, CancellationToken ct = default)
    {
        var subject = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);

        subject.UpdateDetails(request.Kind, request.DisplayName, request.Notes);
        subject.UpdateMetadata(request.MetadataJson);

        _repo.Update(subject);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    public async Task<SvcSubjectDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: true, ct);

    public async Task<SvcSubjectDto> DeactivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: false, ct);

    private async Task<SvcSubjectDto> ToggleAsync(Guid id, bool activate, CancellationToken ct)
    {
        var subject = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);
        if (activate) subject.Activate(); else subject.Deactivate();
        _repo.Update(subject);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken ct)
    {
        _ = await _customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException(nameof(Customer), customerId);
    }

    internal static SvcSubjectDto MapToDto(SvcSubject s) => new(
        Id:           s.Id,
        CustomerId:   s.CustomerId,
        Kind:         s.Kind,
        DisplayName:  s.DisplayName,
        MetadataJson: s.MetadataJson,
        Notes:        s.Notes,
        IsActive:     s.IsActive,
        CreatedAt:    s.CreatedAt,
        UpdatedAt:    s.UpdatedAt);
}
