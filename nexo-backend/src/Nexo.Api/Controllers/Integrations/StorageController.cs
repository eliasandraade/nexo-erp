using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Api.Authorization;

namespace Nexo.Api.Controllers.Integrations;

/// <summary>
/// File uploads. Each context says where the file goes, who may send it and whether it is public:
/// <list type="bullet">
/// <item>public (branding, product images shown on the public menu/portal): stored under the tenant
/// folder and served by the public base URL;</item>
/// <item>private (service records, site photos): stored under <c>private/</c> — in the private bucket
/// when one is configured — and only ever handed out as signed URLs that expire in minutes.</item>
/// </list>
/// The file's first bytes must match its declared type; the original name is never used in the key.
/// </summary>
[ApiController]
[Route("api/integrations/storage")]
[Authorize]
public class StorageController : ControllerBase
{
    /// <param name="NeedsPrivateBucket">Too sensitive to sit in a public bucket even unlisted (clinical-like
    /// records): in production, refused until the private bucket is configured.</param>
    private sealed record UploadContext(string Folder, bool IsPrivate, string[] Roles, bool NeedsPrivateBucket = false);

    private static readonly string[] Stock = AppRoles.Stock.Split(',');
    private static readonly string[] Management = AppRoles.Management.Split(',');

    private static readonly Dictionary<string, UploadContext> Contexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["product-image"]        = new("products", false, Stock),
            ["restaurant-logo"]      = new("restaurant/logo", false, Management),
            ["restaurant-cover"]     = new("restaurant/cover", false, Management),
            ["service-portal-logo"]  = new("service/portal/logo", false, Management),
            ["service-portal-cover"] = new("service/portal/cover", false, Management),
            ["build-daily-log"]      = new("build/daily-logs", true, Management),
            ["build-portal-logo"]    = new("build/portal/logo", false, Management),
            ["build-portal-cover"]   = new("build/portal/cover", false, Management),
            ["build-selection-option"] = new("build/selections", true, Management),
            ["build-quality"]        = new("build/quality", true, Management),
            ["build-handover"]       = new("build/handover", true, Management),
            ["build-assistance"]     = new("build/assistance", true, Management),
            ["build-reality"]        = new("build/reality", true, Management),
            ["service-record"]       = new("service/records", true, Management, NeedsPrivateBucket: true),
        };

    // MIME type → file extension
    private static readonly Dictionary<string, string> Extensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]      = ".jpg",
            ["image/png"]       = ".png",
            ["image/webp"]      = ".webp",
            ["application/pdf"] = ".pdf",
        };

    private readonly IStorageProvider         _storage;
    private readonly IPrivateFileUrls         _privateUrls;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly StorageOptions           _opts;
    private readonly ICurrentTenant           _tenant;
    private readonly IHostEnvironment         _env;
    private readonly ILogger<StorageController> _logger;

    public StorageController(
        IStorageProvider storage,
        IPrivateFileUrls privateUrls,
        IIntegrationFeatureFlags flags,
        IOptions<StorageOptions> opts,
        ICurrentTenant tenant,
        IHostEnvironment env,
        ILogger<StorageController> logger)
    {
        _storage     = storage;
        _privateUrls = privateUrls;
        _flags       = flags;
        _opts        = opts.Value;
        _tenant      = tenant;
        _env         = env;
        _logger      = logger;
    }

    /// <summary>
    /// Upload a file. Form fields: file, context. Public contexts answer { key, publicUrl };
    /// private ones answer { key, url } where url is signed and short-lived (never a public URL).
    /// 404 when storage is off.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = AppRoles.Stock)]   // the widest set; each context narrows it below
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string context,
        CancellationToken ct)
    {
        if (!_flags.StorageEnabled)
            return NotFound(new { error = "Serviço de armazenamento não está habilitado." });

        if (!Contexts.TryGetValue(context, out var target))
            return BadRequest(new { error = $"Contexto inválido: '{context}'." });

        if (!target.Roles.Any(User.IsInRole))
            return Forbid();

        if (target.NeedsPrivateBucket && string.IsNullOrWhiteSpace(_opts.R2.PrivateBucketName) && _env.IsProduction())
            return StatusCode(503, new { error = "Armazenamento privado ainda não configurado. Anexos de prontuário ficam indisponíveis até lá." });

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!_opts.AllowedContentTypes.Contains(contentType) || !Extensions.TryGetValue(contentType, out var ext))
            return BadRequest(new { error = $"Tipo de arquivo não permitido: '{contentType}'." });

        var maxBytes = _opts.MaxFileSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            return BadRequest(new { error = $"Arquivo muito grande. Máximo: {_opts.MaxFileSizeMb}MB." });

        if (file.Length == 0)
            return BadRequest(new { error = "Arquivo vazio não é permitido." });

        if (!await FileSignature.MatchesAsync(file, contentType, ct))
            return BadRequest(new { error = "O conteúdo do arquivo não corresponde ao tipo informado." });

        var objectKey = StorageKeys.Build(_tenant.Id, target.Folder, Guid.NewGuid().ToString("N") + ext, target.IsPrivate);

        _logger.LogInformation(
            "[Storage] Upload — context={Context}, tenant={TenantId}, key={Key}",
            context, _tenant.Id, objectKey);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _storage.UploadAsync(
                new StorageUploadRequest(stream, file.FileName, contentType, objectKey, file.Length), ct);

            return target.IsPrivate
                ? Ok(new { key = result.Key, url = _privateUrls.For(result.Key) })
                : Ok(new { key = result.Key, publicUrl = result.PublicUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Storage] Upload failed — key={Key}", objectKey);
            return StatusCode(503, new { error = "Serviço de armazenamento indisponível. Tente novamente." });
        }
    }

    /// <summary>A fresh signed URL (15 minutes) for one of this tenant's private files.</summary>
    [HttpGet("signed-url")]
    [Authorize(Roles = AppRoles.Management)]
    public IActionResult SignedUrl([FromQuery] string key)
    {
        if (!_flags.StorageEnabled)
            return NotFound(new { error = "Serviço de armazenamento não está habilitado." });

        if (!StorageKeys.IsPrivate(key ?? string.Empty) || !StorageKeys.IsOfTenant(key, _tenant.Id))
            return Forbid();

        var url = _privateUrls.For(key);
        return url is null
            ? StatusCode(503, new { error = "Serviço de armazenamento indisponível. Tente novamente." })
            : Ok(new { key, url });
    }

    /// <summary>
    /// Delete a file by key. The key must be in this tenant's folder (public or private).
    /// 404 when storage is off.
    /// </summary>
    [HttpDelete("{*key}")]
    [Authorize(Roles = AppRoles.Stock)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        if (!_flags.StorageEnabled)
            return NotFound(new { error = "Serviço de armazenamento não está habilitado." });

        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Chave inválida." });

        if (!StorageKeys.IsOfTenant(key, _tenant.Id))
            return Forbid();

        // who may delete is who may upload to that folder (unknown folders: management only)
        var roles = RolesForKey(key) ?? Management;
        if (!roles.Any(User.IsInRole))
            return Forbid();

        try
        {
            await _storage.DeleteAsync(key, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Storage] Delete failed — key={Key}", key);
            return StatusCode(503, new { error = "Falha ao excluir arquivo." });
        }
    }

    private string[]? RolesForKey(string key)
    {
        var rest = StorageKeys.IsPrivate(key) ? key[StorageKeys.PrivatePrefix.Length..] : key;
        rest = rest[$"tenants/{_tenant.Id}/".Length..];
        return Contexts.Values
            .Where(c => c.IsPrivate == StorageKeys.IsPrivate(key) && rest.StartsWith(c.Folder + "/", StringComparison.Ordinal))
            .Select(c => c.Roles)
            .FirstOrDefault();
    }
}

/// <summary>First bytes of each allowed type — the declared content type is the client's word.</summary>
internal static class FileSignature
{
    private static readonly Dictionary<string, Func<byte[], bool>> Checks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"]      = b => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        ["image/png"]       = b => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                                   && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,
        ["image/webp"]      = b => b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                                   && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P',
        ["application/pdf"] = b => b.Length >= 5 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F' && b[4] == '-',
        // video (Build reality capture): ISO BMFF 'ftyp' box at offset 4 (MP4 and QuickTime), EBML header (WebM)
        ["video/mp4"]       = b => b.Length >= 8 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p',
        ["video/quicktime"] = b => b.Length >= 8 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p',
        ["video/webm"]      = b => b.Length >= 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3,
    };

    public static bool Matches(byte[] head, string contentType)
        => Checks.TryGetValue(contentType, out var check) && check(head);

    public static async Task<bool> MatchesAsync(IFormFile file, string contentType, CancellationToken ct)
    {
        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        return Matches(head[..read], contentType);
    }
}
