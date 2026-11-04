using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// The company side of the client portal (tenant-scoped, Management): company slug and branding, each
/// project's portal (slug, access, password, branding, visibility, publication), what of the daily log is
/// shown to the client, and the client updates. Every change is audited; the password and its hash never
/// leave this service.
/// </summary>
public sealed class BuildPortalAdminService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly BuildPortalMedia _media;
    private readonly BuildAuditTrail _audit;
    private readonly IPrivateFileUrls _privateUrls;

    public BuildPortalAdminService(NexoDbContext db, ICurrentTenant tenant, IPasswordHasher hasher,
        BuildPortalMedia media, BuildAuditTrail audit, IPrivateFileUrls privateUrls)
    {
        _privateUrls = privateUrls;
        _db = db;
        _tenant = tenant;
        _hasher = hasher;
        _media = media;
        _audit = audit;
    }

    // ── Company ───────────────────────────────────────────────────────────────

    public async Task<BuildPortalCompanyDto> GetCompanyAsync(CancellationToken ct)
        => MapCompany(await _db.BldPortalCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == _tenant.Id, ct));

    public async Task<BuildPortalCompanyDto> UpdateCompanyAsync(UpdateBuildPortalCompanyRequest r, CancellationToken ct)
    {
        EnsureBrandingKey(r.LogoKey);
        var company = await _db.BldPortalCompanies.FirstOrDefaultAsync(c => c.TenantId == _tenant.Id, ct);
        var before = company?.Slug;
        if (company is null)
        {
            company = BuildPortalCompany.Create(_tenant.Id, r.Slug, r.DisplayName, r.LogoKey, r.ContactEmail, r.ContactPhone);
            _db.BldPortalCompanies.Add(company);
        }
        else
        {
            company.Update(r.Slug, r.DisplayName, r.LogoKey, r.ContactEmail, r.ContactPhone);
        }
        _audit.Record(AuditActions.BuildPortalCompanyUpdated, "PortalCompany", company.Id,
            $"Empresa no portal do cliente: {company.DisplayName} ({company.Slug}).",
            new { slugBefore = before, slug = company.Slug });
        await SaveUniqueAsync("Este endereço de empresa já está em uso. Escolha outro.", ct);
        return MapCompany(company);
    }

    // ── Project portal ────────────────────────────────────────────────────────

    public async Task<BuildProjectPortalDto> GetPortalAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var portal = await _db.BldProjectPortals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.ProjectId == projectId, ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    public async Task<BuildProjectPortalDto> UpdatePortalAsync(Guid projectId, UpdateBuildProjectPortalRequest r, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        EnsureBrandingKey(r.LogoKey);
        EnsureBrandingKey(r.CoverKey);
        var access = string.IsNullOrWhiteSpace(r.Access)
            ? BuildPortalAccess.Public
            : Enum.TryParse<BuildPortalAccess>(r.Access, ignoreCase: true, out var a) && Enum.IsDefined(a)
                ? a
                : throw new DomainException("Acesso inválido: use Public ou Password.");

        var portal = await GetOrCreatePortalAsync(projectId, ct);
        portal.Configure(r.Slug, access, r.Title, r.WelcomeMessage, r.LogoKey, r.CoverKey, r.PrimaryColor, r.AccentColor,
            r.ContactName, r.ContactPhone, r.ContactEmail, r.ContactWhatsApp, r.Visibility ?? BuildPortalVisibility.Defaults);
        _audit.Record(AuditActions.BuildPortalUpdated, "ProjectPortal", portal.Id,
            "Configuração do portal do cliente alterada.",
            new { projectId, portal.Slug, access = portal.Access.ToString(), visibility = portal.Visibility });
        await SaveUniqueAsync("Outra obra desta empresa já usa este endereço. Escolha outro.", ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    public async Task<BuildProjectPortalDto> SetPasswordAsync(Guid projectId, string password, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new DomainException("A senha do portal precisa ter pelo menos 8 caracteres.");
        var portal = await GetOrCreatePortalAsync(projectId, ct);
        portal.SetPasswordHash(_hasher.Hash(password));
        _audit.Record(AuditActions.BuildPortalPasswordSet, "ProjectPortal", portal.Id,
            "Senha do portal do cliente definida — sessões abertas encerradas.", new { projectId, portal.PasswordVersion },
            AuditSeverity.Warning);
        await _db.SaveChangesAsync(ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    public async Task<BuildProjectPortalDto> RemovePasswordAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var portal = await _db.BldProjectPortals.FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.ProjectId == projectId, ct)
            ?? throw new NotFoundException("O portal desta obra ainda não foi configurado.");
        portal.ClearPassword();
        _audit.Record(AuditActions.BuildPortalPasswordRemoved, "ProjectPortal", portal.Id,
            "Senha do portal do cliente removida — sessões abertas encerradas.", new { projectId, portal.PasswordVersion },
            AuditSeverity.Warning);
        await _db.SaveChangesAsync(ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    public async Task<BuildProjectPortalDto> PublishAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var portal = await _db.BldProjectPortals.FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.ProjectId == projectId, ct)
            ?? throw new DomainException("Configure o portal desta obra (endereço e o que mostrar) antes de publicar.",
                "portal_not_configured");
        if (!await _db.BldPortalCompanies.AnyAsync(c => c.TenantId == _tenant.Id, ct))
            throw new DomainException("Defina o endereço da empresa no portal do cliente antes de publicar.",
                "portal_company_missing");
        portal.Publish(DateTime.UtcNow);
        _audit.Record(AuditActions.BuildPortalPublished, "ProjectPortal", portal.Id,
            $"Portal do cliente publicado ({portal.Access}).", new { projectId, portal.Slug, access = portal.Access.ToString() },
            AuditSeverity.Warning);
        await _db.SaveChangesAsync(ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    public async Task<BuildProjectPortalDto> UnpublishAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var portal = await _db.BldProjectPortals.FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.ProjectId == projectId, ct)
            ?? throw new NotFoundException("O portal desta obra ainda não foi configurado.");
        portal.Unpublish();
        _audit.Record(AuditActions.BuildPortalUnpublished, "ProjectPortal", portal.Id,
            "Portal do cliente despublicado.", new { projectId, portal.Slug });
        await _db.SaveChangesAsync(ct);
        return await MapPortalAsync(projectId, portal, ct);
    }

    // ── What of the daily log the client sees ─────────────────────────────────

    public async Task<BuildDailyLogDto> SetDailyLogVisibilityAsync(Guid logId, SetDailyLogClientVisibilityRequest r, CancellationToken ct)
    {
        var log = await _db.BldDailyLogs.Include(l => l.Photos)
            .FirstOrDefaultAsync(l => l.TenantId == _tenant.Id && l.Id == logId, ct)
            ?? throw new NotFoundException("BuildDailyLog", logId);
        log.SetClientVisibility(r.ClientVisible, r.ClientSummary);
        _audit.Record(AuditActions.BuildDailyLogClientVisibility, "DailyLog", log.Id,
            r.ClientVisible ? $"Diário de {log.Date:dd/MM/yyyy} visível ao cliente." : $"Diário de {log.Date:dd/MM/yyyy} oculto do cliente.",
            new { log.ProjectId, r.ClientVisible });
        await _db.SaveChangesAsync(ct);
        return BuildDailyLogService.MapToDto(log, log.Photos, _privateUrls);
    }

    public async Task<BuildDailyLogPhotoDto> SetPhotoVisibilityAsync(Guid photoId, bool visible, CancellationToken ct)
    {
        var photo = await _db.BldDailyLogPhotos.FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.Id == photoId, ct)
            ?? throw new NotFoundException("BuildDailyLogPhoto", photoId);
        photo.SetClientVisible(visible);
        _audit.Record(AuditActions.BuildPhotoClientVisibility, "DailyLogPhoto", photo.Id,
            visible ? "Foto visível ao cliente." : "Foto oculta do cliente.", new { photo.DailyLogId, visible });
        await _db.SaveChangesAsync(ct);
        return new BuildDailyLogPhotoDto(photo.Id, photo.DailyLogId, photo.StorageKey, _privateUrls.For(photo.StorageKey), photo.Caption, photo.CreatedAt,
            photo.ClientVisible);
    }

    // ── Client updates ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildClientUpdateDto>> ListUpdatesAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var list = await _db.BldClientUpdates.AsNoTracking()
            .Where(u => u.TenantId == _tenant.Id && u.ProjectId == projectId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);
        return list.Select(MapUpdate).ToList();
    }

    public async Task<BuildClientUpdateDto> CreateUpdateAsync(Guid projectId, SaveBuildClientUpdateRequest r, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var u = BuildClientUpdate.Create(_tenant.Id, projectId, ParseKind(r.Kind), r.Title, r.Body,
            BuildClientUpdateSource.Manual, _audit.UserId, _audit.UserName);
        _db.BldClientUpdates.Add(u);
        _audit.Record(AuditActions.BuildClientUpdateCreated, "ClientUpdate", u.Id, $"Atualização ao cliente criada: {u.Title}.",
            new { projectId, kind = u.Kind.ToString() });
        await _db.SaveChangesAsync(ct);
        return MapUpdate(u);
    }

    public async Task<BuildClientUpdateDto> EditUpdateAsync(Guid updateId, SaveBuildClientUpdateRequest r, CancellationToken ct)
    {
        var u = await LoadUpdateAsync(updateId, ct);
        u.Edit(ParseKind(r.Kind), r.Title, r.Body);
        _audit.Record(AuditActions.BuildClientUpdateEdited, "ClientUpdate", u.Id, $"Atualização ao cliente editada: {u.Title}.",
            new { u.ProjectId, status = u.Status.ToString() });
        await _db.SaveChangesAsync(ct);
        return MapUpdate(u);
    }

    public async Task<BuildClientUpdateDto> PublishUpdateAsync(Guid updateId, CancellationToken ct)
    {
        var u = await LoadUpdateAsync(updateId, ct);
        u.Publish(DateTime.UtcNow);
        _audit.Record(AuditActions.BuildClientUpdatePublished, "ClientUpdate", u.Id, $"Atualização ao cliente publicada: {u.Title}.",
            new { u.ProjectId });
        await _db.SaveChangesAsync(ct);
        return MapUpdate(u);
    }

    public async Task<BuildClientUpdateDto> ArchiveUpdateAsync(Guid updateId, CancellationToken ct)
    {
        var u = await LoadUpdateAsync(updateId, ct);
        u.Archive();
        _audit.Record(AuditActions.BuildClientUpdateArchived, "ClientUpdate", u.Id, $"Atualização ao cliente arquivada: {u.Title}.",
            new { u.ProjectId });
        await _db.SaveChangesAsync(ct);
        return MapUpdate(u);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BuildClientUpdate> LoadUpdateAsync(Guid id, CancellationToken ct)
        => await _db.BldClientUpdates.FirstOrDefaultAsync(u => u.TenantId == _tenant.Id && u.Id == id, ct)
           ?? throw new NotFoundException("BuildClientUpdate", id);

    private static BuildClientUpdateKind ParseKind(string? kind)
        => string.IsNullOrWhiteSpace(kind) ? BuildClientUpdateKind.Update
            : Enum.TryParse<BuildClientUpdateKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k) ? k
            : throw new DomainException("Tipo inválido: use Update, Milestone ou WeeklyDigest.");

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private async Task<BuildProjectPortal> GetOrCreatePortalAsync(Guid projectId, CancellationToken ct)
    {
        var portal = await _db.BldProjectPortals.FirstOrDefaultAsync(p => p.TenantId == _tenant.Id && p.ProjectId == projectId, ct);
        if (portal is not null) return portal;
        portal = BuildProjectPortal.Create(_tenant.Id, projectId);
        _db.BldProjectPortals.Add(portal);
        return portal;
    }

    private void EnsureBrandingKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!BuildPortalMedia.IsBrandingKeyOf(key.Trim(), _tenant.Id))
            throw new DomainException("Imagem inválida: envie pelo upload do portal da obra (logo ou capa) e use a chave devolvida.",
                "portal_media_invalid");
    }

    private async Task SaveUniqueAsync(string conflictMessage, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(conflictMessage);
        }
    }

    private BuildPortalCompanyDto MapCompany(BuildPortalCompany? c) => c is null
        ? new BuildPortalCompanyDto(false, null, null, null, null, null, null)
        : new BuildPortalCompanyDto(true, c.Slug, c.DisplayName, c.LogoKey, _media.BrandingUrl(c.LogoKey, c.TenantId),
            c.ContactEmail, c.ContactPhone);

    private async Task<BuildProjectPortalDto> MapPortalAsync(Guid projectId, BuildProjectPortal? p, CancellationToken ct)
    {
        var companySlug = await _db.BldPortalCompanies.AsNoTracking()
            .Where(c => c.TenantId == _tenant.Id).Select(c => c.Slug).FirstOrDefaultAsync(ct);
        if (p is null)
            return new BuildProjectPortalDto(projectId, false, null, companySlug, null, false, null,
                nameof(BuildPortalAccess.Public), false, false, null, null, null, null, null, null, null, null,
                null, null, null, null, BuildPortalVisibility.Defaults);
        var path = companySlug is not null && p.Slug is not null ? $"/{companySlug}/{p.Slug}" : null;
        return new BuildProjectPortalDto(
            projectId, true, p.Slug, companySlug, path, p.IsPublished, p.PublishedAt, p.Access.ToString(), p.HasPassword,
            p.IsLocked(DateTime.UtcNow), p.Title, p.WelcomeMessage, p.LogoKey, _media.BrandingUrl(p.LogoKey, p.TenantId),
            p.CoverKey, _media.BrandingUrl(p.CoverKey, p.TenantId), p.PrimaryColor, p.AccentColor, p.ContactName,
            p.ContactPhone, p.ContactEmail, p.ContactWhatsApp, p.Visibility);
    }

    private static BuildClientUpdateDto MapUpdate(BuildClientUpdate u) => new(
        u.Id, u.ProjectId, u.Kind.ToString(), u.Title, u.Body, u.Status.ToString(), u.PublishedAt, u.Source.ToString(),
        u.CreatedByName, u.CreatedAt, u.UpdatedAt);
}
