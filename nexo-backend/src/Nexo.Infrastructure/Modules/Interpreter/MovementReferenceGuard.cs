using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Checks each reference through the tenant query filters of the context: an id of another company simply isn't found.
/// Contexts without an entity of their own (service, department) carry no id to check.
/// </summary>
public sealed class MovementReferenceGuard : IMovementReferenceGuard
{
    private readonly NexoDbContext _db;

    public MovementReferenceGuard(NexoDbContext db) => _db = db;

    public async Task EnsureOwnedAsync(Guid? categoryId, Guid? accountId, FinancialContextType contextType, Guid? contextId, Guid? supplierId,
        CancellationToken ct = default)
    {
        if (accountId is { } account && !await _db.FinancialAccounts.AnyAsync(a => a.Id == account, ct)) Refuse("conta");
        if (supplierId is { } supplier && !await _db.Suppliers.AnyAsync(s => s.Id == supplier, ct)) Refuse("fornecedor");
        if (categoryId is { } category && !await _db.Categories.AnyAsync(c => c.Id == category, ct)) Refuse("categoria");
        if (contextId is { } context)
        {
            var owned = contextType switch
            {
                FinancialContextType.Obra => await _db.BldProjects.AnyAsync(p => p.Id == context, ct),
                FinancialContextType.Loja => await _db.Stores.AnyAsync(s => s.Id == context, ct),
                _ => true,
            };
            if (!owned) Refuse(contextType == FinancialContextType.Obra ? "obra" : "loja");
        }
    }

    private static void Refuse(string what)
        => throw new DomainException($"A {what} informada não existe nesta empresa.", "movement_reference_not_found");
}
