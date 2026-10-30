using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Client selections (escolhas) — company side (tenant-scoped, Management). Confirming an option that changes the
/// price or has a lead time creates a draft change order (selection → change order → budget/schedule).
/// </summary>
public sealed class BuildSelectionService
{
    /// <summary>Private storage folder of option images (upload context build-selection-option).</summary>
    public const string ImageFolder = "build/selections";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IPrivateFileUrls _privateUrls;
    private readonly BuildChangeOrderService _changeOrders;

    public BuildSelectionService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IPrivateFileUrls privateUrls,
        BuildChangeOrderService changeOrders)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _privateUrls = privateUrls;
        _changeOrders = changeOrders;
    }

    public async Task<IReadOnlyList<BuildSelectionDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        var list = await _db.BldSelections.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && s.ProjectId == projectId)
            .OrderBy(s => s.Deadline == null).ThenBy(s => s.Deadline).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);
        return await MapManyAsync(list, ct);
    }

    public async Task<BuildSelectionDto> GetAsync(Guid id, CancellationToken ct)
        => (await MapManyAsync([await LoadAsync(id, ct, tracking: false)], ct))[0];

    public async Task<BuildSelectionDto> CreateAsync(Guid projectId, SaveBuildSelectionRequest r, CancellationToken ct)
    {
        await EnsureProjectAsync(projectId, ct);
        await EnsureStageAsync(projectId, r.StageId, ct);
        var s = BuildSelection.Create(_tenant.Id, projectId, r.Title, r.Description, r.StageId, r.Deadline, r.ClientVisible, _audit.UserId);
        _db.BldSelections.Add(s);
        _audit.Record(AuditActions.BuildSelectionSaved, "Selection", s.Id, $"Escolha do cliente criada: {s.Title}.", new { projectId });
        await _db.SaveBuildAsync(ct);
        return await GetAsync(s.Id, ct);
    }

    public async Task<BuildSelectionDto> UpdateAsync(Guid id, SaveBuildSelectionRequest r, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        await EnsureStageAsync(s.ProjectId, r.StageId, ct);
        s.Update(r.Title, r.Description, r.StageId, r.Deadline, r.ClientVisible);
        _audit.Record(AuditActions.BuildSelectionSaved, "Selection", s.Id, $"Escolha do cliente alterada: {s.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    // ── Options ───────────────────────────────────────────────────────────────

    public async Task<BuildSelectionDto> AddOptionAsync(Guid selectionId, SaveBuildSelectionOptionRequest r, CancellationToken ct)
    {
        var s = await LoadAsync(selectionId, ct);
        s.EnsureOpen();
        EnsureImageKey(r.ImageKey);
        var order = r.Order ?? (await _db.BldSelectionOptions.AsNoTracking()
            .Where(o => o.TenantId == _tenant.Id && o.SelectionId == selectionId).MaxAsync(o => (int?)o.Order, ct) ?? 0) + 1;
        var option = BuildSelectionOption.Create(_tenant.Id, selectionId, order, r.Name, r.Description, r.PriceDelta,
            r.IsStandard, r.ImageKey, r.LeadTimeDays);
        if (option.IsStandard) await ClearOtherStandardsAsync(selectionId, option.Id, ct);
        _db.BldSelectionOptions.Add(option);
        _audit.Record(AuditActions.BuildSelectionSaved, "Selection", s.Id, $"Opção {option.Name} adicionada à escolha {s.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(selectionId, ct);
    }

    public async Task<BuildSelectionDto> UpdateOptionAsync(Guid optionId, SaveBuildSelectionOptionRequest r, CancellationToken ct)
    {
        var option = await LoadOptionAsync(optionId, ct);
        var s = await LoadAsync(option.SelectionId, ct);
        s.EnsureOpen();
        EnsureImageKey(r.ImageKey);
        option.Update(r.Order ?? option.Order, r.Name, r.Description, r.PriceDelta, r.IsStandard, r.ImageKey, r.LeadTimeDays);
        if (option.IsStandard) await ClearOtherStandardsAsync(option.SelectionId, option.Id, ct);
        _audit.Record(AuditActions.BuildSelectionSaved, "Selection", s.Id, $"Opção {option.Name} alterada na escolha {s.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(s.Id, ct);
    }

    public async Task<BuildSelectionDto> RemoveOptionAsync(Guid optionId, CancellationToken ct)
    {
        var option = await LoadOptionAsync(optionId, ct);
        var s = await LoadAsync(option.SelectionId, ct);
        s.EnsureOpen();
        _db.BldSelectionOptions.Remove(option);
        _audit.Record(AuditActions.BuildSelectionSaved, "Selection", s.Id, $"Opção {option.Name} removida da escolha {s.Title}.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(s.Id, ct);
    }

    // ── Confirm / cancel ──────────────────────────────────────────────────────

    /// <summary>
    /// The company confirms the client's choice (or records one with <c>optionId</c> while Open). An option with a
    /// price difference or a lead time creates a Draft change order linked to the selection, in the same save.
    /// </summary>
    public async Task<BuildSelectionDto> ConfirmAsync(Guid id, Guid? optionId, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        var chosenId = s.Status == BuildSelectionStatus.Open ? optionId : s.ChosenOptionId;
        if (chosenId is { } cid && !await _db.BldSelectionOptions.AnyAsync(o => o.TenantId == _tenant.Id && o.Id == cid && o.SelectionId == id, ct))
            throw new DomainException("A opção informada não é desta escolha.", "invalid_option");
        s.Confirm(optionId, _audit.UserName, DateTime.UtcNow);
        var option = await _db.BldSelectionOptions.AsNoTracking()
            .FirstAsync(o => o.TenantId == _tenant.Id && o.Id == s.ChosenOptionId, ct);

        void Stage(BuildChangeOrder? co)
        {
            if (co is not null) s.LinkChangeOrder(co.Id);
            _audit.Record(AuditActions.BuildSelectionConfirmed, "Selection", s.Id,
                $"Escolha {s.Title} confirmada: {option.Name}" + (co is null ? "." : $" (aditivo #{co.Number} em rascunho)."),
                new { optionId = option.Id, option.PriceDelta, option.LeadTimeDays, changeOrderId = co?.Id });
        }

        if (!option.ChangesContract)
        {
            Stage(null);
            await _db.SaveBuildAsync(ct);
            return await GetAsync(id, ct);
        }

        // the schedule impact needs a stage; without one the lead time is only described
        var days = s.StageId is not null ? option.LeadTimeDays ?? 0 : 0;
        var description = $"Gerado pela escolha do cliente \"{s.Title}\": opção {option.Name}."
            + (option.LeadTimeDays is > 0 ? $" Prazo de entrega da opção: {option.LeadTimeDays} dia(s)." : "");
        await _changeOrders.CreateEntityAsync(s.ProjectId,
            new SaveBuildChangeOrderRequest($"Escolha: {s.Title} — {option.Name}", description,
                nameof(BuildChangeOrderReason.ClientRequest), option.PriceDelta, days, s.StageId),
            s.Id, ct, beforeSave: Stage);
        return await GetAsync(id, ct);
    }

    public async Task<BuildSelectionDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        s.Cancel();
        _audit.Record(AuditActions.BuildSelectionCancelled, "Selection", s.Id, $"Escolha {s.Title} cancelada.");
        await _db.SaveBuildAsync(ct);
        return await GetAsync(id, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ClearOtherStandardsAsync(Guid selectionId, Guid keepId, CancellationToken ct)
    {
        var others = await _db.BldSelectionOptions
            .Where(o => o.TenantId == _tenant.Id && o.SelectionId == selectionId && o.Id != keepId && o.IsStandard)
            .ToListAsync(ct);
        foreach (var o in others) o.ClearStandard();
    }

    private void EnsureImageKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!StorageKeys.IsPrivateOf(key, _tenant.Id, ImageFolder))
            throw new DomainException("Imagem inválida: envie pelo upload com o contexto build-selection-option.", "invalid_image_key");
    }

    private async Task<BuildSelection> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldSelections.Where(s => s.TenantId == _tenant.Id && s.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Escolha não encontrada.");
    }

    private async Task<BuildSelectionOption> LoadOptionAsync(Guid id, CancellationToken ct)
        => await _db.BldSelectionOptions.FirstOrDefaultAsync(o => o.TenantId == _tenant.Id && o.Id == id, ct)
           ?? throw new NotFoundException("Opção não encontrada.");

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == projectId, ct))
            throw new NotFoundException("BuildProject", projectId);
    }

    private async Task EnsureStageAsync(Guid projectId, Guid? stageId, CancellationToken ct)
    {
        if (stageId is { } sid
            && !await _db.BldStages.AnyAsync(s => s.TenantId == _tenant.Id && s.Id == sid && s.ProjectId == projectId, ct))
            throw new DomainException("A etapa informada não pertence a esta obra.", "stage_not_of_project");
    }

    private async Task<IReadOnlyList<BuildSelectionDto>> MapManyAsync(IReadOnlyList<BuildSelection> list, CancellationToken ct)
    {
        var ids = list.Select(s => s.Id).ToList();
        var stageIds = list.Where(s => s.StageId.HasValue).Select(s => s.StageId!.Value).Distinct().ToList();
        var options = (await _db.BldSelectionOptions.AsNoTracking()
                .Where(o => o.TenantId == _tenant.Id && ids.Contains(o.SelectionId)).ToListAsync(ct))
            .ToLookup(o => o.SelectionId);
        var stages = await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var today = BusinessClock.Today();
        return list.Select(s => new BuildSelectionDto(
            s.Id, s.PublicId, s.ProjectId, s.Title, s.Description, s.StageId,
            s.StageId is { } sid ? stages.GetValueOrDefault(sid) : null, s.Deadline, s.IsDeadlinePassed(today),
            s.Status.ToString(), s.ClientVisible, s.ChosenOptionId, s.ClientComment, BuildBudgetCalculator.Utc(s.DecidedAt),
            s.ChosenVia?.ToString(), BuildBudgetCalculator.Utc(s.ConfirmedAt), s.ConfirmedByName, s.ChangeOrderId,
            options[s.Id].OrderBy(o => o.Order).ThenBy(o => o.CreatedAt).Select(o => new BuildSelectionOptionDto(
                o.Id, o.Order, o.Name, o.Description, o.PriceDelta, o.IsStandard, o.ImageKey, _privateUrls.For(o.ImageKey),
                o.LeadTimeDays)).ToList(),
            s.CreatedAt, s.UpdatedAt)).ToList();
    }
}
