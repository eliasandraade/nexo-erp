using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Validators.Auth;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;

    public AuthController(
        AuthService authService,
        IValidator<LoginRequest> loginValidator,
        ICurrentUser currentUser,
        IUserRepository users,
        ITenantRepository tenants)
    {
        _authService = authService;
        _loginValidator = loginValidator;
        _currentUser = currentUser;
        _users = users;
        _tenants = tenants;
    }

    /// <summary>Authenticate and receive access + refresh tokens.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var result = await _authService.LoginAsync(request, ct);

        if (result is null)
            return Unauthorized(new { error = "Invalid login or password." });

        return Ok(result);
    }

    /// <summary>
    /// Rotate a refresh token. Returns a new access token (and keeps the same refresh token
    /// window until expiry — or issues a new one via token rotation).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { error = "Refresh token is required." });

        var result = await _authService.RefreshAsync(request, ct);

        if (result is null)
            return Unauthorized(new { error = "Invalid or expired refresh token." });

        return Ok(result);
    }

    /// <summary>Returns the current user's session info from the database.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessionDto>> Me(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null) return Unauthorized();

        var activeModules = await _tenants.GetActiveModuleKeysAsync(_currentUser.TenantId, ct);

        return Ok(new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      user.TenantId.ToString(),
            Name:          user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant(),
            Login:         user.Login,
            Email:         user.Email,
            ActiveModules: activeModules.ToList()));
    }

    /// <summary>
    /// Revokes the provided refresh token. Safe to call multiple times.
    /// The access token will expire naturally within its 15-minute window.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        await _authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>
    /// Manager challenge — validates manager credentials without creating a session.
    /// Used before sensitive operations (sale cancellation, high discounts, etc).
    /// </summary>
    [HttpPost("verify-manager")]
    [Authorize]
    public async Task<ActionResult<VerifyManagerResponse>> VerifyManager(
        [FromBody] VerifyManagerRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Login and password are required." });

        var result = await _authService.VerifyManagerAsync(request, ct);
        return Ok(result);
    }
}
