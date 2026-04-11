namespace Nexo.Application.Features.Auth;

// ── Requests ────────────────────────────────────────────────────────────────

public record LoginRequest(string Login, string Password);

public record VerifyManagerRequest(string Login, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record LogoutRequest(string RefreshToken);

public record SwitchStoreRequest(string StoreId);

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>
/// Minimal session descriptor returned to the frontend on login.
/// Shape matches the AuthSession interface in the frontend.
/// </summary>
public record SessionDto(
    string UserId,
    string TenantId,
    string Name,
    string Role,
    string Login,
    string Email,
    List<string> ActiveModules,
    string? StoreId,
    List<string> StoreIds,
    string CompanyName = "",
    string Type = "tenant");

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    SessionDto Session);

public record RefreshResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

public record VerifyManagerResponse(
    bool Authorized,
    string? ManagerUserId,
    string? ManagerName,
    string? Role);

public record SwitchStoreResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    string StoreId);
