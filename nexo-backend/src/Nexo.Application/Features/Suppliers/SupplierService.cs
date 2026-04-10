using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Suppliers;

public class SupplierService
{
    private readonly ISupplierRepository _suppliers;
    private readonly ICurrentTenant _currentTenant;

    public SupplierService(ISupplierRepository suppliers, ICurrentTenant currentTenant)
    {
        _suppliers = suppliers;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _suppliers.GetAllAsync(includeInactive, ct);
        return list.Select(MapToDto).ToList();
    }

    public async Task<SupplierDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Supplier", id);
        return MapToDto(supplier);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct = default)
    {
        if (await _suppliers.DocumentExistsAsync(request.DocumentNumber, ct: ct))
            throw new ConflictException($"Document number '{request.DocumentNumber}' is already registered.");

        var personType   = Enum.Parse<PersonType>(request.PersonType, ignoreCase: true);
        var documentType = Enum.Parse<DocumentType>(request.DocumentType, ignoreCase: true);

        var supplier = Supplier.Create(
            _currentTenant.Id,
            personType,
            request.Name,
            documentType,
            request.DocumentNumber,
            request.TradeName,
            request.Email,
            request.Phone,
            request.ContactName,
            request.AddressJson,
            request.PaymentTermsDays,
            request.BankInfoJson,
            request.Notes);

        await _suppliers.AddAsync(supplier, ct);
        await _suppliers.SaveChangesAsync(ct);
        return MapToDto(supplier);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Supplier", id);

        supplier.Update(
            request.Name,
            request.TradeName,
            request.Email,
            request.Phone,
            request.ContactName,
            request.AddressJson,
            request.PaymentTermsDays,
            request.BankInfoJson,
            request.Notes);

        await _suppliers.SaveChangesAsync(ct);
        return MapToDto(supplier);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Supplier", id);
        supplier.Activate();
        await _suppliers.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Supplier", id);
        supplier.Deactivate();
        await _suppliers.SaveChangesAsync(ct);
    }

    private static SupplierDto MapToDto(Supplier s) => new(
        s.Id,
        s.PersonType.ToString(),
        s.Name,
        s.TradeName,
        s.DocumentType.ToString(),
        s.DocumentNumber,
        s.Email,
        s.Phone,
        s.ContactName,
        s.AddressJson,
        s.PaymentTermsDays,
        s.BankInfoJson,
        s.Notes,
        s.IsActive,
        s.CreatedAt,
        s.UpdatedAt);
}
