using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Users;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly IValidator<CreateUserRequest> _createValidator;
    private readonly IValidator<UpdateUserRequest> _updateValidator;

    public UsersController(
        UserService userService,
        IValidator<CreateUserRequest> createValidator,
        IValidator<UpdateUserRequest> updateValidator)
    {
        _userService = userService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Returns all users. Accessible to Gerente and Diretoria.</summary>
    [HttpGet]
    [Authorize(Roles = "Gerente,Diretoria")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var users = await _userService.GetAllAsync(ct);
        return Ok(users);
    }

    /// <summary>Returns a single user by UUID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(id, ct);
        return Ok(user);
    }

    /// <summary>Creates a new user. Diretoria only.</summary>
    [HttpPost]
    [Authorize(Roles = "Diretoria")]
    public async Task<ActionResult<UserDto>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var user = await _userService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>Updates a user's profile and role. Diretoria only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Diretoria")]
    public async Task<ActionResult<UserDto>> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var user = await _userService.UpdateAsync(id, request, ct);
        return Ok(user);
    }

    /// <summary>
    /// Self-service password change. User must provide their current password.
    /// </summary>
    [HttpPost("{id:guid}/change-password")]
    public async Task<IActionResult> ChangePassword(
        Guid id,
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return BadRequest(new { error = "Current password is required." });
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { error = "New password must be at least 6 characters." });

        await _userService.ChangePasswordAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Admin password reset — does not require the current password.
    /// Diretoria only.
    /// </summary>
    [HttpPost("{id:guid}/admin-reset-password")]
    [Authorize(Roles = "Diretoria")]
    public async Task<IActionResult> AdminResetPassword(
        Guid id,
        [FromBody] AdminChangePasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { error = "New password must be at least 6 characters." });

        await _userService.AdminChangePasswordAsync(id, request, ct);
        return NoContent();
    }
}
