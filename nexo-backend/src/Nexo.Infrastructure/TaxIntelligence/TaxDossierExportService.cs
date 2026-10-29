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
/// version, the execution verification when there is one, the legal sources and the evidence index it froze, the evidence files when
/// asked for and allowed, and a manifest with the SHA-256 of every file in the package. Only this company's dossiers are found; a file
/// whose bytes no longer match its recorded hash is reported in the manifest, never silently included.
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
        var evidence = root.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array ? ev : default;
        if (evidence.ValueKind == JsonValueKind.Array)
            entries.Add(("evidencias/indice.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evidence, Pretty))));

        var problems = new List<string>();
        if (includeFiles && evidence.ValueKind == JsonValueKind.Array)
            await AddFilesAsync(evidence, entries, problems, ct);

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

    /// <summary>Adds each evidence file the dossier froze, checking its bytes against the SHA-256 recorded at upload.</summary>
    private async Task AddFilesAsync(JsonElement evidence, List<(string Path, byte[] Bytes)> entries, List<string> problems, CancellationToken ct)
    {
        var ids = evidence.EnumerateArray()
            .Select(e => e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("id", out var id) ? id.GetGuid() : (Guid?)null)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var files = await _ctx.Db.TaxEvidenceFiles.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        foreach (var f in files)
        {
            try
            {
                await using var stream = await _files.OpenAsync(f.StorageKey, ct);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                var bytes = buffer.ToArray();
                if (!string.Equals(Sha(bytes), f.Sha256, StringComparison.Ordinal))
                {
                    problems.Add($"Arquivo {f.Id}: os bytes armazenados não conferem com o SHA-256 registrado — não incluído.");
                    continue;
                }
                entries.Add(($"evidencias/arquivos/{f.Sha256}{f.Extension}", bytes));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problems.Add($"Arquivo {f.Id}: não foi possível ler do armazenamento ({ex.GetType().Name}).");
            }
        }
        problems.AddRange(ids.Except(files.Select(f => f.Id)).Select(id => $"Arquivo {id}: registro não encontrado."));
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
