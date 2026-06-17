using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcProfessional aggregate.
///   - All access goes through ISvcProfessionalRepository (tenant/store filtered by EF global query).
///   - TenantId comes from ICurrentTenant; StoreId is auto-injected on INSERT by the interceptor.
/// </summary>
public class SvcProfessionalService
{
    private readonly ISvcProfessionalRepository _repo;
    private readonly ICurrentTenant             _currentTenant;

    public SvcProfessionalService(ISvcProfessionalRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcProfessionalDto>> GetAllAsync(
        bool onlyActive = false, CancellationToken ct = default)
        => (await _repo.GetAllAsync(onlyActive, ct)).Select(MapToDto).ToList();

    public async Task<SvcProfessionalDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcProfessional", id));

    public async Task<SvcProfessionalDto> CreateAsync(
        CreateSvcProfessionalRequest request, CancellationToken ct = default)
    {
        var professional = SvcProfessional.Create(
            tenantId:                 _currentTenant.Id,
            name:                     request.Name,
            role:                     request.Role,
            specialty:                request.Specialty,
            color:                    request.Color,
            phone:                    request.Phone,
            email:                    request.Email,
            defaultCommissionPercent: request.DefaultCommissionPercent,
            userId:                   request.UserId);

        await _repo.AddAsync(professional, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(professional);
    }

    public async Task<SvcProfessionalDto> UpdateAsync(
        Guid id, UpdateSvcProfessionalRequest request, CancellationToken ct = default)
    {
        var professional = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcProfessional", id);

        professional.UpdateDetails(
            request.Name, request.Role, request.Specialty,
            request.Color, request.Phone, request.Email);
        professional.UpdateCommission(request.DefaultCommissionPercent);

        _repo.Update(professional);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(professional);
    }

    public async Task<SvcProfessionalDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: true, ct);

    public async Task<SvcProfessionalDto> DeactivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: false, ct);

    private async Task<SvcProfessionalDto> ToggleAsync(Guid id, bool activate, CancellationToken ct)
    {
        var professional = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("SvcProfessional", id);

        if (activate) professional.Activate();
        else          professional.Deactivate();

        _repo.Update(professional);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(professional);
    }

    internal static SvcProfessionalDto MapToDto(SvcProfessional p) => new(
        Id:                       p.Id,
        StoreId:                  p.StoreId,
        Name:                     p.Name,
        Role:                     p.Role,
        Specialty:                p.Specialty,
        Color:                    p.Color,
        Phone:                    p.Phone,
        Email:                    p.Email,
        DefaultCommissionPercent: p.DefaultCommissionPercent,
        UserId:                   p.UserId,
        IsActive:                 p.IsActive,
        CreatedAt:                p.CreatedAt,
        UpdatedAt:                p.UpdatedAt);
}
