using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/employees")]
[Authorize]
[RequireModule("restaurante")]
public class EmployeesController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;

    public EmployeesController(NexoDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    // GET /api/restaurante/employees?includeInactive=false
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeDto>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var q = _db.RestEmployees.AsQueryable();
        if (!includeInactive) q = q.Where(e => e.IsActive);

        var list = await q
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeDto(
                e.Id, e.Name, e.Role, e.AdmissionDate,
                e.MonthlySalary, e.Notes, e.IsActive, e.CreatedAt))
            .ToListAsync(ct);

        return Ok(list);
    }

    // GET /api/restaurante/employees/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Get(Guid id, CancellationToken ct)
    {
        var e = await _db.RestEmployees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return Ok(new EmployeeDto(e.Id, e.Name, e.Role, e.AdmissionDate, e.MonthlySalary, e.Notes, e.IsActive, e.CreatedAt));
    }

    // POST /api/restaurante/employees
    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create(
        [FromBody] CreateEmployeeRequest req, CancellationToken ct)
    {
        var employee = RestEmployee.Create(
            _tenant.Id, req.Name, req.Role, req.AdmissionDate, req.MonthlySalary, req.Notes);

        _db.RestEmployees.Add(employee);
        await _db.SaveChangesAsync(ct);

        var dto = new EmployeeDto(employee.Id, employee.Name, employee.Role,
            employee.AdmissionDate, employee.MonthlySalary, employee.Notes,
            employee.IsActive, employee.CreatedAt);

        return CreatedAtAction(nameof(Get), new { id = employee.Id }, dto);
    }

    // PUT /api/restaurante/employees/{id}
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(
        Guid id, [FromBody] UpdateEmployeeRequest req, CancellationToken ct)
    {
        var employee = await _db.RestEmployees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (employee is null) return NotFound();

        employee.Update(req.Name, req.Role, req.AdmissionDate, req.MonthlySalary, req.Notes, req.IsActive);
        await _db.SaveChangesAsync(ct);

        return Ok(new EmployeeDto(employee.Id, employee.Name, employee.Role,
            employee.AdmissionDate, employee.MonthlySalary, employee.Notes,
            employee.IsActive, employee.CreatedAt));
    }
}

// ── Request/Response records ──────────────────────────────────────────────────

public record CreateEmployeeRequest(
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes);

public record UpdateEmployeeRequest(
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes,
    bool     IsActive);

public record EmployeeDto(
    Guid     Id,
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes,
    bool     IsActive,
    DateTime CreatedAt);
