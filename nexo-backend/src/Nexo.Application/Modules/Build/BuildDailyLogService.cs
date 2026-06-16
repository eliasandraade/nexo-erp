using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Use cases for BuildDailyLog and BuildDailyLogPhoto.
///
/// One daily log per project per date — enforced at DB (unique index) and guarded
/// at the application layer before attempting an insert.
///
/// Photos carry a StorageKey only — binary content is managed by a separate blob
/// storage service (IAttachmentStorage pattern from the Interpreter module).
/// </summary>
public class BuildDailyLogService
{
    private readonly IBuildProjectRepository       _projects;
    private readonly IBuildDailyLogRepository      _logs;
    private readonly IBuildDailyLogPhotoRepository _photos;
    private readonly ICurrentTenant                _currentTenant;
    private readonly ICurrentUser                  _currentUser;

    public BuildDailyLogService(
        IBuildProjectRepository       projects,
        IBuildDailyLogRepository      logs,
        IBuildDailyLogPhotoRepository photos,
        ICurrentTenant                currentTenant,
        ICurrentUser                  currentUser)
    {
        _projects      = projects;
        _logs          = logs;
        _photos        = photos;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<BuildPagedResult<BuildDailyLogDto>> GetByProjectAsync(
        Guid              projectId,
        DateOnly?         from     = null,
        DateOnly?         to       = null,
        int               page     = 1,
        int               pageSize = 20,
        CancellationToken ct       = default)
    {
        _ = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        var list = await _logs.GetByProjectAsync(projectId, from, to, page, pageSize, ct);

        var dtos = new List<BuildDailyLogDto>();
        foreach (var log in list)
        {
            var logPhotos = await _photos.GetByLogAsync(log.Id, ct);
            dtos.Add(MapToDto(log, logPhotos));
        }

        return new BuildPagedResult<BuildDailyLogDto>(dtos, dtos.Count, page, pageSize);
    }

    public async Task<BuildDailyLogDto> GetByIdAsync(Guid logId, CancellationToken ct = default)
    {
        var log = await _logs.GetByIdWithPhotosAsync(logId, ct)
            ?? throw new NotFoundException("BuildDailyLog", logId);
        return MapToDto(log, log.Photos);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<BuildDailyLogDto> CreateAsync(
        Guid                  projectId,
        CreateDailyLogRequest request,
        CancellationToken     ct = default)
    {
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        if (!project.IsActive)
            throw new DomainException($"Cannot create a daily log for a project in status '{project.Status}'.");

        var exists = await _logs.ExistsForDateAsync(projectId, request.Date, ct);
        if (exists)
            throw new DomainException($"A daily log already exists for this project on {request.Date:yyyy-MM-dd}.");

        var log = BuildDailyLog.Create(
            tenantId:       _currentTenant.Id,
            projectId:      projectId,
            date:           request.Date,
            createdBy:      _currentUser.UserId,
            notes:          request.Notes,
            weatherSummary: request.WeatherSummary);

        await _logs.AddAsync(log, ct);
        await _logs.SaveChangesAsync(ct);
        return MapToDto(log, []);
    }

    public async Task<BuildDailyLogDto> UpdateAsync(
        Guid                  logId,
        UpdateDailyLogRequest request,
        CancellationToken     ct = default)
    {
        var log = await _logs.GetByIdWithPhotosAsync(logId, ct)
            ?? throw new NotFoundException("BuildDailyLog", logId);

        log.Update(request.Notes, request.WeatherSummary);
        _logs.Update(log);
        await _logs.SaveChangesAsync(ct);
        return MapToDto(log, log.Photos);
    }

    public async Task<BuildDailyLogDto> AddPhotoAsync(
        Guid                   logId,
        AddDailyLogPhotoRequest request,
        CancellationToken      ct = default)
    {
        var log = await _logs.GetByIdWithPhotosAsync(logId, ct)
            ?? throw new NotFoundException("BuildDailyLog", logId);

        if (string.IsNullOrWhiteSpace(request.StorageKey))
            throw new DomainException("StorageKey is required.");

        var photo = BuildDailyLogPhoto.Create(
            tenantId:   _currentTenant.Id,
            dailyLogId: logId,
            storageKey: request.StorageKey,
            url:        request.Url,
            caption:    request.Caption);

        await _photos.AddAsync(photo, ct);
        await _photos.SaveChangesAsync(ct);

        var allPhotos = await _photos.GetByLogAsync(logId, ct);
        return MapToDto(log, allPhotos);
    }

    public async Task<BuildDailyLogDto> RemovePhotoAsync(
        Guid              photoId,
        CancellationToken ct = default)
    {
        var photo = await _photos.GetByIdAsync(photoId, ct)
            ?? throw new NotFoundException("BuildDailyLogPhoto", photoId);

        var log = await _logs.GetByIdWithPhotosAsync(photo.DailyLogId, ct)
            ?? throw new NotFoundException("BuildDailyLog", photo.DailyLogId);

        _photos.Remove(photo);
        await _photos.SaveChangesAsync(ct);

        var remaining = await _photos.GetByLogAsync(log.Id, ct);
        return MapToDto(log, remaining);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    internal static BuildDailyLogDto MapToDto(BuildDailyLog log, IEnumerable<BuildDailyLogPhoto> photos)
    {
        var photoList = photos.Select(MapPhotoToDto).ToList();
        return new BuildDailyLogDto(
            Id:             log.Id,
            ProjectId:      log.ProjectId,
            Date:           log.Date,
            WeatherSummary: log.WeatherSummary,
            Notes:          log.Notes,
            PhotoCount:     photoList.Count,
            Photos:         photoList,
            CreatedAt:      log.CreatedAt,
            UpdatedAt:      log.UpdatedAt);
    }

    /// <summary>
    /// Returns a DTO using the already-loaded Photos navigation.
    /// Used for lightweight list views (e.g. project detail summary).
    /// </summary>
    internal static BuildDailyLogDto MapToDtoNoPhotos(BuildDailyLog log) =>
        MapToDto(log, log.Photos);

    private static BuildDailyLogPhotoDto MapPhotoToDto(BuildDailyLogPhoto p) => new(
        Id:         p.Id,
        DailyLogId: p.DailyLogId,
        StorageKey: p.StorageKey,
        Url:        p.Url,
        Caption:    p.Caption,
        CreatedAt:  p.CreatedAt);
}
