using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Use cases for BuildProject aggregate.
///
/// Isolation rules:
///   - All entities fetched/saved through IBuildProjectRepository (tenant-filtered by EF global query).
///   - No direct EF Core dependency in this file.
///   - Financial data is read via IBuildFinancialQueryService (never FinancialMovement entities).
/// </summary>
public class BuildProjectService
{
    private readonly IBuildProjectRepository _projects;
    private readonly ICurrentTenant          _currentTenant;
    private readonly ICurrentUser            _currentUser;

    public BuildProjectService(
        IBuildProjectRepository projects,
        ICurrentTenant          currentTenant,
        ICurrentUser            currentUser)
    {
        _projects      = projects;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<BuildPagedResult<BuildProjectDto>> GetAllAsync(
        BuildProjectStatus? status   = null,
        int                 page     = 1,
        int                 pageSize = 20,
        CancellationToken   ct       = default)
    {
        var total = await _projects.CountAsync(status, ct);
        var items = await _projects.GetAllAsync(status, page, pageSize, ct);
        return new BuildPagedResult<BuildProjectDto>(
            items.Select(MapToDto).ToList(),
            total, page, pageSize);
    }

    public async Task<BuildProjectDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        return MapToDto(project);
    }

    public async Task<BuildProjectDetailsDto> GetDetailsAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdWithDetailsAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        return MapToDetailsDto(project);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<BuildProjectDto> CreateAsync(
        CreateBuildProjectRequest request,
        CancellationToken         ct = default)
    {
        if (!Enum.TryParse<BuildProjectType>(request.Type, ignoreCase: true, out var type))
            throw new DomainException($"Invalid project type: '{request.Type}'.");

        var project = BuildProject.Create(
            tenantId:        _currentTenant.Id,
            createdBy:       _currentUser.UserId,
            name:            request.Name,
            clientName:      request.ClientName,
            type:            type,
            location:        request.Location,
            startDate:       request.StartDate,
            expectedEndDate: request.ExpectedEndDate,
            budgetEstimated: request.BudgetEstimated);

        await _projects.AddAsync(project, ct);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    public async Task<BuildProjectDto> UpdateAsync(
        Guid                      id,
        UpdateBuildProjectRequest request,
        CancellationToken         ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);

        if (!Enum.TryParse<BuildProjectType>(request.Type, ignoreCase: true, out var type))
            throw new DomainException($"Invalid project type: '{request.Type}'.");

        project.UpdateDetails(
            name:            request.Name,
            clientName:      request.ClientName,
            type:            type,
            location:        request.Location,
            startDate:       request.StartDate,
            expectedEndDate: request.ExpectedEndDate,
            budgetEstimated: request.BudgetEstimated,
            budgetApproved:  request.BudgetApproved);

        _projects.Update(project);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<BuildProjectDto> StartAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        project.Start();
        _projects.Update(project);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    public async Task<BuildProjectDto> PauseAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        project.Pause();
        _projects.Update(project);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    public async Task<BuildProjectDto> CompleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        project.Complete();
        _projects.Update(project);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    public async Task<BuildProjectDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("BuildProject", id);
        project.Cancel();
        _projects.Update(project);
        await _projects.SaveChangesAsync(ct);
        return MapToDto(project);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    internal static BuildProjectDto MapToDto(BuildProject p) => new(
        Id:                  p.Id,
        Name:                p.Name,
        ClientName:          p.ClientName,
        Location:            p.Location,
        Status:              p.Status.ToString(),
        Type:                p.Type.ToString(),
        StartDate:           p.StartDate,
        ExpectedEndDate:     p.ExpectedEndDate,
        ActualEndDate:       p.ActualEndDate,
        BudgetEstimated:     p.BudgetEstimated,
        BudgetApproved:      p.BudgetApproved,
        StageCount:          p.Stages.Count,
        CompletedStageCount: p.Stages.Count(s => s.Status == BuildStageStatus.Completed),
        LogCount:            p.DailyLogs.Count,
        CreatedAt:           p.CreatedAt,
        UpdatedAt:           p.UpdatedAt);

    private static BuildProjectDetailsDto MapToDetailsDto(BuildProject p) => new(
        Id:              p.Id,
        Name:            p.Name,
        ClientName:      p.ClientName,
        Location:        p.Location,
        Status:          p.Status.ToString(),
        Type:            p.Type.ToString(),
        StartDate:       p.StartDate,
        ExpectedEndDate: p.ExpectedEndDate,
        ActualEndDate:   p.ActualEndDate,
        BudgetEstimated: p.BudgetEstimated,
        BudgetApproved:  p.BudgetApproved,
        Stages:          p.Stages.OrderBy(s => s.Order)
                                 .Select(BuildStageService.MapToDto)
                                 .ToList(),
        RecentLogs:      p.DailyLogs.OrderByDescending(l => l.Date)
                                    .Take(5)
                                    .Select(BuildDailyLogService.MapToDtoNoPhotos)
                                    .ToList(),
        CreatedAt:       p.CreatedAt,
        UpdatedAt:       p.UpdatedAt);
}
