using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A service professional (profissional / médico / instrutor / professor — label varies by
/// preset). Store-scoped operational record.
///
/// NOT a system user: <see cref="UserId"/> is an optional link only and no login is created in
/// v1 (auth is untouched). Commission is captured here but not calculated/reported until P2.
/// </summary>
public class SvcProfessional : StoreEntity
{
    private SvcProfessional() { }                                   // EF Core
    private SvcProfessional(Guid tenantId) : base(tenantId) { }

    public string   Name                     { get; private set; } = string.Empty;
    public string?  Role                     { get; private set; }
    public string?  Specialty                { get; private set; }
    public string?  Color                    { get; private set; }
    public string?  Phone                    { get; private set; }
    public string?  Email                    { get; private set; }
    public decimal? DefaultCommissionPercent { get; private set; }
    public Guid?    UserId                   { get; private set; }
    public bool     IsActive                 { get; private set; }

    public static SvcProfessional Create(
        Guid     tenantId,
        string   name,
        string?  role                     = null,
        string?  specialty                = null,
        string?  color                    = null,
        string?  phone                    = null,
        string?  email                    = null,
        decimal? defaultCommissionPercent = null,
        Guid?    userId                   = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Professional name is required.");
        EnsureCommissionInRange(defaultCommissionPercent);

        return new SvcProfessional(tenantId)
        {
            Name                     = name.Trim(),
            Role                     = role?.Trim(),
            Specialty                = specialty?.Trim(),
            Color                    = color?.Trim(),
            Phone                    = phone?.Trim(),
            Email                    = email?.Trim().ToLowerInvariant(),
            DefaultCommissionPercent = defaultCommissionPercent,
            UserId                   = userId,
            IsActive                 = true,
        };
    }

    public void UpdateDetails(
        string  name,
        string? role,
        string? specialty,
        string? color,
        string? phone,
        string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Professional name is required.");

        Name      = name.Trim();
        Role      = role?.Trim();
        Specialty = specialty?.Trim();
        Color     = color?.Trim();
        Phone     = phone?.Trim();
        Email     = email?.Trim().ToLowerInvariant();
        SetUpdatedAt();
    }

    public void UpdateCommission(decimal? defaultCommissionPercent)
    {
        EnsureCommissionInRange(defaultCommissionPercent);
        DefaultCommissionPercent = defaultCommissionPercent;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    private static void EnsureCommissionInRange(decimal? pct)
    {
        if (pct is < 0m or > 100m)
            throw new DomainException("Commission percent must be between 0 and 100.");
    }
}
