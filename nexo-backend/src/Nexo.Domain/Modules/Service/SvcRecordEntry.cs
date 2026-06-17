using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// An internal annotation / history note in the Service engine — "observações internas",
/// "histórico de atendimento". Store-scoped (a <see cref="StoreEntity"/>) operational record,
/// append-only (no edit; hard delete only). Attaches to a <see cref="SvcRecordContextType"/>
/// (customer or subject in v1).
///
/// NOTE (v1 scope): this is NOT a regulated medical record (não é prontuário CFM/CFO,
/// prescrição, nem sistema hospitalar) — it is a free-text note plus durable attachment
/// references. <see cref="AttachmentsJson"/> stores only durable fields (storageKey, fileName,
/// contentType, sizeBytes, caption); the public URL is composed at read time, never persisted.
/// </summary>
public class SvcRecordEntry : StoreEntity
{
    private SvcRecordEntry() { }                                   // EF Core
    private SvcRecordEntry(Guid tenantId) : base(tenantId) { }

    public SvcRecordContextType ContextType     { get; private set; }
    public Guid                 ContextId       { get; private set; }
    public Guid                 AuthorUserId    { get; private set; }
    public string?              Text            { get; private set; }
    public string?              AttachmentsJson { get; private set; }

    public static SvcRecordEntry Create(
        Guid                 tenantId,
        SvcRecordContextType contextType,
        Guid                 contextId,
        Guid                 authorUserId,
        string?              text,
        string?              attachmentsJson)
    {
        if (contextId == Guid.Empty)
            throw new DomainException("Record context id is required.");
        if (authorUserId == Guid.Empty)
            throw new DomainException("Record author is required.");
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(attachmentsJson))
            throw new DomainException("A record must have text or at least one attachment.");

        return new SvcRecordEntry(tenantId)
        {
            ContextType     = contextType,
            ContextId       = contextId,
            AuthorUserId    = authorUserId,
            Text            = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            AttachmentsJson = string.IsNullOrWhiteSpace(attachmentsJson) ? null : attachmentsJson,
        };
    }
}
