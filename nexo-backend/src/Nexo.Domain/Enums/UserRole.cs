namespace Nexo.Domain.Enums;

/// <summary>
/// Matches the frontend roles: diretoria, gerente, vendedor, estoquista.
/// Used for policy-based authorization in the API layer.
/// </summary>
public enum UserRole
{
    Diretoria,
    Gerente,
    Vendedor,
    Estoquista
}
