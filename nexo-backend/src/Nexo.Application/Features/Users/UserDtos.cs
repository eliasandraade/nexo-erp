namespace Nexo.Application.Features.Users;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateUserRequest(
    string FullName,
    string Email,
    string Login,
    string Password,
    string Role,          // "diretoria" | "gerente" | "vendedor" | "estoquista"
    string? Phone,
    string? Notes,
    bool RequirePasswordChange = false);

public record UpdateUserRequest(
    string FullName,
    string Email,
    string Role,
    string? Phone,
    string? Notes,
    string? Status);     // "active" | "inactive" | "blocked"

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AdminChangePasswordRequest(string NewPassword);

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>
/// Full user descriptor. Shape aligns with the frontend User interface.
/// PasswordHash is never included in responses.
/// </summary>
public record UserDto(
    string Id,
    string TenantId,
    string FullName,
    string Email,
    string Login,
    string? Phone,
    string Role,
    string Status,
    bool RequirePasswordChange,
    string? Notes,
    DateTime? LastAccessAt,
    DateTime? PasswordChangedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record ValidateManagerRequest(string Login, string Password);

public record ValidateManagerResponse(
    bool Success,
    string? ErrorMessage,
    string? FullName,
    string? Role);
