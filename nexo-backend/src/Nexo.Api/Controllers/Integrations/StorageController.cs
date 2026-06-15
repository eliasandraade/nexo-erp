using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;

namespace Nexo.Api.Controllers.Integrations;

[ApiController]
[Route("api/integrations/storage")]
[Authorize]
public class StorageController : ControllerBase
{
    // Valid upload contexts → sub-path used in object key
    private static readonly Dictionary<string, string> ContextPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["product-image"]    = "products",
            ["restaurant-logo"]  = "restaurant/logo",
            ["restaurant-cover"] = "restaurant/cover",
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
    private readonly IIntegrationFeatureFlags _flags;
    private readonly StorageOptions           _opts;
    private readonly ICurrentTenant           _tenant;
    private readonly ILogger<StorageController> _logger;

    public StorageController(
        IStorageProvider storage,
        IIntegrationFeatureFlags flags,
        IOptions<StorageOptions> opts,
        ICurrentTenant tenant,
        ILogger<StorageController> logger)
    {
        _storage = storage;
        _flags   = flags;
        _opts    = opts.Value;
        _tenant  = tenant;
        _logger  = logger;
    }

    /// <summary>
    /// Upload a file to storage.
    /// Returns 404 when StorageEnabled feature flag is false.
    /// Form fields: file (IFormFile), context (string).
    /// Valid contexts: product-image, restaurant-logo, restaurant-cover.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string context,
        CancellationToken ct)
    {
        // Feature flag gate — 404 when storage is not enabled
        if (!_flags.StorageEnabled)
            return NotFound(new { error = "Serviço de armazenamento não está habilitado." });

        // Validate context
        if (!ContextPaths.TryGetValue(context, out var pathSegment))
            return BadRequest(new { error = $"Contexto inválido: '{context}'." });

        // Validate content-type
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!_opts.AllowedContentTypes.Contains(contentType))
            return BadRequest(new { error = $"Tipo de arquivo não permitido: '{contentType}'." });

        // Validate size
        var maxBytes = _opts.MaxFileSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            return BadRequest(new { error = $"Arquivo muito grande. Máximo: {_opts.MaxFileSizeMb}MB." });

        if (file.Length == 0)
            return BadRequest(new { error = "Arquivo vazio não é permitido." });

        // Generate safe object key — never use the original filename
        if (!Extensions.TryGetValue(contentType, out var ext))
            ext = ".bin";

        var fileId    = Guid.NewGuid().ToString("N");
        var objectKey = $"tenants/{_tenant.Id}/{pathSegment}/{fileId}{ext}";

        _logger.LogInformation(
            "[Storage] Upload — context={Context}, tenant={TenantId}, key={Key}",
            context, _tenant.Id, objectKey);

        try
        {
            await using var stream  = file.OpenReadStream();
            var uploadRequest       = new StorageUploadRequest(stream, file.FileName, contentType, objectKey, file.Length);
            var result              = await _storage.UploadAsync(uploadRequest, ct);

            return Ok(new { key = result.Key, publicUrl = result.PublicUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Storage] Upload failed — key={Key}", objectKey);
            return StatusCode(503, new { error = "Serviço de armazenamento indisponível. Tente novamente." });
        }
    }

    /// <summary>
    /// Delete a file from storage by its object key.
    /// Returns 404 when StorageEnabled feature flag is false.
    /// The key must belong to the current tenant (prefix tenants/{tenantId}/).
    /// </summary>
    [HttpDelete("{*key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        // Feature flag gate
        if (!_flags.StorageEnabled)
            return NotFound(new { error = "Serviço de armazenamento não está habilitado." });

        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Chave inválida." });

        // Security: key must belong to this tenant
        var tenantPrefix = $"tenants/{_tenant.Id}/";
        if (!key.StartsWith(tenantPrefix, StringComparison.OrdinalIgnoreCase))
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
}
