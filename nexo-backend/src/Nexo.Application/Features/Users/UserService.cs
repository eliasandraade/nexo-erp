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

    public UserService(
        IUserRepository users,
        IPasswordHasher hasher,
        IAuditWriter audit,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
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
        var hash = _hasher.Hash(request.Password);

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
    }

    public async Task AdminChangePasswordAsync(
        Guid id,
        AdminChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("User", id);

        user.ChangePasswordHash(_hasher.Hash(request.NewPassword), clearRequireChange: true);

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
