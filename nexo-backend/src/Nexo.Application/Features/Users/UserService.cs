using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Users;

public class UserService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    private readonly IUserSessionRevoker? _sessions;
    private readonly IUserInviter? _inviter;

    public UserService(
        IUserRepository users,
        IPasswordHasher hasher,
        IAuditWriter audit,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IUserSessionRevoker? sessions = null,
        IUserInviter? inviter = null)
    {
        _inviter = inviter;
        _sessions = sessions;
        _users = users;
        _hasher = hasher;
        _audit = audit;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await _users.GetAllAsync(ct);
        return users.Select(MapToDto).ToList();
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("User", id);
        return MapToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (await _users.LoginExistsAsync(request.Login, ct))
            throw new ConflictException($"Login '{request.Login}' is already in use.");

        if (!string.IsNullOrWhiteSpace(request.Email) && await _users.EmailExistsAsync(request.Email, ct))
            throw new ConflictException($"Email '{request.Email}' is already in use.");

        var role = ParseRole(request.Role);
        var invite = request.SendInvite;
        if (invite && (string.IsNullOrWhiteSpace(request.Email) || _inviter is null))
            throw new DomainException("Informe o e-mail para enviar o convite.");
        if (!invite && (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6))
            throw new DomainException("A senha deve ter no mínimo 6 caracteres.");
        // invited: nobody knows this password — the person sets one through the link
        var hash = _hasher.Hash(invite ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) : request.Password);

        var user = User.Create(
            tenantId:               _currentTenant.Id,
            fullName:               request.FullName,
            email:                  request.Email ?? string.Empty,
            login:                  request.Login,
            passwordHash:           hash,
            role:                   role,
            phone:                  request.Phone,
            notes:                  request.Notes,
            requirePasswordChange:  request.RequirePasswordChange);

        _audit.Stage(
            actionType:  AuditActions.UserCreated,
            severity:    AuditSeverity.Info,
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"User '{user.Login}' created with role {user.Role}.",
            tenantId:    _currentTenant.Id,
            actorId:     _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            actorName:   _currentUser.IsAuthenticated ? _currentUser.Name : "system");

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);
        if (invite) await _inviter!.SendInviteAsync(user.Id, ct);

        return MapToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("User", id);

        var role = ParseRole(request.Role);

        user.UpdateProfile(
            fullName: request.FullName,
            email:    request.Email ?? string.Empty,
            phone:    request.Phone,
            role:     role,
            notes:    request.Notes);

        if (request.Status is not null)
            user.SetStatus(ParseStatus(request.Status));

        _audit.Stage(
            actionType:  AuditActions.UserUpdated,
            severity:    AuditSeverity.Info,
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"User '{user.Login}' updated.",
            tenantId:    _currentTenant.Id,
            actorId:     _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            actorName:   _currentUser.IsAuthenticated ? _currentUser.Name : "system");

        await _users.SaveChangesAsync(ct);
        return MapToDto(user);
    }

    public async Task ChangePasswordAsync(
        Guid id,
        ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("User", id);

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ForbiddenException("Current password is incorrect.");

        user.ChangePasswordHash(_hasher.Hash(request.NewPassword));
        // a changed password ends every session (a stolen session must not survive it)
        user.BumpSecurityStamp();
        if (_sessions is not null) await _sessions.RevokeAllAsync(user.Id, ct);

        _audit.Stage(
            actionType:  AuditActions.UserPasswordChanged,
            severity:    AuditSeverity.Warning,
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"User '{user.Login}' changed their own password.",
            tenantId:    _currentTenant.Id,
            actorId:     _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            actorName:   _currentUser.IsAuthenticated ? _currentUser.Name : "system");

        await _users.SaveChangesAsync(ct);
        if (_sessions is not null) await _sessions.ForgetStampAsync(user.Id, ct);
    }

    public async Task AdminChangePasswordAsync(
        Guid id,
        AdminChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("User", id);

        user.ChangePasswordHash(_hasher.Hash(request.NewPassword), clearRequireChange: false);
        // the manager knows this password: the person replaces it on the next sign-in
        user.RequirePasswordChangeOnNextLogin();
        user.BumpSecurityStamp();
        if (_sessions is not null) await _sessions.RevokeAllAsync(user.Id, ct);

        _audit.Stage(
            actionType:  AuditActions.UserPasswordChanged,
            severity:    AuditSeverity.Warning,
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"Admin reset password for user '{user.Login}'.",
            tenantId:    _currentTenant.Id,
            actorId:     _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            actorName:   _currentUser.IsAuthenticated ? _currentUser.Name : "system");

        await _users.SaveChangesAsync(ct);
        if (_sessions is not null) await _sessions.ForgetStampAsync(user.Id, ct);
    }

    public async Task<ValidateManagerResponse> ValidateManagerAsync(
        ValidateManagerRequest request,
        CancellationToken ct = default)
    {
        var users = await _users.GetAllAsync(ct);
        var user = users.FirstOrDefault(u => u.Login == request.Login);

        if (user is null)
            return new ValidateManagerResponse(false, "Usuário não encontrado.", null, null);

        if (user.Status != UserStatus.Active)
            return new ValidateManagerResponse(false, "Usuário inativo ou bloqueado.", null, null);

        if (user.Role != UserRole.Gerente && user.Role != UserRole.Diretoria)
            return new ValidateManagerResponse(false, "Usuário não possui autorização gerencial.", null, null);

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            _audit.Stage(
                actionType:  AuditActions.ManagerAuthorization,
                severity:    AuditSeverity.Critical,
                entityType:  "User",
                entityId:    user.Id.ToString(),
                description: $"Failed manager authorization for '{request.Login}'.",
                tenantId:    _currentTenant.Id,
                actorId:     user.Id,
                actorName:   user.FullName);
            await _users.SaveChangesAsync(ct);
            return new ValidateManagerResponse(false, "Senha incorreta.", null, null);
        }

        _audit.Stage(
            actionType:  AuditActions.ManagerAuthorization,
            severity:    AuditSeverity.Warning,
            entityType:  "User",
            entityId:    user.Id.ToString(),
            description: $"Manager authorization granted to '{user.FullName}'.",
            tenantId:    _currentTenant.Id,
            actorId:     user.Id,
            actorName:   user.FullName);
        await _users.SaveChangesAsync(ct);

        return new ValidateManagerResponse(
            true, null, user.FullName, user.Role.ToString().ToLower());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UserRole ParseRole(string role) =>
        role.ToLowerInvariant() switch
        {
            "diretoria"  => UserRole.Diretoria,
            "gerente"    => UserRole.Gerente,
            "vendedor"   => UserRole.Vendedor,
            "estoquista" => UserRole.Estoquista,
            "cozinha"    => UserRole.Cozinha,
            _ => throw new DomainException($"Unknown role '{role}'."),
        };

    private static UserStatus ParseStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "active"   => UserStatus.Active,
            "inactive" => UserStatus.Inactive,
            "blocked"  => UserStatus.Blocked,
            _ => throw new DomainException($"Unknown status '{status}'."),
        };

    private static UserDto MapToDto(User u) => new(
        Id:                    u.Id.ToString(),
        TenantId:              u.TenantId.ToString(),
        FullName:              u.FullName,
        Email:                 u.Email,
        Login:                 u.Login,
        Phone:                 u.Phone,
        Role:                  u.Role.ToString().ToLowerInvariant(),
        Status:                u.Status.ToString().ToLowerInvariant(),
        RequirePasswordChange: u.RequirePasswordChange,
        Notes:                 u.Notes,
        LastAccessAt:          u.LastAccessAt,
        PasswordChangedAt:     u.PasswordChangedAt,
        CreatedAt:             u.CreatedAt,
        UpdatedAt:             u.UpdatedAt);
}
