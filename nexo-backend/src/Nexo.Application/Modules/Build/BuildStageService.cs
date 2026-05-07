using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Use cases for BuildStage.
/// Stages belong to a BuildProject — every mutation validates the parent project
/// exists and is not terminal before proceeding.
/// </summary>
public class BuildStageService
{
    private readonly IBuildProjectRepository _projects;
    private readonly IBuildStageRepository   _stages;
    private readonly ICurrentTenant          _currentTenant;

    public BuildStageService(
        IBuildProjectRepository projects,
        IBuildStageRepository   stages,
        ICurrentTenant          currentTenant)
    {
        _projects      = projects;
        _stages        = stages;
        _currentTenant = currentTenant;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildStageDto>> GetByProjectAsync(
        Guid              projectId,
        CancellationToken ct = default)
    {
        _ = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        var stages = await _stages.GetByProjectAsync(projectId, ct);
        return stages.OrderBy(s => s.Order).Select(MapToDto).ToList();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<BuildStageDto> CreateAsync(
        Guid                    projectId,
        CreateBuildStageRequest request,
        CancellationToken       ct = default)
    {
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        if (!project.IsActive)
            throw new DomainException($"Cannot add a stage to a project in status '{project.Status}'.");

        var maxOrder = await _stages.GetMaxOrderAsync(projectId, ct);
        var stage    = BuildStage.Create(
            tenantId:    _currentTenant.Id,
            projectId:   projectId,
            name:        request.Name,
            order:       maxOrder + 1,
            description: request.Description,
            plannedStart: request.PlannedStart,
            plannedEnd:   request.PlannedEnd);

        await _stages.AddAsync(stage, ct);
        await _stages.SaveChangesAsync(ct);
        return MapToDto(stage);
    }

    public async Task<BuildStageDto> UpdateAsync(
        Guid                    stageId,
        UpdateBuildStageRequest request,
        CancellationToken       ct = default)
    {
        var stage = await _stages.GetByIdAsync(stageId, ct)
            ?? throw new NotFoundException("BuildStage", stageId);

        stage.UpdateDetails(
            name:         request.Name,
            description:  request.Description,
            order:        request.Order,
            plannedStart: request.PlannedStart,
            plannedEnd:   request.PlannedEnd);

        _stages.Update(stage);
        await _stages.SaveChangesAsync(ct);
        return MapToDto(stage);
    }

    public async Task<BuildStageDto> UpdateProgressAsync(
        Guid                            stageId,
        UpdateBuildStageProgressRequest request,
        CancellationToken               ct = default)
    {
        var stage = await _stages.GetByIdAsync(stageId, ct)
            ?? throw new NotFoundException("BuildStage", stageId);

        stage.UpdateProgress(request.ProgressPercent);

        // Optional explicit status override (e.g. mark InProgress without 100%)
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<BuildStageStatus>(request.Status, ignoreCase: true, out var s))
                throw new DomainException($"Invalid stage status: '{request.Status}'.");
            stage.UpdateStatus(s);
        }

        _stages.Update(stage);
        await _stages.SaveChangesAsync(ct);
        return MapToDto(stage);
    }

    public async Task ReorderAsync(
        Guid                      projectId,
        ReorderBuildStagesRequest request,
        CancellationToken         ct = default)
    {
        _ = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("BuildProject", projectId);

        var stages   = await _stages.GetByProjectAsync(projectId, ct);
        var stageMap = stages.ToDictionary(s => s.Id);

        foreach (var entry in request.Entries)
        {
            if (!stageMap.TryGetValue(entry.StageId, out var stage))
                throw new NotFoundException("BuildStage", entry.StageId);
            stage.Reorder(entry.Order);
            _stages.Update(stage);
        }

        await _stages.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid stageId, CancellationToken ct = default)
    {
        var stage = await _stages.GetByIdAsync(stageId, ct)
            ?? throw new NotFoundException("BuildStage", stageId);

        var project = await _projects.GetByIdAsync(stage.ProjectId, ct);
        if (project is not null && !project.IsActive)
            throw new DomainException($"Cannot delete a stage from a project in status '{project.Status}'.");

        _stages.Remove(stage);
        await _stages.SaveChangesAsync(ct);
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    internal static BuildStageDto MapToDto(BuildStage s) => new(
        Id:               s.Id,
        ProjectId:        s.ProjectId,
        Name:             s.Name,
        Description:      s.Description,
        Order:            s.Order,
        Status:           s.Status.ToString(),
        ProgressPercent:  s.ProgressPercent,
        PlannedStartDate: s.PlannedStartDate,
        PlannedEndDate:   s.PlannedEndDate,
        ActualStartDate:  s.ActualStartDate,
        ActualEndDate:    s.ActualEndDate,
        CreatedAt:        s.CreatedAt,
        UpdatedAt:        s.UpdatedAt);
}
