using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

public enum BuildClientUpdateKind
{
    Update       = 1,
    Milestone    = 2,
    WeeklyDigest = 3,
}

public enum BuildClientUpdateStatus
{
    Draft     = 1,
    Published = 2,
    Archived  = 3,
}

public enum BuildClientUpdateSource
{
    Manual = 1,
    Ai     = 2,
    /// <summary>Templated text built from the facts by the deterministic fallback (no generated prose).</summary>
    Rules  = 3,
}

/// <summary>
/// A message the company writes for the client of a project (news, milestone, weekly digest). Only
/// published updates reach the client portal; archived ones leave it. An AI-written draft is only shown
/// after a person publishes it.
/// </summary>
public class BuildClientUpdate : TenantEntity
{
    private BuildClientUpdate() { }
    private BuildClientUpdate(Guid tenantId) : base(tenantId) { }

    public Guid                    ProjectId     { get; private set; }
    public BuildClientUpdateKind   Kind          { get; private set; }
    public string                  Title         { get; private set; } = string.Empty;
    public string                  Body          { get; private set; } = string.Empty;
    public BuildClientUpdateStatus Status        { get; private set; }
    public DateTime?               PublishedAt   { get; private set; }
    public BuildClientUpdateSource Source        { get; private set; }
    public Guid                    CreatedBy     { get; private set; }
    public string                  CreatedByName { get; private set; } = string.Empty;

    public static BuildClientUpdate Create(Guid tenantId, Guid projectId, BuildClientUpdateKind kind, string title,
        string body, BuildClientUpdateSource source, Guid createdBy, string createdByName)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (!Enum.IsDefined(source)) throw new DomainException("Origem inválida.");
        var u = new BuildClientUpdate(tenantId)
        {
            ProjectId     = projectId,
            Status        = BuildClientUpdateStatus.Draft,
            Source        = source,
            CreatedBy     = createdBy,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? "usuário" : createdByName.Trim(),
        };
        u.Apply(kind, title, body);
        return u;
    }

    public void Edit(BuildClientUpdateKind kind, string title, string body)
    {
        if (Status == BuildClientUpdateStatus.Archived)
            throw new DomainException("Esta atualização foi arquivada e não pode mais ser editada.");
        Apply(kind, title, body);
        SetUpdatedAt();
    }

    public void Publish(DateTime nowUtc)
    {
        if (Status == BuildClientUpdateStatus.Archived)
            throw new DomainException("Esta atualização foi arquivada e não pode ser publicada.");
        if (Status == BuildClientUpdateStatus.Published) return;
        Status      = BuildClientUpdateStatus.Published;
        PublishedAt = nowUtc;
        SetUpdatedAt();
    }

    public void Archive()
    {
        Status = BuildClientUpdateStatus.Archived;
        SetUpdatedAt();
    }

    private void Apply(BuildClientUpdateKind kind, string title, string body)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Tipo de atualização inválido.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Informe o título da atualização.");
        if (string.IsNullOrWhiteSpace(body)) throw new DomainException("Escreva o texto da atualização.");
        if (title.Trim().Length > 200) throw new DomainException("O título pode ter até 200 caracteres.");
        if (body.Trim().Length > 10000) throw new DomainException("O texto pode ter até 10000 caracteres.");
        Kind  = kind;
        Title = title.Trim();
        Body  = body.Trim();
    }
}
