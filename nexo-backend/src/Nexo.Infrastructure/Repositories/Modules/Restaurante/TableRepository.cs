using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class TableRepository : ITableRepository
{
    private readonly NexoDbContext _context;

    public TableRepository(NexoDbContext context) => _context = context;

    public async Task<RestTable?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestTables
            .Include(x => x.Area)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>
    /// Carrega a mesa com lock pessimista (SELECT FOR UPDATE via EF raw SQL).
    /// Deve ser chamado dentro de uma transação ativa.
    /// Garante que nenhuma outra transação possa abrir comanda para a mesma mesa
    /// simultaneamente até que a transação atual seja confirmada ou revertida.
    /// </summary>
    public async Task<RestTable?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        // EF Core não suporta SELECT FOR UPDATE nativamente.
        // Usamos FromSqlRaw para emitir o lock explicitamente no PostgreSQL.
        var tables = await _context.RestTables
            .FromSqlRaw(
                "SELECT * FROM nexo.rest_tables WHERE id = {0} AND tenant_id = {1} FOR UPDATE",
                id, _context.CurrentTenantIdForFilter)
            .Include(x => x.Area)
            .ToListAsync(ct);

        return tables.FirstOrDefault();
    }

    public async Task<IReadOnlyList<RestTable>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _context.RestTables.Include(x => x.Area).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Area!.Name).ThenBy(x => x.Number).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RestTable>> GetByAreaAsync(Guid areaId, CancellationToken ct = default)
        => await _context.RestTables
            .Include(x => x.Area)
            .Where(x => x.AreaId == areaId)
            .OrderBy(x => x.Number)
            .ToListAsync(ct);

    public async Task AddAsync(RestTable table, CancellationToken ct = default)
        => await _context.RestTables.AddAsync(table, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
