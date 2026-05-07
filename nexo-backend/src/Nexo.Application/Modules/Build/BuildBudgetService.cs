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
    private readonly ICurrentTenant             _currentTenant;
    private readonly ICurrentUser               _currentUser;

    public BuildBudgetService(
        IBuildBudgetRepository     budgets,
        IBuildBudgetItemRepository items,
        IBuildProjectRepository    projects,
        ICurrentTenant             currentTenant,
        ICurrentUser               currentUser)
    {
        _budgets       = budgets;
        _items         = items;
        _projects      = projects;
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
        var list = await _budgets.GetAllAsync(projectId, status, page, pageSize, ct);

        var dtos = new List<BuildBudgetDto>();
        foreach (var b in list)
        {
            var items = await _items.GetByBudgetAsync(b.Id, ct);
            dtos.Add(MapToDto(b, items));
        }

        return new BuildPagedResult<BuildBudgetDto>(dtos, dtos.Count, page, pageSize);
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
            stageId:   request.StageId);

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
            stageId:  request.StageId);

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
        budget.Approve();
        _budgets.Update(budget);
        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    public async Task<BuildBudgetDto> RejectBudgetAsync(Guid budgetId, CancellationToken ct = default)
    {
        var budget = await _budgets.GetByIdWithItemsAsync(budgetId, ct)
            ?? throw new NotFoundException("BuildBudget", budgetId);
        budget.Reject();
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

        await _budgets.SaveChangesAsync(ct);
        return MapToDto(budget, budget.Items);
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static BuildBudgetDto MapToDto(BuildBudget b, IEnumerable<BuildBudgetItem> items) => new(
        Id:            b.Id,
        ProjectId:     b.ProjectId,
        Name:          b.Name,
        Status:        b.Status.ToString(),
        TotalCost:     b.TotalCost,
        MarginPercent: b.MarginPercent,
        FinalPrice:    b.FinalPrice,
        Items:         items.Select(MapItemToDto).ToList(),
        CreatedAt:     b.CreatedAt,
        UpdatedAt:     b.UpdatedAt);

    private static BuildBudgetItemDto MapItemToDto(BuildBudgetItem i) => new(
        Id:        i.Id,
        BudgetId:  i.BudgetId,
        StageId:   i.StageId,
        Name:      i.Name,
        Category:  i.Category,
        Quantity:  i.Quantity,
        Unit:      i.Unit,
        UnitCost:  i.UnitCost,
        TotalCost: i.TotalCost,
        CreatedAt: i.CreatedAt,
        UpdatedAt: i.UpdatedAt);
}
