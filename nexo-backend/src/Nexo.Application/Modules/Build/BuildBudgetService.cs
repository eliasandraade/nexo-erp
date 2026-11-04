using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// Use cases for BuildBudget and BuildBudgetItem.
///
/// Budget lifecycle:
///   Draft → Sent → Approved → Converted (linked to a project)
///           ↑               → Rejected
///   Draft → Approved (shortcut — client skips formal send)
///
/// ConvertToProject: budget must be Approved; links to existing project by ID.
///   After conversion, project.BudgetApproved is updated from budget.FinalPrice.
/// </summary>
public class BuildBudgetService
{
    private readonly IBuildBudgetRepository     _budgets;
    private readonly IBuildBudgetItemRepository _items;
    private readonly IBuildProjectRepository    _projects;
    private readonly IBuildStageRepository      _stages;
    private readonly ICurrentTenant             _currentTenant;
    private readonly ICurrentUser               _currentUser;

    public BuildBudgetService(
        IBuildBudgetRepository     budgets,
        IBuildBudgetItemRepository items,
        IBuildProjectRepository    projects,
        IBuildStageRepository      stages,
        ICurrentTenant             currentTenant,
        ICurrentUser               currentUser)
    {
        _budgets       = budgets;
        _items         = items;
        _projects      = projects;
        _stages        = stages;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<BuildPagedResult<BuildBudgetDto>> GetAllAsync(
        Guid?              projectId = null,
        BuildBudgetStatus? status    = null,
        int                page      = 1,
        int                pageSize  = 20,
        CancellationToken  ct        = default)
    {
        (page, pageSize) = BuildPaging.Clamp(page, pageSize);
        var total = await _budgets.CountAsync(projectId, status, ct);
        var list = await _budgets.GetAllAsync(projectId, status, page, pageSize, ct);

        var dtos = new List<BuildBudgetDto>();
        foreach (var b in list)
        {
            var items = await _items.GetByBudgetAsync(b.Id, ct);
            dtos.Add(MapToDto(b, items));
        }

        return new BuildPagedResult<BuildBudgetDto>(dtos, total, page, pageSize);
    }

    public async Task<BuildBudgetDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("BuildBudget", id);
        return MapToDto(budget, budget.Items);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<BuildBudgetDto> CreateAsync(
        CreateBuildBudgetRequest request,
        CancellationToken        ct = default)
    {
        if (request.ProjectId.HasValue)
            _ = await _projects.GetByIdAsync(request.ProjectId.Value, ct)
                ?? throw new NotFoundException("BuildProject", request.ProjectId.Value);

        var budget = BuildBudget.Create(
            tenantId:      _currentTenant.Id,
            createdBy:     _currentUser.UserId,
            name:          request.Name,
            projectId:     request.ProjectId,
            marginPercent: request.MarginPercent);
        if (request.ValidUntil is not null || request.Notes is not null || request.InternalNotes is not null
            || request.AreaM2 is not null)
            budget.SetTerms(request.ValidUntil, request.Notes, request.InternalNotes, request.AreaM2);

        await _budgets.AddAsync(budget, ct);
        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, []);
    }

    public async Task<BuildBudgetDto> AddItemAsync(
        Guid                     budgetId,
        AddBuildBudgetItemRequest request,
        CancellationToken        ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);

        if (!budget.IsEditable)
            throw new DomainException($"Budget '{budget.Name}' in status '{budget.Status}' does not accept new items.");

        var item = BuildBudgetItem.Create(
            tenantId:  _currentTenant.Id,
            budgetId:  budgetId,
            name:      request.Name,
            category:  request.Category,
            quantity:  request.Quantity,
            unit:      request.Unit,
            unitCost:  request.UnitCost,
            stageId:   request.StageId,
            kind:      BuildBudgetCalculator.ParseKind(request.Kind),
            stageName: request.StageName);

        await _items.AddAsync(item, ct);

        var allItems = (await _items.GetByBudgetAsync(budgetId, ct)).ToList();
        allItems.Add(item);
        budget.RecalculateTotals(allItems);
        _budgets.Update(budget);

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, allItems);
    }

    public async Task<BuildBudgetDto> UpdateItemAsync(
        Guid                         itemId,
        UpdateBuildBudgetItemRequest request,
        CancellationToken            ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("BuildBudgetItem", itemId);

        var budget = await _budgets.GetByIdWithItemsAsync(item.BudgetId, ct)
            ?? throw new NotFoundException("BuildBudget", item.BudgetId);

        if (!budget.IsEditable)
            throw new DomainException($"Budget '{budget.Name}' in status '{budget.Status}' cannot be modified.");

        item.Update(
            name:     request.Name,
            category: request.Category,
            quantity: request.Quantity,
            unit:     request.Unit,
            unitCost: request.UnitCost,
            stageId:  request.StageId,
            kind:     request.Kind is null ? null : BuildBudgetCalculator.ParseKind(request.Kind),
            stageName: request.StageName);

        _items.Update(item);

        var allItems = await _items.GetByBudgetAsync(item.BudgetId, ct);
        budget.RecalculateTotals(allItems);
        _budgets.Update(budget);

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, allItems);
    }

    public async Task<BuildBudgetDto> RemoveItemAsync(
        Guid              itemId,
        CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(itemId, ct)
            ?? throw new NotFoundException("BuildBudgetItem", itemId);

        var budget = await _budgets.GetByIdWithItemsAsync(item.BudgetId, ct)
            ?? throw new NotFoundException("BuildBudget", item.BudgetId);

        if (!budget.IsEditable)
            throw new DomainException($"Budget '{budget.Name}' in status '{budget.Status}' cannot be modified.");

        _items.Remove(item);

        var remaining = (await _items.GetByBudgetAsync(item.BudgetId, ct))
            .Where(i => i.Id != itemId)
            .ToList();
        budget.RecalculateTotals(remaining);
        _budgets.Update(budget);

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, remaining);
    }

    public async Task<BuildBudgetDto> SetMarginAsync(
        Guid                   budgetId,
        SetBudgetMarginRequest request,
        CancellationToken      ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);

        budget.SetMargin(request.MarginPercent);
        _budgets.Update(budget);
        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<BuildBudgetDto> ApproveBudgetAsync(Guid budgetId, CancellationToken ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);
        budget.Approve(BusinessClock.Today(), _currentUser.UserId, _currentUser.Name);
        _budgets.Update(budget);

        // When the budget is already linked to a project, approving it sets the
        // project's approved budget to the budget's final price. Conversion to
        // "Converted" status remains a separate, explicit step.
        if (budget.ProjectId is Guid projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, ct);
            if (project is not null)
            {
                project.ApproveBudget(budget.FinalPrice);
                _projects.Update(project);
            }
        }

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    public async Task<BuildBudgetDto> RejectBudgetAsync(Guid budgetId, string? reason = null, CancellationToken ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);
        budget.Reject(_currentUser.UserId, _currentUser.Name, reason);
        _budgets.Update(budget);
        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    public async Task<BuildBudgetDto> MarkSentAsync(Guid budgetId, CancellationToken ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);
        budget.MarkSent();
        _budgets.Update(budget);
        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    /// <summary>
    /// Converts an Approved budget into a project link.
    /// Also updates the project's BudgetApproved to budget.FinalPrice.
    /// </summary>
    public async Task<BuildBudgetDto> ConvertToProjectAsync(
        Guid                         budgetId,
        ConvertBudgetToProjectRequest request,
        CancellationToken            ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);

        var project = await _projects.GetByIdAsync(request.ProjectId, ct)
            ?? throw new NotFoundException("BuildProject", request.ProjectId);

        if (!project.IsActive)
            throw new DomainException($"Cannot link budget to a project in status '{project.Status}'.");

        budget.Convert(request.ProjectId);
        _budgets.Update(budget);

        project.ApproveBudget(budget.FinalPrice);
        _projects.Update(project);

        await CreateStagesFromItemsAsync(project, budget.Items, ct);

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    /// <summary>
    /// A project without stages gets one stage per distinct item stage name (in item order), and the items with
    /// that name are linked to it — so cost per stage and measurements work right after the conversion. A
    /// project that already has stages is left as it is.
    /// </summary>
    private async Task CreateStagesFromItemsAsync(BuildProject project, IEnumerable<BuildBudgetItem> items, CancellationToken ct)
    {
        var named = items.Where(i => i.StageId is null && !string.IsNullOrWhiteSpace(i.StageName))
            .OrderBy(i => i.CreatedAt).ToList();
        if (named.Count == 0) return;
        if ((await _stages.GetByProjectAsync(project.Id, ct)).Count > 0) return;

        var created = new Dictionary<string, BuildStage>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var item in named)
        {
            var name = item.StageName!.Trim();
            if (!created.TryGetValue(name, out var stage))
            {
                stage = BuildStage.Create(project.TenantId, project.Id, name, ++order);
                created[name] = stage;
                await _stages.AddAsync(stage, ct);
            }
            item.LinkStage(stage.Id);
            _items.Update(item);
        }
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static BuildBudgetDto MapToDto(BuildBudget b, IEnumerable<BuildBudgetItem> items)
        => BuildBudgetCalculator.ToDto(b, items, BusinessClock.Today());
}
