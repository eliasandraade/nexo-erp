using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Pdf;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Commercial side of budgets (Wave 2, tenant-scoped): terms (validity, notes, area), duplicate, versions,
/// templates (per-m² scaling) and the client PDF. The original budget endpoints/lifecycle stay in
/// <see cref="BuildBudgetService"/>.
/// </summary>
public sealed class BuildBudgetCommercialService
{
    public const string LogoHttpClient = "build-pdf-logo";
    private const int MaxLogoBytes = 2 * 1024 * 1024;

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IPdfRenderer _pdf;
    private readonly BuildPortalMedia _media;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<BuildBudgetCommercialService> _logger;

    public BuildBudgetCommercialService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IPdfRenderer pdf,
        BuildPortalMedia media, IHttpClientFactory http, ILogger<BuildBudgetCommercialService> logger)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _pdf = pdf;
        _media = media;
        _http = http;
        _logger = logger;
    }

    // ── Terms ─────────────────────────────────────────────────────────────────

    public async Task<BuildBudgetDto> SetTermsAsync(Guid budgetId, SetBuildBudgetTermsRequest r, CancellationToken ct)
    {
        var budget = await LoadAsync(budgetId, ct);
        budget.SetTerms(r.ValidUntil, r.Notes, r.InternalNotes, r.AreaM2);
        _audit.Record(AuditActions.BuildBudgetTermsSet, "Budget", budget.Id, $"Condições do orçamento {budget.Name} atualizadas.",
            new { r.ValidUntil, r.AreaM2 });
        await _db.SaveChangesAsync(ct);
        return await DtoAsync(budget, ct);
    }

    // ── Duplicate / versions ──────────────────────────────────────────────────

    public async Task<BuildBudgetDto> DuplicateAsync(Guid budgetId, string? name, CancellationToken ct)
    {
        var source = await LoadAsync(budgetId, ct);
        var copy = source.Duplicate(_audit.UserId, name);
        await CopyItemsAsync(source, copy, ct);
        _audit.Record(AuditActions.BuildBudgetDuplicated, "Budget", copy.Id, $"Orçamento {source.Name} duplicado.",
            new { sourceId = source.Id });
        await _db.SaveChangesAsync(ct);
        return await DtoAsync(copy, ct);
    }

    /// <summary>A Draft copy with version max + 1 of the family; the previous budget is not touched.</summary>
    public async Task<BuildBudgetDto> NewVersionAsync(Guid budgetId, CancellationToken ct)
    {
        var source = await LoadAsync(budgetId, ct);
        var family = source.FamilyId;
        var max = await _db.BldBudgets.AsNoTracking()
            .Where(b => b.TenantId == _tenant.Id && (b.Id == family || b.RootBudgetId == family))
            .MaxAsync(b => b.Version, ct);
        var version = source.NewVersion(_audit.UserId, max + 1);
        await CopyItemsAsync(source, version, ct);
        _audit.Record(AuditActions.BuildBudgetVersionCreated, "Budget", version.Id,
            $"Nova versão (v{version.Version}) do orçamento {source.Name}.", new { parentId = source.Id, family });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Outra versão deste orçamento foi criada ao mesmo tempo. Recarregue e tente de novo.");
        }
        return await DtoAsync(version, ct);
    }

    public async Task<IReadOnlyList<BuildBudgetVersionDto>> GetVersionsAsync(Guid budgetId, CancellationToken ct)
    {
        var budget = await LoadAsync(budgetId, ct, tracking: false);
        var family = budget.FamilyId;
        var today = BusinessClock.Today();
        var rows = await _db.BldBudgets.AsNoTracking()
            .Where(b => b.TenantId == _tenant.Id && (b.Id == family || b.RootBudgetId == family))
            .OrderBy(b => b.Version).ThenBy(b => b.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(b => new BuildBudgetVersionDto(b.Id, b.Version, b.Name, b.Status.ToString(), b.FinalPrice,
            b.ParentBudgetId, b.ValidUntil, b.IsExpiredOn(today), b.CreatedAt)).ToList();
    }

    private async Task CopyItemsAsync(BuildBudget source, BuildBudget target, CancellationToken ct)
    {
        var items = await _db.BldBudgetItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.BudgetId == source.Id)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        var copies = items.Select(i => i.CopyTo(target.Id)).ToList();
        target.RecalculateTotals(copies);
        _db.BldBudgets.Add(target);
        _db.BldBudgetItems.AddRange(copies);
    }

    // ── Templates ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildBudgetTemplateDto>> GetTemplatesAsync(CancellationToken ct)
    {
        var templates = await _db.BldBudgetTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenant.Id).OrderBy(t => t.Name).ToListAsync(ct);
        var ids = templates.Select(t => t.Id).ToList();
        var items = await _db.BldBudgetTemplateItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && ids.Contains(i.TemplateId)).ToListAsync(ct);
        var byTemplate = items.ToLookup(i => i.TemplateId);
        return templates.Select(t => MapTemplate(t, byTemplate[t.Id])).ToList();
    }

    public async Task<BuildBudgetTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(id, ct, tracking: false);
        return MapTemplate(t, await TemplateItemsAsync(id, ct));
    }

    public async Task<BuildBudgetTemplateDto> CreateTemplateAsync(SaveBuildBudgetTemplateRequest r, CancellationToken ct)
    {
        var t = BuildBudgetTemplate.Create(_tenant.Id, _audit.UserId, r.Name, r.Description, r.DefaultMarginPercent);
        _db.BldBudgetTemplates.Add(t);
        var items = BuildTemplateItems(t.Id, r.Items ?? []);
        _db.BldBudgetTemplateItems.AddRange(items);
        _audit.Record(AuditActions.BuildBudgetTemplateSaved, "BudgetTemplate", t.Id, $"Modelo de orçamento {t.Name} criado.",
            new { items = items.Count });
        await _db.SaveChangesAsync(ct);
        return MapTemplate(t, items);
    }

    /// <summary>Replaces the template, items included.</summary>
    public async Task<BuildBudgetTemplateDto> UpdateTemplateAsync(Guid id, SaveBuildBudgetTemplateRequest r, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(id, ct);
        t.Update(r.Name, r.Description, r.DefaultMarginPercent);
        var old = await _db.BldBudgetTemplateItems.Where(i => i.TenantId == _tenant.Id && i.TemplateId == id).ToListAsync(ct);
        _db.BldBudgetTemplateItems.RemoveRange(old);
        var items = BuildTemplateItems(t.Id, r.Items ?? []);
        _db.BldBudgetTemplateItems.AddRange(items);
        _audit.Record(AuditActions.BuildBudgetTemplateSaved, "BudgetTemplate", t.Id, $"Modelo de orçamento {t.Name} alterado.",
            new { items = items.Count });
        await _db.SaveChangesAsync(ct);
        return MapTemplate(t, items);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(id, ct);
        _db.BldBudgetTemplates.Remove(t);
        _audit.Record(AuditActions.BuildBudgetTemplateDeleted, "BudgetTemplate", t.Id, $"Modelo de orçamento {t.Name} excluído.");
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Saves a budget's lines as a template. With <c>perSquareMeter</c> (and a budget area) quantities become
    /// quantity per m²; otherwise they are fixed. Stage names come from the line or its project stage.
    /// </summary>
    public async Task<BuildBudgetTemplateDto> SaveAsTemplateAsync(Guid budgetId, SaveBudgetAsTemplateRequest r, CancellationToken ct)
    {
        var budget = await LoadAsync(budgetId, ct, tracking: false);
        if (r.PerSquareMeter && budget.AreaM2 is not > 0m)
            throw new DomainException("Informe a área do orçamento para salvar as quantidades por m².", "template_area_required");
        var items = await _db.BldBudgetItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.BudgetId == budgetId).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var stageNames = await StageNamesAsync(items.Where(i => i.StageId.HasValue).Select(i => i.StageId!.Value), ct);

        var t = BuildBudgetTemplate.Create(_tenant.Id, _audit.UserId, r.Name, r.Description, budget.MarginPercent);
        _db.BldBudgetTemplates.Add(t);
        var order = 0;
        var templateItems = items.Select(i => BuildBudgetTemplateItem.Create(_tenant.Id, t.Id, ++order, i.Name, i.Kind, i.Category,
            i.Unit,
            r.PerSquareMeter ? PerSquareMeter(i.Quantity, budget.AreaM2!.Value) : null,
            r.PerSquareMeter ? null : i.Quantity,
            i.UnitCost,
            i.StageId is { } sid && stageNames.TryGetValue(sid, out var sn) ? sn : i.StageName)).ToList();
        _db.BldBudgetTemplateItems.AddRange(templateItems);
        _audit.Record(AuditActions.BuildBudgetTemplateSaved, "BudgetTemplate", t.Id,
            $"Modelo {t.Name} criado a partir do orçamento {budget.Name}.", new { budgetId, items = templateItems.Count, r.PerSquareMeter });
        await _db.SaveChangesAsync(ct);
        return MapTemplate(t, templateItems);
    }

    /// <summary>A Draft budget from a template: per-m² quantities × area, fixed quantities as they are.</summary>
    public async Task<BuildBudgetDto> CreateFromTemplateAsync(Guid templateId, CreateBudgetFromTemplateRequest r, CancellationToken ct)
    {
        var t = await LoadTemplateAsync(templateId, ct, tracking: false);
        if (r.ProjectId is { } pid && !await _db.BldProjects.AnyAsync(p => p.TenantId == _tenant.Id && p.Id == pid, ct))
            throw new NotFoundException("BuildProject", pid);
        var templateItems = (await TemplateItemsAsync(templateId, ct)).ToList();

        var budget = BuildBudget.Create(_tenant.Id, _audit.UserId, r.Name, r.ProjectId, r.MarginPercent ?? t.DefaultMarginPercent);
        budget.SetTerms(r.ValidUntil, r.Notes, null, r.AreaM2);
        budget.MarkFromTemplate(t.Id);
        var items = templateItems.Select(i => BuildBudgetItem.Create(_tenant.Id, budget.Id, i.Name, i.Category,
            i.QuantityFor(r.AreaM2), i.Unit, i.UnitCost, null, i.Kind, i.StageName)).ToList();
        budget.RecalculateTotals(items);
        _db.BldBudgets.Add(budget);
        _db.BldBudgetItems.AddRange(items);
        _audit.Record(AuditActions.BuildBudgetFromTemplate, "Budget", budget.Id,
            $"Orçamento {budget.Name} criado do modelo {t.Name}.", new { templateId, r.AreaM2, items = items.Count });
        await _db.SaveChangesAsync(ct);
        return BuildBudgetCalculator.ToDto(budget, items, BusinessClock.Today());
    }

    private static decimal PerSquareMeter(decimal quantity, decimal area)
        => Math.Max(0.0001m, Math.Round(quantity / area, 4, MidpointRounding.AwayFromZero));

    private List<BuildBudgetTemplateItem> BuildTemplateItems(Guid templateId, IReadOnlyList<BuildBudgetTemplateItemRequest> items)
    {
        var order = 0;
        return items.Select(i => BuildBudgetTemplateItem.Create(_tenant.Id, templateId, ++order, i.Name,
            BuildBudgetCalculator.ParseKind(i.Kind), i.Category, i.Unit, i.QuantityPerM2, i.FixedQuantity, i.UnitCost,
            i.StageName)).ToList();
    }

    private async Task<IReadOnlyList<BuildBudgetTemplateItem>> TemplateItemsAsync(Guid templateId, CancellationToken ct)
        => await _db.BldBudgetTemplateItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.TemplateId == templateId)
            .OrderBy(i => i.Order).ToListAsync(ct);

    private async Task<BuildBudgetTemplate> LoadTemplateAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldBudgetTemplates.Where(t => t.TenantId == _tenant.Id && t.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Modelo de orçamento não encontrado.");
    }

    private static BuildBudgetTemplateDto MapTemplate(BuildBudgetTemplate t, IEnumerable<BuildBudgetTemplateItem> items) => new(
        t.Id, t.Name, t.Description, t.DefaultMarginPercent,
        items.OrderBy(i => i.Order).Select(i => new BuildBudgetTemplateItemDto(i.Id, i.Order, i.Name, i.Kind.ToString(), i.Category,
            i.Unit, i.QuantityPerM2, i.FixedQuantity, i.UnitCost, i.StageName)).ToList(),
        t.CreatedAt, t.UpdatedAt);

    // ── PDF ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The client PDF: company branding (portal company name/logo, else the tenant name; the logo is optional and
    /// never makes the PDF fail), project/client, lines grouped by kind with prices including the margin.
    /// </summary>
    public async Task<PdfRenderResult> RenderPdfAsync(Guid budgetId, CancellationToken ct)
    {
        var budget = await LoadAsync(budgetId, ct, tracking: false);
        var items = await _db.BldBudgetItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.BudgetId == budgetId).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var stageNames = await StageNamesAsync(items.Where(i => i.StageId.HasValue).Select(i => i.StageId!.Value), ct);

        var company = await _db.BldPortalCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == _tenant.Id, ct);
        var tenant = await _db.Tenants.AsNoTracking().Where(x => x.Id == _tenant.Id)
            .Select(x => new { x.CompanyName, x.TradeName, x.Email, x.Phone }).FirstOrDefaultAsync(ct);
        var name = company?.DisplayName ?? tenant?.TradeName ?? tenant?.CompanyName ?? "Orçamento";
        var contact = string.Join(" · ", new[] { company?.ContactPhone ?? tenant?.Phone, company?.ContactEmail ?? tenant?.Email }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var logo = await LogoAsync(company?.LogoKey, ct);

        var project = budget.ProjectId is { } pid
            ? await _db.BldProjects.AsNoTracking().Where(p => p.TenantId == _tenant.Id && p.Id == pid)
                .Select(p => new { p.Name, p.ClientName, p.Location }).FirstOrDefaultAsync(ct)
            : null;

        var model = BuildBudgetPdfModelBuilder.Build(budget, items, name, logo, contact.Length == 0 ? null : contact,
            project?.Name, project?.ClientName, project?.Location, stageNames, BusinessClock.Today());
        return _pdf.RenderBuildBudget(model);
    }

    private async Task<byte[]?> LogoAsync(string? logoKey, CancellationToken ct)
    {
        var url = _media.BrandingUrl(logoKey, _tenant.Id);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            var client = _http.CreateClient(LogoHttpClient);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxLogoBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            return bytes.Length is > 0 and <= MaxLogoBytes ? bytes : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Budget PDF: company logo could not be loaded; rendering without it.");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BuildBudget> LoadAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        var q = _db.BldBudgets.Where(b => b.TenantId == _tenant.Id && b.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("BuildBudget", id);
    }

    private async Task<Dictionary<Guid, string>> StageNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await _db.BldStages.AsNoTracking()
            .Where(s => s.TenantId == _tenant.Id && list.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }

    private async Task<BuildBudgetDto> DtoAsync(BuildBudget budget, CancellationToken ct)
    {
        var items = await _db.BldBudgetItems.AsNoTracking()
            .Where(i => i.TenantId == _tenant.Id && i.BudgetId == budget.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return BuildBudgetCalculator.ToDto(budget, items, BusinessClock.Today());
    }
}
