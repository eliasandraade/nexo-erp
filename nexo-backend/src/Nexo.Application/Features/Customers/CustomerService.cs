using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Customers;

public class CustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentTenant _currentTenant;

    public CustomerService(ICustomerRepository customers, ICurrentTenant currentTenant)
    {
        _customers = customers;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _customers.GetAllAsync(includeInactive, ct);
        return list.Select(MapToDto).ToList();
    }

    public async Task<CustomerDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Customer", id);
        return MapToDto(customer);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        if (await _customers.DocumentExistsAsync(request.DocumentNumber, ct: ct))
            throw new ConflictException($"Document number '{request.DocumentNumber}' is already registered.");

        var personType   = Enum.Parse<PersonType>(request.PersonType, ignoreCase: true);
        var documentType = Enum.Parse<DocumentType>(request.DocumentType, ignoreCase: true);

        var customer = Customer.Create(
            _currentTenant.Id,
            personType,
            request.Name,
            documentType,
            request.DocumentNumber,
            request.TradeName,
            request.Email,
            request.Phone,
            request.WhatsApp,
            request.AddressJson,
            request.CreditLimit,
            request.Notes);

        await _customers.AddAsync(customer, ct);
        await _customers.SaveChangesAsync(ct);
        return MapToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Customer", id);

        customer.Update(
            request.Name,
            request.TradeName,
            request.Email,
            request.Phone,
            request.WhatsApp,
            request.AddressJson,
            request.CreditLimit,
            request.Notes);

        await _customers.SaveChangesAsync(ct);
        return MapToDto(customer);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Customer", id);
        customer.Activate();
        await _customers.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Customer", id);
        customer.Deactivate();
        await _customers.SaveChangesAsync(ct);
    }

    private static CustomerDto MapToDto(Customer c) => new(
        c.Id,
        c.PersonType.ToString(),
        c.Name,
        c.TradeName,
        c.DocumentType.ToString(),
        c.DocumentNumber,
        c.Email,
        c.Phone,
        c.WhatsApp,
        c.AddressJson,
        c.CreditLimit,
        c.Notes,
        c.IsActive,
        c.CreatedAt,
        c.UpdatedAt);
}
