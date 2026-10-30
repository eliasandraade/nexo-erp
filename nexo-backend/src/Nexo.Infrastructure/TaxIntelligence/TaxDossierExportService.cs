using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Files;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Exports a decision dossier as a ZIP an auditor can keep: the dossier exactly as frozen (JSON, with its stored hash), a human-readable
/// version, the execution verification when there is one, the legal sources and the evidence indexes they froze, the evidence files when
/// asked for and allowed — those frozen by the decision and those frozen by the execution verification — and a manifest with the SHA-256
/// of every file in the package. Only this company's dossiers are found; a file that is missing or whose bytes no longer match the hash
/// the dossier froze is reported in the manifest, never silently included or left out.
/// </summary>
public sealed class TaxDossierExportService
{
    private const string AuditEntity = "Opportunity";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly TaxContext _ctx;
    private readonly TaxFileStore _files;

    public TaxDossierExportService(TaxContext ctx, TaxFileStore files)
    {
        _ctx = ctx;
        _files = files;
    }

    public async Task<(string FileName, byte[] Content)> ExportAsync(Guid decisionId, bool includeFiles, CancellationToken ct)
    {
        var db = _ctx.Db;
        var decision = await db.TaxDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == decisionId, ct) ?? throw new NotFoundException("Decisão", decisionId);
        var execution = await db.TaxExecutionVerifications.AsNoTracking().FirstOrDefaultAsync(e => e.DecisionId == decisionId, ct);
        using var dossier = JsonDocument.Parse(decision.DossierJson);
        var root = dossier.RootElement;

        var entries = new List<(string Path, byte[] Bytes)>
        {
            ("dossie/decisao.json", Encoding.UTF8.GetBytes(decision.DossierJson)),
            ("dossie/decisao.md", Encoding.UTF8.GetBytes(Markdown(decision, root, execution))),
        };
        if (execution is not null) entries.Add(("dossie/verificacao-execucao.json", Encoding.UTF8.GetBytes(execution.DossierJson)));
        if (root.TryGetProperty("legalSources", out var sources))
            entries.Add(("fontes-juridicas.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sources, Pretty))));
        var evidence = ArrayOf(root, "evidence");
        if (evidence is { } decided)
            entries.Add(("evidencias/indice.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(decided, Pretty))));
        using var executionDossier = execution is null ? null : JsonDocument.Parse(execution.DossierJson);
        var fiscal = executionDossier is null ? null : ArrayOf(executionDossier.RootElement, "fiscalDocumentEvidence");
        if (fiscal is { } executed)
            entries.Add(("evidencias/indice-execucao.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(executed, Pretty))));

        var problems = new List<string>();
        var evidenceFiles = new List<object>();
        if (includeFiles)
            await AddFilesAsync([("decisao", evidence), ("execucao", fiscal)], entries, evidenceFiles, problems, ct);

        var manifest = new
        {
            kind = "orken-tax-dossier-export/1",
            generatedAt = DateTime.UtcNow,
            generatedBy = _ctx.Audit.UserName,
            tenantId = _ctx.Tenant.Id,
            decisionId = decision.Id,
            opportunityId = decision.OpportunityId,
            dossierHash = decision.DossierHash,
            executionVerificationHash = execution?.DossierHash,
            engineVersion = decision.EngineVersion,
            filesIncluded = includeFiles,
            evidenceFiles,
            problems,
            notice = TaxOpportunityService.IntegrityNotice,
            files = entries.Select(e => new { path = e.Path, sha256 = Sha(e.Bytes), sizeBytes = e.Bytes.LongLength }).ToList(),
        };
        entries.Add(("manifesto.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Pretty))));

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                await using var s = entry.Open();
                await s.WriteAsync(bytes, ct);
            }

        _ctx.Audit.Record(TaxAuditActions.DossierExported, AuditEntity, decision.OpportunityId,
            $"Dossiê exportado (decisão {decision.DossierHash[..12]}…, {(includeFiles ? "com" : "sem")} arquivos).",
            new { decisionId, includeFiles, files = entries.Count, problems });
        await _ctx.Db.SaveChangesAsync(ct);
        return ($"dossie-{decision.Id:N}.zip", zip.ToArray());
    }

    private static JsonElement? ArrayOf(JsonElement root, string property)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items : null;

    /// <summary>A file reference as a dossier froze it: the file id and the SHA-256 of its bytes at that moment.</summary>
    private sealed record FrozenFile(Guid Id, string? Sha256, List<string> Phases);

    /// <summary>
    /// Adds each evidence file the dossiers froze — the decision's first, then the execution verification's — under the folder of the
    /// phase that first referenced it; a file referenced by both phases is included once and listed with both. Which files belong to the
    /// package comes from the frozen references, never from the evidence as it stands today; the bytes are checked against the SHA-256 the
    /// dossier froze and against the one recorded at upload. Files are looked up within this company only.
    /// </summary>
    private async Task AddFilesAsync(IReadOnlyList<(string Phase, JsonElement? Evidence)> phases, List<(string Path, byte[] Bytes)> entries,
        List<object> evidenceFiles, List<string> problems, CancellationToken ct)
    {
        var frozen = new Dictionary<Guid, FrozenFile>();
        foreach (var (phase, evidence) in phases)
        {
            if (evidence is not { } items) continue;
            foreach (var reference in items.EnumerateArray().Select(FileOf).OfType<FrozenFile>())
            {
                if (!frozen.TryGetValue(reference.Id, out var known)) frozen[reference.Id] = known = reference with { Phases = [] };
                if (!known.Phases.Contains(phase)) known.Phases.Add(phase);
            }
        }
        var ids = frozen.Keys.ToList();
        var files = await _ctx.Db.TaxEvidenceFiles.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        foreach (var reference in frozen.Values)
        {
            var phase = reference.Phases[0];
            if (!files.TryGetValue(reference.Id, out var f))
            {
                problems.Add($"Arquivo {reference.Id} ({phase}): registro não encontrado — não incluído.");
                continue;
            }
            var expected = reference.Sha256 ?? f.Sha256;
            if (!string.Equals(expected, f.Sha256, StringComparison.Ordinal))
            {
                problems.Add($"Arquivo {f.Id} ({phase}): o SHA-256 registrado não é o que o dossiê congelou — não incluído.");
                continue;
            }
            var bytes = await ReadAsync(f, phase, problems, ct);
            if (bytes is null) continue;
            if (!string.Equals(Sha(bytes), expected, StringComparison.Ordinal))
            {
                problems.Add($"Arquivo {f.Id} ({phase}): os bytes armazenados não conferem com o SHA-256 congelado — não incluído.");
                continue;
            }
            var path = $"evidencias/{phase}/{expected}{f.Extension}";
            if (!entries.Exists(e => e.Path == path)) entries.Add((path, bytes));
            evidenceFiles.Add(new { fileId = f.Id, sha256 = expected, path, phases = reference.Phases });
        }
    }

    private static FrozenFile? FileOf(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("file", out var f) || f.ValueKind != JsonValueKind.Object
            || !f.TryGetProperty("id", out var id) || !id.TryGetGuid(out var fileId)) return null;
        var sha = f.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return new FrozenFile(fileId, sha, []);
    }

    private async Task<byte[]?> ReadAsync(TaxEvidenceFile f, string phase, List<string> problems, CancellationToken ct)
    {
        try
        {
            await using var stream = await _files.OpenAsync(f.StorageKey, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Add($"Arquivo {f.Id} ({phase}): não foi possível ler do armazenamento ({ex.GetType().Name}) — não incluído.");
            return null;
        }
    }

    private static string Markdown(TaxDecision d, JsonElement root, TaxExecutionVerification? x)
    {
        static string Get(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
        }
        var md = new StringBuilder();
        md.AppendLine($"# Dossiê de decisão — {d.Strategy}");
        md.AppendLine();
        md.AppendLine($"> {Get(root, "nature")}");
        md.AppendLine();
        md.AppendLine("| Campo | Valor |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Decisão | `{d.Id}` |");
        md.AppendLine($"| Aprovado por | {d.ApprovedByName} em {d.ApprovedAt:yyyy-MM-dd HH:mm} UTC |");
        md.AppendLine($"| Risco | {d.Risk}{(d.HighRiskAcknowledged ? " (reconhecido expressamente)" : "")} |");
        md.AppendLine($"| Engine | {d.EngineVersion} |");
        md.AppendLine($"| Hash do dossiê (SHA-256) | `{d.DossierHash}` |");
        if (root.TryGetProperty("governance", out var g))
        {
            md.AppendLine($"| Modo de aprovação | {Get(g, "mode")} ({Get(g, "policy")}) |");
            md.AppendLine($"| Natureza da aprovação | {Get(g, "nature")} |");
        }
        if (root.TryGetProperty("impactAnalysis", out var impact))
        {
            md.AppendLine($"| Diferença de base | {Get(impact, "baseDifference")} |");
            md.AppendLine($"| Diferença de imposto calculado | {Get(impact, "calculatedTaxDifference")} |");
            md.AppendLine($"| Efeito financeiro | {Get(impact, "financialEffect")} |");
        }
        md.AppendLine();
        Section(md, "Regras e versões", root, "rules", r => $"{Get(r, "ruleKey")} v{Get(r, "ruleVersion")} — `{Get(r, "contentHash")}`");
        Section(md, "Fontes jurídicas", root, "legalSources", s => $"{Get(s, "code")}: {Get(s, "title")}, {Get(s, "reference")} (texto: {Get(s, "textVerification")})");
        Section(md, "Requisitos", root, "requirements", r => $"[{(Get(r, "met") == "True" ? "x" : " ")}] {Get(r, "text")} ({Get(r, "phase")})");
        Section(md, "Evidências", root, "evidence", e => $"{Get(e, "type")} v{Get(e, "version")} — {Get(e, "description")} — {Get(e, "status")} — `{Get(e, "contentHash")}` ({Get(e, "hashKind")})");
        Section(md, "Pendências", root, "pendingIssues", p => p.GetString() ?? "");
        if (x is not null)
        {
            md.AppendLine("## Verificação da execução");
            md.AppendLine();
            md.AppendLine($"Resultado: **{x.Outcome}** por {x.VerifiedByName} em {x.VerifiedAt:yyyy-MM-dd HH:mm} UTC. Hash: `{x.DossierHash}`.");
            md.AppendLine();
        }
        md.AppendLine("---");
        md.AppendLine();
        md.AppendLine(Get(root, "integrityNotice"));
        return md.ToString();
    }

    private static void Section(StringBuilder md, string title, JsonElement root, string property, Func<JsonElement, string> line)
    {
        if (!root.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) return;
        md.AppendLine($"## {title}");
        md.AppendLine();
        foreach (var item in items.EnumerateArray()) md.AppendLine($"- {line(item)}");
        md.AppendLine();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
