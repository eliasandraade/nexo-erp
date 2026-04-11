using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Validators.Auth;
using Nexo.Infrastructure.Auth;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly RegistrationService _registration;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IStoreRepository _stores;
    private readonly NexoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public AuthController(
        AuthService authService,
        RegistrationService registration,
        IValidator<LoginRequest> loginValidator,
        ICurrentUser currentUser,
        IUserRepository users,
        ITenantRepository tenants,
        IStoreRepository stores,
        NexoDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt)
    {
        _authService = authService;
        _registration = registration;
        _loginValidator = loginValidator;
        _currentUser = currentUser;
        _users = users;
        _tenants = tenants;
        _stores = stores;
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    /// <summary>Authenticate and receive access + refresh tokens.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var outcome = await _authService.LoginAsync(request, ct);

        if (outcome.IsSuccess)
            return Ok(outcome.Response!);

        if (outcome.ErrorCode == "email_not_verified")
            return Unauthorized(new { error = "E-mail não verificado. Verifique sua caixa de entrada.", code = "email_not_verified" });

        if (outcome.ErrorCode == "account_blocked")
            return Unauthorized(new { error = "Conta bloqueada. Entre em contato com o suporte." });

        // Fallback: check platform users (login field accepted as email)
        var platformUser = await _db.PlatformUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Login.Trim().ToLowerInvariant(), ct);

        if (platformUser is not null && _hasher.Verify(request.Password, platformUser.PasswordHash))
        {
            var token = _jwt.GeneratePlatformToken(platformUser);
            var session = new SessionDto(
                UserId:        platformUser.Id.ToString(),
                TenantId:      "",
                Name:          platformUser.Email,
                Role:          platformUser.Role,
                Login:         platformUser.Email,
                Email:         platformUser.Email,
                ActiveModules: new List<string>(),
                StoreId:       null,
                StoreIds:      new List<string>(),
                CompanyName:   "NexoERP",
                Type:          "platform");

            return Ok(new LoginResponse(token.AccessToken, "", token.ExpiresAt, session));
        }

        return Unauthorized(new { error = "Invalid login or password." });
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
        // Platform token path — no tenant context
        var tokenType = User.FindFirstValue("type");
        if (tokenType == "platform")
        {
            var platformUserId = User.FindFirstValue("platformUserId");
            if (!Guid.TryParse(platformUserId, out var pid)) return Unauthorized();
            var pu = await _db.PlatformUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pid, ct);
            if (pu is null) return Unauthorized();
            return Ok(new SessionDto(
                UserId: pu.Id.ToString(), TenantId: "", Name: pu.Email, Role: pu.Role,
                Login: pu.Email, Email: pu.Email, ActiveModules: new List<string>(),
                StoreId: null, StoreIds: new List<string>(), CompanyName: "NexoERP", Type: "platform"));
        }

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null) return Unauthorized();

        var tenant = await _tenants.GetByIdAsync(_currentUser.TenantId, ct);
        var activeModules = await _tenants.GetActiveModuleKeysAsync(_currentUser.TenantId, ct);
        var stores = await _stores.GetByTenantIdAsync(_currentUser.TenantId, ct);

        return Ok(new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      user.TenantId.ToString(),
            Name:          user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant(),
            Login:         user.Login,
            Email:         user.Email,
            ActiveModules: activeModules.ToList(),
            StoreId:       _currentUser.StoreId == Guid.Empty ? null : _currentUser.StoreId.ToString(),
            StoreIds:      stores.Select(s => s.Id.ToString()).ToList(),
            CompanyName:   tenant?.TradeName ?? tenant?.CompanyName ?? ""));
    }

    /// <summary>
    /// Switch the active store context. Issues a new token pair with the requested storeId.
    /// The old refresh token is revoked; a new one is issued.
    /// </summary>
    [HttpPost("switch-store")]
    [Authorize]
    public async Task<ActionResult<SwitchStoreResponse>> SwitchStore(
        [FromBody] SwitchStoreRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.StoreId, out var storeId))
            return BadRequest(new { error = "Invalid storeId format." });

        var refreshToken = HttpContext.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", string.Empty);

        var result = await _authService.SwitchStoreAsync(
            _currentUser.UserId,
            _currentUser.TenantId,
            storeId,
            refreshToken ?? string.Empty,
            ct);

        if (result is null)
            return Forbid();

        return Ok(result);
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
    /// Self-registration: creates tenant + store + user + subscription + settings atomically.
    /// Sends a verification email. User must verify before logging in.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
            return BadRequest(new { error = "Informe seu nome completo." });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Informe um e-mail válido." });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new { error = "A senha deve ter no mínimo 6 caracteres." });

        var errorCode = await _registration.RegisterAsync(request, ct);
        if (errorCode == "email_already_registered")
            return Conflict(new { error = "Este e-mail já está cadastrado.", code = errorCode });
        if (errorCode is not null)
            return BadRequest(new { error = "Erro ao criar conta.", code = errorCode });

        return Ok(new { message = "verification_email_sent" });
    }

    /// <summary>
    /// Email verification: validates the one-time token and activates the user.
    /// Returns access + refresh tokens for auto-login.
    /// </summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyEmail([FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Token inválido." });

        var result = await _registration.VerifyEmailAsync(token, ct);
        if (result is null)
            return BadRequest(new { error = "Link inválido ou expirado." });

        return Ok(result);
    }

    /// <summary>
    /// Resends the verification email. Silently succeeds even if email not found
    /// to prevent email enumeration attacks.
    /// </summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Informe o e-mail." });
        await _registration.ResendVerificationAsync(request.Email.Trim().ToLowerInvariant(), ct);
        return Ok(new { message = "Email reenviado se o endereço estiver cadastrado." });
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
