using System.Text.Json;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for SvcRecordEntry — internal annotations/history with durable attachment refs.
/// The context (customer or subject) is validated through tenant-filtered repositories, so a
/// cross-tenant context is invisible → NotFound → 404. Attachment public URLs are composed at
/// read time from the durable storageKey (IStoragePublicUrlResolver); they are never persisted.
/// </summary>
public class SvcRecordEntryService
{
    private readonly ISvcRecordEntryRepository  _repo;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ISvcOrderRepository        _orders;
    private readonly ICustomerRepository        _customers;
    private readonly ICurrentTenant             _currentTenant;
    private readonly ICurrentUser               _currentUser;
    private readonly IStoragePublicUrlResolver  _urls;

    public SvcRecordEntryService(
        ISvcRecordEntryRepository repo,
        ISvcSubjectRepository     subjects,
        ISvcOrderRepository       orders,
        ICustomerRepository       customers,
        ICurrentTenant            currentTenant,
        ICurrentUser              currentUser,
        IStoragePublicUrlResolver urls)
    {
        _repo          = repo;
        _subjects      = subjects;
        _orders        = orders;
        _customers     = customers;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
        _urls          = urls;
    }

    public async Task<IReadOnlyList<SvcRecordEntryDto>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default)
        => (await _repo.GetByContextAsync(contextType, contextId, ct)).Select(MapToDto).ToList();

    public async Task<SvcRecordEntryDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcRecordEntry", id));

    public async Task<SvcRecordEntryDto> CreateAsync(CreateSvcRecordEntryRequest request, CancellationToken ct = default)
    {
        // ContextType / ContextId are NotNull-validated upstream.
        var contextType = request.ContextType!.Value;
        var contextId   = request.ContextId!.Value;

        await EnsureContextExistsAsync(contextType, contextId, ct);

        var attachmentsJson = (request.Attachments is { Count: > 0 })
            ? JsonSerializer.Serialize(request.Attachments)
            : null;

        var entry = SvcRecordEntry.Create(
            tenantId:        _currentTenant.Id,
            contextType:     contextType,
            contextId:       contextId,
            authorUserId:    _currentUser.UserId,
            text:            request.Text,
            attachmentsJson: attachmentsJson);

        await _repo.AddAsync(entry, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(entry);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcRecordEntry", id);
        _repo.Remove(entry);
        await _repo.SaveChangesAsync(ct);
    }

    private async Task EnsureContextExistsAsync(SvcRecordContextType type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case SvcRecordContextType.Customer:
                _ = await _customers.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Customer), id);
                break;
            case SvcRecordContextType.Subject:
                _ = await _subjects.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);
                break;
            case SvcRecordContextType.Order:
                _ = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
                break;
            default:
                // Defense in depth — the validator already rejects reserved context types.
                throw new DomainException("Context type is not supported yet.");
        }
    }

    private SvcRecordEntryDto MapToDto(SvcRecordEntry e) => new(
        Id:           e.Id,
        StoreId:      e.StoreId,
        ContextType:  e.ContextType,
        ContextId:    e.ContextId,
        AuthorUserId: e.AuthorUserId,
        Text:         e.Text,
        Attachments:  ParseAttachments(e.AttachmentsJson),
        CreatedAt:    e.CreatedAt);

    private IReadOnlyList<SvcRecordAttachmentDto> ParseAttachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SvcRecordAttachmentDto>();
        var raw = JsonSerializer.Deserialize<List<SvcRecordAttachmentInput>>(json)
                  ?? new List<SvcRecordAttachmentInput>();
        return raw.Select(a => new SvcRecordAttachmentDto(
            StorageKey:  a.StorageKey,
            FileName:    a.FileName,
            ContentType: a.ContentType,
            SizeBytes:   a.SizeBytes,
            Caption:     a.Caption,
            Url:         _urls.ResolvePublicUrl(a.StorageKey))).ToList();
    }
}
