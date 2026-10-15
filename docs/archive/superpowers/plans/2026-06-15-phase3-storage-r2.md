# Phase 3 — Storage com Cloudflare R2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement real file storage via Cloudflare R2 (S3-compatible) — upload de imagens de produto, logo e capa do portal de restaurante, via backend exclusivamente, com feature flag obrigatória.

**Architecture:** `IStorageProvider` genérico na camada Application (Integrations), `CloudflareR2Provider` na Infrastructure usando AWSSDK.S3 com endpoint customizado R2. Upload passa sempre pelo backend (`POST /api/integrations/storage/upload`). `StorageController` verifica `IIntegrationFeatureFlags.StorageEnabled` antes de qualquer operação — retorna 404 se false. Frontend usa `apiClient.postForm` e o `ImageUploadButton` compartilhado. Upload de imagem de produto é permitido **somente em modo edição** (produto existente) para evitar arquivos órfãos. Ao completar upload, `patchProductImage` é chamado imediatamente para persistir. Portal restaurante: upload gera URL → estado → `updatePortalInfo` persiste ao salvar.

**Tech Stack:** AWSSDK.S3 3.7.x, .NET 8, IFormFile (multipart), React + TypeScript, shadcn/ui, sonner toast, apiClient.postForm (já existe em api-client.ts)

---

## Decisões de design (fixas — não alterar)

| Decisão | Escolha | Motivo |
|---------|---------|--------|
| Feature flag `StorageEnabled` | **Obrigatória** — 404 se false | Nenhuma integração experimental ativa sem flag |
| `StorageEnabled` padrão | `false` em appsettings.json | Sem credenciais reais → upload quebraria |
| Produto novo sem imageId | **Opção A** — upload só em edit mode | Evita arquivo órfão se usuário cancelar |
| Produto existente | Upload imediato + `patchProductImage` chamado no handler | Imagem persiste no banco, não só no estado |
| Portal restaurante | Upload → estado → `updatePortalInfo` ao salvar | Fluxo natural do formulário já existente |
| Delete: segurança de key | Validar prefixo `tenants/{tenantId}/` no controller | Tenant não pode deletar arquivo de outro tenant |

---

## Mapa de arquivos

**Criar:**
- `nexo-backend/src/Nexo.Application/Integrations/Contracts/IStorageProvider.cs`
- `nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadRequest.cs`
- `nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadResult.cs`
- `nexo-backend/src/Nexo.Application/Integrations/Options/StorageOptions.cs`
- `nexo-backend/src/Nexo.Infrastructure/Integrations/Storage/CloudflareR2Provider.cs`
- `nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs`
- `nexo-main/src/services/storage.api.ts`
- `nexo-main/src/components/shared/ImageUploadButton.tsx`
- `nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs`

**Modificar:**
- `nexo-backend/src/Nexo.Infrastructure/Nexo.Infrastructure.csproj` — add AWSSDK.S3
- `nexo-backend/src/Nexo.Infrastructure/Integrations/DependencyInjection.cs` — register StorageOptions + CloudflareR2Provider
- `nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs` — add PATCH /{id}/image
- `nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs` — add SetImageUrlAsync
- `nexo-backend/src/Nexo.Api/appsettings.json` — add Storage section (StorageEnabled=false)
- `nexo-main/src/modules/products/types/index.ts` — add imageUrl to ProductDto + Product
- `nexo-main/src/modules/products/api/products.api.ts` — add patchProductImage
- `nexo-main/src/modules/products/components/ProductMainDataSection.tsx` — image upload only in edit mode
- `nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx` — logo + cover upload

---

## Task 1: Application — IStorageProvider + DTOs + StorageOptions

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Integrations/Contracts/IStorageProvider.cs`
- Create: `nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadRequest.cs`
- Create: `nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadResult.cs`
- Create: `nexo-backend/src/Nexo.Application/Integrations/Options/StorageOptions.cs`

- [ ] **Step 1: Create IStorageProvider**

`nexo-backend/src/Nexo.Application/Integrations/Contracts/IStorageProvider.cs`:
```csharp
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Application.Integrations.Contracts;

public interface IStorageProvider
{
    /// <summary>
    /// Uploads a file and returns the storage key and public URL.
    /// Throws on failure — caller handles the exception.
    /// </summary>
    Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file by key. Tolerant — does not throw if key not found.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Verifies storage backend is reachable.</summary>
    Task PingAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create StorageUploadRequest**

`nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadRequest.cs`:
```csharp
namespace Nexo.Application.Integrations.DTOs;

public sealed record StorageUploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    string ObjectKey,
    long   ContentLength);
```

- [ ] **Step 3: Create StorageUploadResult**

`nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadResult.cs`:
```csharp
namespace Nexo.Application.Integrations.DTOs;

public sealed record StorageUploadResult(
    string Key,
    string PublicUrl);
```

- [ ] **Step 4: Create StorageOptions**

`nexo-backend/src/Nexo.Application/Integrations/Options/StorageOptions.cs`:
```csharp
namespace Nexo.Application.Integrations.Options;

public sealed class StorageOptions
{
    public const string SectionKey = "Integrations:Storage";

    public string Provider { get; init; } = "R2";

    /// <summary>Maximum allowed upload size in MB.</summary>
    public int MaxFileSizeMb { get; init; } = 10;

    /// <summary>Allowed MIME types for upload.</summary>
    public string[] AllowedContentTypes { get; init; } =
        ["image/jpeg", "image/png", "image/webp"];

    public R2Options R2 { get; init; } = new();
}

public sealed class R2Options
{
    public string AccountId       { get; init; } = string.Empty;
    public string AccessKeyId     { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string BucketName      { get; init; } = "orken-assets";
    /// <summary>Public base URL, e.g. https://assets.orken.com.br</summary>
    public string PublicUrl       { get; init; } = string.Empty;
}
```

- [ ] **Step 5: Build Application project**

Run from repo root:
```
dotnet build nexo-backend/src/Nexo.Application/Nexo.Application.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```
git add nexo-backend/src/Nexo.Application/Integrations/Contracts/IStorageProvider.cs
git add nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadRequest.cs
git add nexo-backend/src/Nexo.Application/Integrations/DTOs/StorageUploadResult.cs
git add nexo-backend/src/Nexo.Application/Integrations/Options/StorageOptions.cs
git commit -m "feat(storage): add IStorageProvider contract, upload/result DTOs, and StorageOptions"
```

---

## Task 2: Infrastructure — AWSSDK.S3 + CloudflareR2Provider + DI

**Files:**
- Modify: `nexo-backend/src/Nexo.Infrastructure/Nexo.Infrastructure.csproj`
- Create: `nexo-backend/src/Nexo.Infrastructure/Integrations/Storage/CloudflareR2Provider.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Integrations/DependencyInjection.cs`

- [ ] **Step 1: Add AWSSDK.S3 to Infrastructure csproj**

Open `nexo-backend/src/Nexo.Infrastructure/Nexo.Infrastructure.csproj`.
Inside the existing `<ItemGroup>` with other `PackageReference` entries, add:
```xml
<PackageReference Include="AWSSDK.S3" Version="3.7.414.0" />
```

Run:
```
dotnet restore nexo-backend/src/Nexo.Infrastructure/Nexo.Infrastructure.csproj
```
Expected: no errors.

- [ ] **Step 2: Create CloudflareR2Provider**

`nexo-backend/src/Nexo.Infrastructure/Integrations/Storage/CloudflareR2Provider.cs`:
```csharp
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations.Storage;

public sealed class CloudflareR2Provider : IStorageProvider
{
    private readonly AmazonS3Client  _s3;
    private readonly StorageOptions  _opts;
    private readonly ILogger<CloudflareR2Provider> _logger;

    public CloudflareR2Provider(IOptions<StorageOptions> opts, ILogger<CloudflareR2Provider> logger)
    {
        _opts   = opts.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL           = $"https://{_opts.R2.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle       = true,
            AuthenticationRegion = "auto",
        };
        var credentials = new BasicAWSCredentials(_opts.R2.AccessKeyId, _opts.R2.SecretAccessKey);
        _s3 = new AmazonS3Client(credentials, config);
    }

    public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("[Storage] Uploading {Key} ({ContentType}, {Length}B)",
            request.ObjectKey, request.ContentType, request.ContentLength);

        var putRequest = new PutObjectRequest
        {
            BucketName            = _opts.R2.BucketName,
            Key                   = request.ObjectKey,
            InputStream           = request.Content,
            ContentType           = request.ContentType,
            DisablePayloadSigning = true,
            UseChunkEncoding      = false,
        };

        await _s3.PutObjectAsync(putRequest, ct);

        var publicUrl = $"{_opts.R2.PublicUrl.TrimEnd('/')}/{request.ObjectKey}";

        _logger.LogInformation("[Storage] Uploaded {Key} → {PublicUrl}", request.ObjectKey, publicUrl);

        return new StorageUploadResult(request.ObjectKey, publicUrl);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _logger.LogInformation("[Storage] Deleting {Key}", key);
        try
        {
            await _s3.DeleteObjectAsync(_opts.R2.BucketName, key, ct);
            _logger.LogInformation("[Storage] Deleted {Key}", key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[Storage] Delete: key not found {Key}", key);
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        await _s3.ListBucketsAsync(ct);
    }
}
```

- [ ] **Step 3: Register StorageOptions and CloudflareR2Provider in DI**

Open `nexo-backend/src/Nexo.Infrastructure/Integrations/DependencyInjection.cs`.

At the top, add this using:
```csharp
using Nexo.Infrastructure.Integrations.Storage;
```

Before the final `return services;`, add:
```csharp
// ── Storage ───────────────────────────────────────────────────────────────────
services.Configure<StorageOptions>(
    configuration.GetSection(StorageOptions.SectionKey));
services.AddSingleton<IStorageProvider, CloudflareR2Provider>();
```

- [ ] **Step 4: Build full solution**

```
dotnet build nexo-backend/Nexo.sln
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add nexo-backend/src/Nexo.Infrastructure/Nexo.Infrastructure.csproj
git add nexo-backend/src/Nexo.Infrastructure/Integrations/Storage/CloudflareR2Provider.cs
git add nexo-backend/src/Nexo.Infrastructure/Integrations/DependencyInjection.cs
git commit -m "feat(storage): add CloudflareR2Provider with AWSSDK.S3, register in DI"
```

---

## Task 3: API — StorageController + PATCH products/{id}/image + appsettings.json

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs`
- Modify: `nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs`
- Modify: `nexo-backend/src/Nexo.Api/appsettings.json`

### StorageController — regras obrigatórias

- Verificar `IIntegrationFeatureFlags.StorageEnabled` no início de CADA action (`Upload` e `Delete`). Se false → retornar `NotFound()` (padrão do projeto para feature desabilitada).
- Gerar `objectKey` sem nunca usar o filename original do cliente.
- Validar que key no `Delete` começa com `tenants/{tenantId}/` — `Forbid()` se não.
- Nunca logar `SecretAccessKey`, `AccessKeyId`, nem headers de Authorization.

- [ ] **Step 1: Create StorageController**

`nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs`:
```csharp
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
```

- [ ] **Step 2: Add SetImageUrlAsync to ProductService**

Open `nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs`.

First, read the file to find the existing `MapToDto` method. Verify it includes `ImageUrl = product.ImageUrl`. If missing, add it to the mapping.

Then add this method to the `ProductService` class (before the closing `}`):
```csharp
public async Task<ProductDto> SetImageUrlAsync(Guid id, string? imageUrl, CancellationToken ct = default)
{
    var product = await _products.GetByIdAsync(id, ct)
        ?? throw new NotFoundException("Product", id);

    // Treat empty string as null — remove image
    var cleanUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
    product.SetImageUrl(cleanUrl);
    await _products.SaveChangesAsync(ct);

    return MapToDto(product);
}
```

- [ ] **Step 3: Add PATCH /{id}/image to ProductsController**

Open `nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs`.

Add this record before the class declaration (after the using statements, or at the top of the namespace):
```csharp
public record SetProductImageRequest(string? ImageUrl);
```

Add this action inside the class body, after the `Deactivate` action:
```csharp
[HttpPatch("{id:guid}/image")]
public async Task<ActionResult<ProductDto>> SetImage(
    Guid id,
    [FromBody] SetProductImageRequest request,
    CancellationToken ct)
    => Ok(await _service.SetImageUrlAsync(id, request.ImageUrl, ct));
```

- [ ] **Step 4: Update appsettings.json**

Open `nexo-backend/src/Nexo.Api/appsettings.json`.

**DO NOT change `StorageEnabled` to true.** It must remain `false`.

Inside the `"Integrations"` object, after the existing `"Resilience"` block, add:
```json
"Storage": {
  "Provider": "R2",
  "MaxFileSizeMb": 10,
  "AllowedContentTypes": ["image/jpeg", "image/png", "image/webp"],
  "R2": {
    "AccountId": "",
    "AccessKeyId": "",
    "SecretAccessKey": "",
    "BucketName": "orken-assets",
    "PublicUrl": ""
  }
}
```

`StorageEnabled` stays at `false`. To enable in production/staging, set via environment variable:
```
Integrations__Features__StorageEnabled=true
```

- [ ] **Step 5: Build solution**

```
dotnet build nexo-backend/Nexo.sln
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs
git add nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs
git add nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs
git add nexo-backend/src/Nexo.Api/appsettings.json
git commit -m "feat(storage): StorageController with feature flag gate, PATCH /products/{id}/image"
```

---

## Task 4: Frontend — types + storage.api.ts + products.api.ts + ImageUploadButton

**Files:**
- Create: `nexo-main/src/services/storage.api.ts`
- Create: `nexo-main/src/components/shared/ImageUploadButton.tsx`
- Modify: `nexo-main/src/modules/products/types/index.ts`
- Modify: `nexo-main/src/modules/products/api/products.api.ts`

### Comportamento do ImageUploadButton

- Aceita `context: StorageContext`, `value: string | null | undefined`, `onChange: (url: string | null) => void`
- Valida content-type e tamanho **no cliente** antes de enviar (UX rápida)
- Chama `uploadFile()` do `storage.api.ts` — nunca chama R2 diretamente
- `onChange(publicUrl)` apenas quando upload retorna com sucesso
- `onChange(null)` quando usuário clica em remover
- Não é responsável por persistir — quem consome decide o que fazer com `publicUrl`

- [ ] **Step 1: Add imageUrl to ProductDto and Product in types/index.ts**

Open `nexo-main/src/modules/products/types/index.ts`.

In the `ProductDto` interface, after `updatedAt: string;`, add:
```typescript
  imageUrl: string | null;
```

In the `Product` interface (form model), after the last field `maxStockQuantity: number | null;`, add:
```typescript
  imageUrl: string | null;
```

- [ ] **Step 2: Add patchProductImage to products.api.ts**

Open `nexo-main/src/modules/products/api/products.api.ts`.

At the end of the file, add:
```typescript
export function patchProductImage(id: string, imageUrl: string | null): Promise<ProductDto> {
  return apiClient.patch<ProductDto>(`/products/${id}/image`, { imageUrl });
}
```

- [ ] **Step 3: Create storage.api.ts**

`nexo-main/src/services/storage.api.ts`:
```typescript
import { apiClient } from "@/services/api-client";

export type StorageContext =
  | "product-image"
  | "restaurant-logo"
  | "restaurant-cover";

export interface StorageUploadResult {
  key: string;
  publicUrl: string;
}

/**
 * Uploads a file via backend storage endpoint.
 * Never calls R2/external storage directly.
 */
export async function uploadFile(
  file: File,
  context: StorageContext
): Promise<StorageUploadResult> {
  const form = new FormData();
  form.append("file", file);
  form.append("context", context);
  return apiClient.postForm<StorageUploadResult>("/integrations/storage/upload", form);
}

export async function deleteFile(key: string): Promise<void> {
  return apiClient.delete<void>(`/integrations/storage/${encodeURIComponent(key)}`);
}
```

- [ ] **Step 4: Create ImageUploadButton**

`nexo-main/src/components/shared/ImageUploadButton.tsx`:
```tsx
import { useRef, useState } from "react";
import { Loader2, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { uploadFile, type StorageContext } from "@/services/storage.api";

interface Props {
  context: StorageContext;
  value: string | null | undefined;
  onChange: (url: string | null) => void;
  label?: string;
  accept?: string;
  maxMb?: number;
}

const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];

export function ImageUploadButton({
  context,
  value,
  onChange,
  label = "Imagem",
  accept = "image/jpeg,image/png,image/webp",
  maxMb = 10,
}: Props) {
  const [loading, setLoading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Reset input so the same file can be reselected after removal
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;

    if (!ALLOWED_TYPES.includes(file.type)) {
      toast.error("Formato não permitido. Use JPG, PNG ou WebP.");
      return;
    }
    if (file.size > maxMb * 1024 * 1024) {
      toast.error(`Arquivo muito grande. Máximo: ${maxMb}MB.`);
      return;
    }
    if (file.size === 0) {
      toast.error("Arquivo vazio não é permitido.");
      return;
    }

    setLoading(true);
    try {
      const result = await uploadFile(file, context);
      onChange(result.publicUrl);
      toast.success(`${label} enviada com sucesso.`);
    } catch {
      toast.error(`Falha ao enviar ${label.toLowerCase()}. Tente novamente.`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-2">
      {value && (
        <div className="relative inline-block">
          <img
            src={value}
            alt={label}
            className="h-24 w-24 rounded-md object-cover border border-border"
          />
          <button
            type="button"
            onClick={() => onChange(null)}
            className="absolute -top-1.5 -right-1.5 rounded-full bg-destructive text-destructive-foreground h-5 w-5 flex items-center justify-center"
            title={`Remover ${label.toLowerCase()}`}
          >
            <X className="h-3 w-3" />
          </button>
        </div>
      )}
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="hidden"
        onChange={handleFileChange}
      />
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={loading}
        onClick={() => inputRef.current?.click()}
      >
        {loading
          ? <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          : <Upload className="mr-2 h-4 w-4" />}
        {value ? `Trocar ${label.toLowerCase()}` : `Adicionar ${label.toLowerCase()}`}
      </Button>
    </div>
  );
}
```

- [ ] **Step 5: tsc check**

In `nexo-main/`:
```
npx tsc --noEmit
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add nexo-main/src/services/storage.api.ts
git add nexo-main/src/components/shared/ImageUploadButton.tsx
git add nexo-main/src/modules/products/types/index.ts
git add nexo-main/src/modules/products/api/products.api.ts
git commit -m "feat(storage): storage.api.ts, ImageUploadButton, product type + api updates"
```

---

## Task 5: Frontend — Product image upload (edit mode only)

**Files:**
- Modify: `nexo-main/src/modules/products/components/ProductMainDataSection.tsx`

### Regra obrigatória: upload somente em modo edição

`ProductMainDataSection` recebe `data: Partial<Product>` e `onChange: (field: string, value: unknown) => void`. O componente PAI é `ProductForm`, que é usado tanto em criação quanto em edição.

Problema: se o usuário fizer upload antes de salvar um produto novo e cancelar, cria arquivo órfão sem `productId` vinculado. Solução: só exibir `ImageUploadButton` quando `data.id` existe (produto salvo).

Fluxo de persistência em produto existente:
1. Usuário clica no botão de upload
2. `ImageUploadButton` faz upload → recebe `publicUrl`
3. `ImageUploadButton` chama `onChange(publicUrl)` — que atualiza estado local via `onChange("imageUrl", url)` no `ProductMainDataSection`
4. `ProductMainDataSection` TAMBÉM chama `patchProductImage(data.id, publicUrl)` imediatamente para persistir no banco
5. Se `patchProductImage` falhar, mostrar `toast.error`

Isso garante que a imagem é persistida sem precisar que o usuário clique em "Salvar" novamente.

- [ ] **Step 1: Add imports to ProductMainDataSection**

Open `nexo-main/src/modules/products/components/ProductMainDataSection.tsx`.

Add at the top:
```tsx
import { useState } from "react";
import { toast } from "sonner";
import { ImageUploadButton } from "@/components/shared/ImageUploadButton";
import { patchProductImage } from "../api/products.api";
```

- [ ] **Step 2: Add image upload handler inside the component**

Inside the `ProductMainDataSection` function body, before the `return` statement, add:
```tsx
  const handleImageChange = async (url: string | null) => {
    onChange("imageUrl", url);
    if (!data.id) return; // safety guard — should not happen in edit mode
    try {
      await patchProductImage(data.id, url);
    } catch {
      toast.error("Falha ao salvar imagem. Tente novamente.");
    }
  };
```

- [ ] **Step 3: Add image upload block in the JSX (edit mode only)**

Inside the returned `<div className="grid ...">`, add this block at the END (before the closing `</div>`):
```tsx
      {data.id && (
        <div className="space-y-1.5 col-span-full">
          <Label>Imagem do produto</Label>
          <ImageUploadButton
            context="product-image"
            value={data.imageUrl ?? null}
            onChange={handleImageChange}
            label="Imagem"
          />
        </div>
      )}
```

- [ ] **Step 4: tsc check**

```
npx tsc --noEmit
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add nexo-main/src/modules/products/components/ProductMainDataSection.tsx
git commit -m "feat(storage): product image upload in edit mode only, persists via patchProductImage"
```

---

## Task 6: Frontend — Logo e capa no PortalSetupPage

**Files:**
- Modify: `nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx`

### Fluxo obrigatório de persistência

1. Usuário faz upload → `ImageUploadButton` chama `onChange(publicUrl)` → estado local atualizado
2. Estado local alimenta o payload de `updatePortalInfo`
3. Usuário clica "Salvar" → `PUT /api/restaurante/settings/portal` persiste `logoUrl` + `coverImageUrl`
4. Imagem não fica somente local — só existe risco de não salvar se o usuário fechar sem clicar em "Salvar"

Este fluxo é aceitável porque o formulário de portal é explicitamente salvo pelo usuário, diferente do produto onde o upload precisa persistir imediatamente.

- [ ] **Step 1: Read the full PortalSetupPage.tsx**

Read `nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx` in full. Identify:
- The state variable(s) for `logoUrl` and `coverImageUrl` (look for `useState` or form object state)
- Where `logoUrl` and `coverImageUrl` are used in the JSX (probably `<Input>` fields)
- How they are passed to `updatePortalInfo` (either directly or via a form object)

- [ ] **Step 2: Add import for ImageUploadButton**

At the top of `PortalSetupPage.tsx`, add:
```tsx
import { ImageUploadButton } from "@/components/shared/ImageUploadButton";
```

- [ ] **Step 3: Replace logoUrl and coverImageUrl inputs with ImageUploadButton**

Find where `logoUrl` renders (likely an `<Input>` for a URL string). Replace each one:

For the logo field, replace the existing `<Input>` with:
```tsx
<ImageUploadButton
  context="restaurant-logo"
  value={/* existing state variable for logoUrl */}
  onChange={/* existing state setter for logoUrl */}
  label="Logo"
/>
```

For the cover image field, replace with:
```tsx
<ImageUploadButton
  context="restaurant-cover"
  value={/* existing state variable for coverImageUrl */}
  onChange={/* existing state setter for coverImageUrl */}
  label="Capa"
/>
```

Use the exact variable names already in the file. Do NOT rename existing state variables.

- [ ] **Step 4: tsc check**

```
npx tsc --noEmit
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx
git commit -m "feat(storage): logo and cover image upload in PortalSetupPage"
```

---

## Task 7: Tests + build + type-check final

**Files:**
- Create: `nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs`

### Test cases obrigatórios

| # | Test | Verifica |
|---|------|----------|
| 1 | `Upload_StorageDisabled_ReturnsNotFound` | Feature flag false → 404, provider não é chamado |
| 2 | `Upload_StorageEnabled_InvalidContext_ReturnsBadRequest` | Contexto inválido → 400 |
| 3 | `Upload_DisallowedContentType_ReturnsBadRequest` | MIME não permitido → 400 |
| 4 | `Upload_FileTooLarge_ReturnsBadRequest` | Arquivo acima do limite → 400 |
| 5 | `Upload_EmptyFile_ReturnsBadRequest` | 0 bytes → 400 |
| 6 | `Upload_ValidFile_ReturnsKeyAndPublicUrl` | Upload OK → 200 com key e publicUrl |
| 7 | `Upload_ProviderThrows_Returns503` | Provider lança → 503 |
| 8 | `Upload_ObjectKey_ContainsTenantIdAndContext` | key gerado contém tenantId e path do contexto |
| 9 | `Delete_StorageDisabled_ReturnsNotFound` | Feature flag false → 404, provider não é chamado |
| 10 | `Delete_KeyFromOtherTenant_ReturnsForbid` | key de outro tenant → 403 |
| 11 | `Delete_ValidKey_ReturnsNoContent` | key válida → 204 |
| 12 | `Upload_StorageEnabled_ProviderReceivesCorrectContentType` | Provider recebe o contentType correto |

- [ ] **Step 1: Write StorageControllerTests**

`nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Api.Controllers.Integrations;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public class StorageControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private record Sut(
        StorageController Controller,
        IStorageProvider Provider,
        IIntegrationFeatureFlags Flags);

    private static Sut Build(
        bool storageEnabled = true,
        int maxFileSizeMb   = 10,
        string[]? allowed   = null)
    {
        var provider = Substitute.For<IStorageProvider>();
        var flags    = Substitute.For<IIntegrationFeatureFlags>();
        flags.StorageEnabled.Returns(storageEnabled);

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(TenantId);

        var opts = Options.Create(new StorageOptions
        {
            MaxFileSizeMb       = maxFileSizeMb,
            AllowedContentTypes = allowed ?? ["image/jpeg", "image/png", "image/webp"],
        });

        var controller = new StorageController(
            provider, flags, opts, tenant,
            NullLogger<StorageController>.Instance);

        return new Sut(controller, provider, flags);
    }

    private static IFormFile MakeFile(
        string contentType = "image/jpeg",
        long   size        = 1024,
        string fileName    = "photo.jpg")
    {
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns(contentType);
        file.Length.Returns(size);
        file.FileName.Returns(fileName);
        file.OpenReadStream().Returns(new MemoryStream(new byte[size > 0 ? (int)size : 0]));
        return file;
    }

    // ── Feature flag ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_StorageDisabled_ReturnsNotFound()
    {
        var sut  = Build(storageEnabled: false);
        var file = MakeFile();

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await sut.Provider.DidNotReceive().UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_StorageDisabled_ReturnsNotFound()
    {
        var sut = Build(storageEnabled: false);
        var key = $"tenants/{TenantId}/products/file.jpg";

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        await sut.Provider.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Upload validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_InvalidContext_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile();

        var result = await sut.Controller.Upload(file, "unknown-context", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upload_DisallowedContentType_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile(contentType: "application/zip");

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upload_FileTooLarge_ReturnsBadRequest()
    {
        var sut  = Build(maxFileSizeMb: 1);
        var file = MakeFile(size: 2 * 1024 * 1024); // 2MB > 1MB limit

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile(size: 0);

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Upload success ────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ValidFile_ReturnsKeyAndPublicUrl()
    {
        var sut  = Build();
        var file = MakeFile();

        sut.Provider
            .UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StorageUploadResult(
                $"tenants/{TenantId}/products/abc.jpg",
                $"https://cdn.example.com/tenants/{TenantId}/products/abc.jpg"));

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        dynamic val = ok.Value!;
        Assert.NotNull((string)val.key);
        Assert.NotNull((string)val.publicUrl);
    }

    [Fact]
    public async Task Upload_ProviderThrows_Returns503()
    {
        var sut  = Build();
        var file = MakeFile();

        sut.Provider
            .UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("R2 unreachable"));

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task Upload_ObjectKey_ContainsTenantIdAndContextPath()
    {
        var sut  = Build();
        var file = MakeFile();

        StorageUploadRequest? captured = null;
        sut.Provider
            .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains(TenantId.ToString(), captured!.ObjectKey);
        Assert.Contains("products", captured.ObjectKey);
        Assert.DoesNotContain("photo.jpg", captured.ObjectKey); // original filename never used
    }

    [Fact]
    public async Task Upload_ProviderReceivesCorrectContentType()
    {
        var sut  = Build();
        var file = MakeFile(contentType: "image/webp");

        StorageUploadRequest? captured = null;
        sut.Provider
            .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        await sut.Controller.Upload(file, "restaurant-logo", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("image/webp", captured!.ContentType);
        Assert.EndsWith(".webp", captured.ObjectKey);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_KeyFromOtherTenant_ReturnsForbid()
    {
        var sut = Build();
        var key = "tenants/other-tenant-id/products/file.jpg";

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        await sut.Provider.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ValidKey_ReturnsNoContent()
    {
        var sut = Build();
        var key = $"tenants/{TenantId}/products/file.jpg";

        sut.Provider.DeleteAsync(key, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await sut.Provider.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run storage tests**

```
dotnet test nexo-backend/Nexo.sln --filter "FullyQualifiedName~StorageController"
```
Expected: 12/12 passed.

- [ ] **Step 3: Run all integration tests**

```
dotnet test nexo-backend/Nexo.sln --filter "FullyQualifiedName~Integrations"
```
Expected: all pass (30 previous + 12 new = 42 passing).

- [ ] **Step 4: Full build**

```
dotnet build nexo-backend/Nexo.sln
```
Expected: 0 errors.

- [ ] **Step 5: TypeScript check**

In `nexo-main/`:
```
npx tsc --noEmit
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs
git commit -m "test(storage): 12 unit tests for StorageController including feature flag and security"
```

---

## Riscos e lacunas restantes

| Item | Status | Ação |
|------|--------|------|
| `StorageEnabled=false` por padrão | **Correto** — não ativa sem credenciais | Ativar via env var em produção/staging |
| Produto novo — sem upload | **Opção A implementada** — upload só em edit mode | Aceitável para MVP |
| Imagem antiga ao trocar | **Não implementado** | Pode acumular arquivos órfãos no R2. Cleanup em fase futura |
| Tenant.LogoUrl | **Não existe na entidade** | Requer migration separada. Fora do escopo desta fase |
| Bucket R2 público | **Requerido externamente** | `orken-assets` deve ter leitura pública no Cloudflare |
| Credenciais R2 | **Vazias em appsettings** | Preencher apenas em produção/staging via env vars |
| `client_max_body_size` | **Verificar no proxy** | Nginx/Cloudflare pode limitar multipart abaixo de 10MB |
| Delete com `{*key}` e `/` | **Protegido** | Prefixo `tenants/{tenantId}/` obrigatório + `encodeURIComponent` no frontend |
