using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Funcionário do restaurante. Um registro por pessoa por loja.
/// MonthlySalary é o custo fixo mensal utilizado no cálculo de lucro operacional.
/// </summary>
public class RestEmployee : StoreEntity
{
    private RestEmployee() { }
    private RestEmployee(Guid tenantId) : base(tenantId) { }

    public string   Name          { get; private set; } = string.Empty;
    public string   Role          { get; private set; } = string.Empty;
    public DateOnly AdmissionDate { get; private set; }
    public decimal  MonthlySalary { get; private set; }
    public string?  Notes         { get; private set; }
    public bool     IsActive      { get; private set; }

    public static RestEmployee Create(
        Guid tenantId, string name, string role,
        DateOnly admissionDate, decimal monthlySalary, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Employee name is required.");
        if (string.IsNullOrWhiteSpace(role))
            throw new DomainException("Employee role is required.");
        if (monthlySalary < 0)
            throw new DomainException("Monthly salary cannot be negative.");

        return new RestEmployee(tenantId)
        {
            Name          = name.Trim(),
            Role          = role.Trim(),
            AdmissionDate = admissionDate,
            MonthlySalary = monthlySalary,
            Notes         = notes?.Trim(),
            IsActive      = true,
        };
    }

    public void Update(string name, string role, DateOnly admissionDate, decimal monthlySalary, string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Employee name is required.");
        if (string.IsNullOrWhiteSpace(role))
            throw new DomainException("Employee role is required.");
        if (monthlySalary < 0)
            throw new DomainException("Monthly salary cannot be negative.");

        Name          = name.Trim();
        Role          = role.Trim();
        AdmissionDate = admissionDate;
        MonthlySalary = monthlySalary;
        Notes         = notes?.Trim();
        IsActive      = isActive;
        SetUpdatedAt();
    }
}
