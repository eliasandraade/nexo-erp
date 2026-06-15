namespace Nexo.Application.Features.Auth;

// ── Requests ────────────────────────────────────────────────────────────────

public record LoginRequest(string Login, string Password);

public record VerifyManagerRequest(string Login, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record LogoutRequest(string RefreshToken);

/// <summary>
/// Client must include the current refresh token so the server can revoke it
/// atomically when issuing the new store-scoped token pair.
/// </summary>
public record SwitchStoreRequest(string StoreId, string RefreshToken);

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
    string Type = "tenant",
    bool IsNewAccount = false,
    DateTime? TrialEndsAt = null);

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
    string StoreId,
    SessionDto Session);

// ── Register / Verify ──────────────────────────────────────────────────────────

public record RegisterRequest(string Name, string Email, string Password);

public record RegisterResponse(string Message);

public record VerifyEmailRequest(string Token);

public record ResendVerificationRequest(string Email);

/// <summary>Wraps LoginAsync result to distinguish failure reasons.</summary>
public record LoginOutcome
{
    public LoginResponse? Response { get; init; }
    public string? ErrorCode { get; init; }
    public bool IsSuccess => Response is not null;

    public static LoginOutcome Ok(LoginResponse r) => new() { Response = r };
    public static LoginOutcome Fail(string code) => new() { ErrorCode = code };
}
