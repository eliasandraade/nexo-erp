# CMV Restaurante — Fase 1: Separação Estoque/Cardápio + Ficha Técnica

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separar ingredientes do estoque dos produtos do cardápio no NexoERP, e completar a ficha técnica com foto, modo de preparo em etapas, montagem, embalagem e cálculo de CMV incluindo custo de gás e mão de obra.

**Architecture:** Flag `IsIngredient` no `Product` existente separa os dois tipos. Nova tabela `ProductPurchasePrice` registra histórico de preços de compra. `RestRecipeCard` é estendido com campos de preparo, assembly e embalagem. `FoodServiceSettings` ganha taxas de custo/minuto para gás e MO usadas no cálculo automático do CMV.

**Tech Stack:** .NET 8 + EF Core + Npgsql (backend); React + TypeScript + TanStack Query v5 + shadcn/ui (frontend). Testes: xUnit + Testcontainers.

---

## File Map

### Backend — criados
- `nexo-backend/src/Nexo.Domain/Entities/ProductPurchasePrice.cs` — entidade de histórico de preços
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductPurchasePriceConfiguration.cs` — mapeamento EF
- `nexo-backend/src/Nexo.Application/Common/Interfaces/IProductPurchasePriceRepository.cs` — contrato do repositório
- `nexo-backend/src/Nexo.Infrastructure/Repositories/ProductPurchasePriceRepository.cs` — implementação
- `nexo-backend/src/Nexo.Application/Features/Products/ProductPurchasePriceDtos.cs` — DTOs de preços
- `nexo-backend/src/Nexo.Application/Features/Products/ProductPurchasePriceService.cs` — lógica de negócio
- `nexo-backend/src/Nexo.Api/Controllers/ProductPurchasePricesController.cs` — endpoints REST
- `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/RecipeCardCmvTests.cs` — testes de CMV

### Backend — modificados
- `nexo-backend/src/Nexo.Domain/Entities/Product.cs` — add `IsIngredient` + `SetIsIngredient()`
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductConfiguration.cs` — add `is_ingredient`
- `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs` — add `DbSet<ProductPurchasePrice>`
- `nexo-backend/src/Nexo.Application/Common/Interfaces/IProductRepository.cs` — add `isIngredient?` to `GetAllAsync`
- `nexo-backend/src/Nexo.Infrastructure/Repositories/ProductRepository.cs` — implement filter
- `nexo-backend/src/Nexo.Application/Features/Products/ProductDtos.cs` — add `IsIngredient` everywhere
- `nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs` — pass `IsIngredient`, filter
- `nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs` — add `?isIngredient` param
- `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestRecipeCard.cs` — add 6 new fields + `PrepStep` record + updated `Update()`
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestRecipeCardConfiguration.cs` — add column mappings
- `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs` — update `RecipeCardDto`, `UpdateRecipeCardRequest`, `FoodServiceSettingsDto`, `UpdateFoodServiceSettingsRequest`
- `nexo-backend/src/Nexo.Application/Modules/Restaurante/RecipeCardService.cs` — update `MapAsync` + `UpdateAsync` + image endpoint
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/RecipeCardsController.cs` — add `POST /:id/image`
- `nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs` — add cost fields + `UpdateOperationalCosts()`
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/FoodServiceSettingsConfiguration.cs` — add 2 columns
- `nexo-backend/src/Nexo.Application/Modules/Restaurante/FoodServiceSettingsService.cs` — update Map + `UpdateOperationalCostsAsync`
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FoodServiceSettingsController.cs` — add `PUT /costs`

### Frontend — criados
- `nexo-main/src/modules/restaurante/types/recipe-card.types.ts` — tipos TypeScript para ficha técnica
- `nexo-main/src/modules/restaurante/api/recipe-card.api.ts` — funções de API
- `nexo-main/src/modules/restaurante/hooks/use-recipe-card.ts` — queries e mutations
- `nexo-main/src/modules/restaurante/pages/RecipeCardPage.tsx` — página da ficha técnica
- `nexo-main/src/modules/restaurante/components/CmvBar.tsx` — barra de CMV sticky no rodapé
- `nexo-main/src/modules/restaurante/components/PrepStepsEditor.tsx` — editor de etapas de preparo
- `nexo-main/src/modules/products/components/IngredientPriceSection.tsx` — histórico de preços de compra

### Frontend — modificados
- `nexo-main/src/modules/products/types/index.ts` — add `isIngredient` a `ProductDto`, `Product`, `emptyProduct`, `dtoToProduct`
- `nexo-main/src/modules/products/api/products.api.ts` — add `isIngredient` aos payloads + param de filtro
- `nexo-main/src/modules/products/hooks/use-products.ts` — `useProducts` aceita `isIngredient?`
- `nexo-main/src/modules/inventory/pages/EstoquePage.tsx` — filtrar por `isIngredient=true`
- `nexo-main/src/modules/products/pages/ProdutosPage.tsx` — filtrar por `isIngredient=false`
- `nexo-main/src/modules/products/pages/ProductFormPage.tsx` — toggle ingrediente/cardápio + link para ficha + seção preços
- `nexo-main/src/modules/restaurante/types/index.ts` — add `costPerMinuteGas`, `costPerMinuteLaborRate` a `FoodServiceSettingsDto`
- `nexo-main/src/modules/restaurante/api/restaurante.api.ts` — add `updateOperationalCosts()`
- `nexo-main/src/modules/restaurante/hooks/useFoodSettings.ts` — add mutation para custos operacionais
- `nexo-main/src/modules/restaurante/pages/RestauranteSetupPage.tsx` — seção Custos Operacionais
- `nexo-main/src/app/router/AppRouter.tsx` — add rota `/produtos/:id/ficha`

---

## Task 1: Adicionar `IsIngredient` ao `Product` (Domain + Config)

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Entities/Product.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductConfiguration.cs`

- [ ] **Step 1.1: Adicionar propriedade e método ao domínio**

Em `Product.cs`, adicione após `IsMenuVisible`:
```csharp
public bool IsIngredient { get; private set; }  // true = insumo de estoque do restaurante
```

No método `Create`, adicione o parâmetro e a atribuição (antes do `return`):
```csharp
public static Product Create(
    Guid tenantId,
    string code,
    string name,
    ProductUnit unit,
    decimal salePrice,
    decimal costPrice = 0,
    string? barcode = null,
    string? description = null,
    Guid? categoryId = null,
    bool trackStock = true,
    decimal? minStockQuantity = null,
    decimal? maxStockQuantity = null,
    bool isIngredient = false)   // ← novo
{
    return new Product(tenantId)
    {
        Code             = code.Trim().ToUpperInvariant(),
        Barcode          = barcode?.Trim(),
        Name             = name.Trim(),
        Description      = description?.Trim(),
        CategoryId       = categoryId,
        Unit             = unit,
        CostPrice        = costPrice,
        SalePrice        = salePrice,
        TrackStock       = trackStock,
        MinStockQuantity = minStockQuantity,
        MaxStockQuantity = maxStockQuantity,
        IsActive         = true,
        IsMenuVisible    = true,
        IsIngredient     = isIngredient,   // ← novo
    };
}
```

Adicione o método `SetIsIngredient` após `SetImageUrl`:
```csharp
public void SetIsIngredient(bool value) { IsIngredient = value; SetUpdatedAt(); }
```

- [ ] **Step 1.2: Mapear coluna no EF**

Em `ProductConfiguration.cs`, adicione após o mapeamento de `IsMenuVisible`:
```csharp
builder.Property(x => x.IsIngredient)
    .HasColumnName("is_ingredient")
    .HasDefaultValue(false)
    .IsRequired();
```

- [ ] **Step 1.3: Gerar e aplicar migration**

```bash
cd nexo-backend
dotnet ef migrations add AddIsIngredientToProducts \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

Expected: migration criada com `migrationBuilder.AddColumn<bool>("is_ingredient", defaultValue: false)`. Database atualizado sem erros.

- [ ] **Step 1.4: Commit**

```bash
git add nexo-backend/src/Nexo.Domain/Entities/Product.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductConfiguration.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(domain): add IsIngredient flag to Product entity"
```

---

## Task 2: Expor `IsIngredient` na API de Produtos

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Common/Interfaces/IProductRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Repositories/ProductRepository.cs`
- Modify: `nexo-backend/src/Nexo.Application/Features/Products/ProductDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Features/Products/ProductService.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/ProductsController.cs`

- [ ] **Step 2.1: Atualizar interface do repositório**

Em `IProductRepository.cs`, mude a assinatura de `GetAllAsync`:
```csharp
Task<IReadOnlyList<Product>> GetAllAsync(
    bool includeInactive = false,
    bool? isIngredient = null,
    CancellationToken ct = default);
```

- [ ] **Step 2.2: Implementar filtro no repositório**

Em `ProductRepository.cs`, substitua o método `GetAllAsync`:
```csharp
public async Task<IReadOnlyList<Product>> GetAllAsync(
    bool includeInactive = false,
    bool? isIngredient = null,
    CancellationToken ct = default)
    => await _context.Products
        .Where(x => includeInactive || x.IsActive)
        .Where(x => isIngredient == null || x.IsIngredient == isIngredient)
        .OrderBy(x => x.Name)
        .ToListAsync(ct);
```

- [ ] **Step 2.3: Adicionar `IsIngredient` aos DTOs**

Em `ProductDtos.cs`, substitua os records existentes pelas versões atualizadas:

```csharp
public record CreateProductRequest(
    string Code,
    string Name,
    string Unit,
    decimal SalePrice,
    decimal CostPrice = 0,
    string? Barcode = null,
    string? Description = null,
    Guid? CategoryId = null,
    bool TrackStock = true,
    decimal? MinStockQuantity = null,
    decimal? MaxStockQuantity = null,
    bool IsIngredient = false);      // ← novo

public record UpdateProductRequest(
    string Name,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool TrackStock,
    string? Barcode = null,
    string? Description = null,
    Guid? CategoryId = null,
    decimal? MinStockQuantity = null,
    decimal? MaxStockQuantity = null,
    bool IsIngredient = false);      // ← novo

public record ProductDto(
    Guid Id,
    string Code,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    bool TrackStock,
    decimal? MinStockQuantity,
    decimal? MaxStockQuantity,
    bool IsActive,
    bool IsMenuVisible,
    bool IsIngredient,               // ← novo
    string? ImageUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

- [ ] **Step 2.4: Atualizar `ProductService`**

Em `ProductService.cs`:

No método `CreateAsync`, passe `isIngredient` ao `Product.Create`:
```csharp
var product = Product.Create(
    _currentTenant.Id,
    request.Code,
    request.Name,
    unit,
    request.SalePrice,
    request.CostPrice,
    request.Barcode,
    request.Description,
    request.CategoryId,
    request.TrackStock,
    request.MinStockQuantity,
    request.MaxStockQuantity,
    request.IsIngredient);    // ← novo
```

No método `UpdateAsync`, após `product.Update(...)`, adicione:
```csharp
product.SetIsIngredient(request.IsIngredient);
```

No método `GetAllAsync`, passe o parâmetro:
```csharp
public async Task<IReadOnlyList<ProductDto>> GetAllAsync(
    bool includeInactive = false,
    bool? isIngredient = null,
    CancellationToken ct = default)
{
    var list = await _products.GetAllAsync(includeInactive, isIngredient, ct);
    return list.Select(MapToDto).ToList();
}
```

No método `MapToDto`, adicione `IsIngredient` ao `ProductDto`:
```csharp
private static ProductDto MapToDto(Product p) => new(
    p.Id, p.Code, p.Barcode, p.Name, p.Description,
    p.CategoryId, p.Unit.ToString(), p.CostPrice, p.SalePrice,
    p.TrackStock, p.MinStockQuantity, p.MaxStockQuantity,
    p.IsActive, p.IsMenuVisible, p.IsIngredient, p.ImageUrl,
    p.CreatedAt, p.UpdatedAt);
```

- [ ] **Step 2.5: Expor filtro no controller**

Em `ProductsController.cs`, substitua o `GetAll`:
```csharp
[HttpGet]
public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll(
    [FromQuery] bool includeInactive = false,
    [FromQuery] bool? isIngredient = null,
    CancellationToken ct = default)
    => Ok(await _service.GetAllAsync(includeInactive, isIngredient, ct));
```

- [ ] **Step 2.6: Build e teste**

```bash
cd nexo-backend
dotnet build
```

Expected: sem erros de compilação.

- [ ] **Step 2.7: Commit**

```bash
git add nexo-backend/src/
git commit -m "feat(products): expose IsIngredient flag in API with optional filter"
```

---

## Task 3: Entidade `ProductPurchasePrice` (Domain + Config + DbContext)

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Entities/ProductPurchasePrice.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductPurchasePriceConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 3.1: Criar entidade de domínio**

Crie `ProductPurchasePrice.cs`:
```csharp
using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Registra o preço de compra de um ingrediente.
/// O backend calcula a média das últimas 5 entradas para exibição como referência.
/// </summary>
public class ProductPurchasePrice : TenantEntity
{
    private ProductPurchasePrice() { }
    private ProductPurchasePrice(Guid tenantId) : base(tenantId) { }

    public Guid     ProductId   { get; private set; }
    public decimal  Price       { get; private set; }
    public DateOnly PurchasedAt { get; private set; }

    public static ProductPurchasePrice Create(Guid tenantId, Guid productId, decimal price, DateOnly purchasedAt)
    {
        if (price < 0)
            throw new Domain.Exceptions.DomainException("Purchase price cannot be negative.");
        return new ProductPurchasePrice(tenantId)
        {
            ProductId   = productId,
            Price       = price,
            PurchasedAt = purchasedAt,
        };
    }
}
```

- [ ] **Step 3.2: Criar configuração EF**

Crie `ProductPurchasePriceConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class ProductPurchasePriceConfiguration : IEntityTypeConfiguration<ProductPurchasePrice>
{
    public void Configure(EntityTypeBuilder<ProductPurchasePrice> builder)
    {
        builder.ToTable("product_purchase_prices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price")
            .HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.PurchasedAt).HasColumnName("purchased_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.PurchasedAt })
            .HasDatabaseName("ix_product_purchase_prices_tenant_product_date");
    }
}
```

- [ ] **Step 3.3: Registrar no DbContext**

Em `NexoDbContext.cs`, adicione após os DbSets de Core:
```csharp
public DbSet<ProductPurchasePrice> ProductPurchasePrices => Set<ProductPurchasePrice>();
```

- [ ] **Step 3.4: Gerar e aplicar migration**

```bash
cd nexo-backend
dotnet ef migrations add CreateProductPurchasePrices \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

Expected: migration cria a tabela `product_purchase_prices` com índice.

- [ ] **Step 3.5: Commit**

```bash
git add nexo-backend/src/Nexo.Domain/Entities/ProductPurchasePrice.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/ProductPurchasePriceConfiguration.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(domain): add ProductPurchasePrice entity and migration"
```

---

## Task 4: Repository, Service e Controller de Histórico de Preços

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Common/Interfaces/IProductPurchasePriceRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/ProductPurchasePriceRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Features/Products/ProductPurchasePriceDtos.cs`
- Create: `nexo-backend/src/Nexo.Application/Features/Products/ProductPurchasePriceService.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/ProductPurchasePricesController.cs`

- [ ] **Step 4.1: Criar interface do repositório**

Crie `IProductPurchasePriceRepository.cs`:
```csharp
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IProductPurchasePriceRepository
{
    Task<IReadOnlyList<ProductPurchasePrice>> GetLastFiveAsync(Guid productId, CancellationToken ct = default);
    Task AddAsync(ProductPurchasePrice price, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4.2: Implementar repositório**

Crie `ProductPurchasePriceRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class ProductPurchasePriceRepository : IProductPurchasePriceRepository
{
    private readonly NexoDbContext _context;
    public ProductPurchasePriceRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductPurchasePrice>> GetLastFiveAsync(
        Guid productId, CancellationToken ct = default)
        => await _context.ProductPurchasePrices
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.PurchasedAt)
            .Take(5)
            .ToListAsync(ct);

    public async Task AddAsync(ProductPurchasePrice price, CancellationToken ct = default)
        => await _context.ProductPurchasePrices.AddAsync(price, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 4.3: Criar DTOs**

Crie `ProductPurchasePriceDtos.cs`:
```csharp
namespace Nexo.Application.Features.Products;

public record AddPurchasePriceRequest(decimal Price, DateOnly PurchasedAt);

public record PurchasePriceEntryDto(Guid Id, decimal Price, DateOnly PurchasedAt);

public record PurchasePriceHistoryDto(
    decimal? LastPrice,
    decimal? AveragePrice,
    IReadOnlyList<PurchasePriceEntryDto> History);
```

- [ ] **Step 4.4: Criar service**

Crie `ProductPurchasePriceService.cs`:
```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Products;

public class ProductPurchasePriceService
{
    private readonly IProductPurchasePriceRepository _repo;
    private readonly IProductRepository              _products;
    private readonly ICurrentTenant                  _currentTenant;

    public ProductPurchasePriceService(
        IProductPurchasePriceRepository repo,
        IProductRepository products,
        ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _products      = products;
        _currentTenant = currentTenant;
    }

    public async Task<PurchasePriceHistoryDto> GetHistoryAsync(Guid productId, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var entries = await _repo.GetLastFiveAsync(productId, ct);
        if (!entries.Any())
            return new PurchasePriceHistoryDto(null, null, []);

        var avg = Math.Round(entries.Average(e => e.Price), 4);
        return new PurchasePriceHistoryDto(
            LastPrice:    entries[0].Price,
            AveragePrice: avg,
            History:      entries.Select(e => new PurchasePriceEntryDto(e.Id, e.Price, e.PurchasedAt)).ToList());
    }

    public async Task<PurchasePriceEntryDto> AddAsync(Guid productId, AddPurchasePriceRequest request, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var entry = ProductPurchasePrice.Create(_currentTenant.Id, productId, request.Price, request.PurchasedAt);
        await _repo.AddAsync(entry, ct);
        await _repo.SaveChangesAsync(ct);
        return new PurchasePriceEntryDto(entry.Id, entry.Price, entry.PurchasedAt);
    }
}
```

- [ ] **Step 4.5: Registrar dependências no DI**

Em `nexo-backend/src/Nexo.Api/Program.cs` ou no arquivo de registro de serviços, adicione:
```csharp
builder.Services.AddScoped<IProductPurchasePriceRepository, ProductPurchasePriceRepository>();
builder.Services.AddScoped<ProductPurchasePriceService>();
```

> Localize o bloco onde `ProductService` e `IProductRepository` são registrados para inserir no mesmo lugar.

- [ ] **Step 4.6: Criar controller**

Crie `ProductPurchasePricesController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Features.Products;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/purchase-prices")]
[Authorize]
public class ProductPurchasePricesController : ControllerBase
{
    private readonly ProductPurchasePriceService _service;
    public ProductPurchasePricesController(ProductPurchasePriceService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PurchasePriceHistoryDto>> GetHistory(
        Guid productId, CancellationToken ct)
        => Ok(await _service.GetHistoryAsync(productId, ct));

    [HttpPost]
    public async Task<ActionResult<PurchasePriceEntryDto>> Add(
        Guid productId, [FromBody] AddPurchasePriceRequest request, CancellationToken ct)
    {
        var dto = await _service.AddAsync(productId, request, ct);
        return CreatedAtAction(nameof(GetHistory), new { productId }, dto);
    }
}
```

- [ ] **Step 4.7: Build**

```bash
cd nexo-backend && dotnet build
```

Expected: zero erros.

- [ ] **Step 4.8: Commit**

```bash
git add nexo-backend/src/
git commit -m "feat(products): add purchase price history with 5-entry average"
```

---

## Task 5: Estender `RestRecipeCard` (Domain + Config)

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestRecipeCard.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestRecipeCardConfiguration.cs`

- [ ] **Step 5.1: Adicionar `PrepStep` e novos campos ao domínio**

Em `RestRecipeCard.cs`, adicione o record `PrepStep` antes da classe:
```csharp
/// <summary>Etapa estruturada do modo de preparo. Serializada como JSONB.</summary>
public record PrepStep(int Order, string Description, int? DurationMinutes);
```

Na classe `RestRecipeCard`, adicione após `IsActive`:
```csharp
public string?  ImageUrl           { get; private set; }
public bool     HasPrep            { get; private set; } = true;
// Serializado como JSONB: array de PrepStep ordenado por Order
public string   PrepStepsJson      { get; private set; } = "[]";
public int?     TotalPrepTimeMin   { get; private set; }
public string?  AssemblyNotes      { get; private set; }
public bool     RequiresPackaging  { get; private set; }
public Guid?    PackagingProductId { get; private set; }
```

Adicione helpers de acesso:
```csharp
// Deserializa PrepStepsJson para uso na camada de aplicação
public IReadOnlyList<PrepStep> GetPrepSteps()
{
    if (string.IsNullOrWhiteSpace(PrepStepsJson)) return [];
    return System.Text.Json.JsonSerializer.Deserialize<List<PrepStep>>(PrepStepsJson) ?? [];
}
```

Substitua o método `Update` existente pelo novo:
```csharp
public void Update(
    decimal yield, string yieldUnit, string? notes,
    bool hasPrep,
    IEnumerable<PrepStep> prepSteps,
    string? assemblyNotes,
    bool requiresPackaging,
    Guid? packagingProductId,
    string? imageUrl)
{
    if (yield <= 0)
        throw new DomainException("Recipe yield must be greater than zero.");

    Yield              = yield;
    YieldUnit          = yieldUnit.Trim();
    Notes              = notes?.Trim();
    HasPrep            = hasPrep;
    AssemblyNotes      = assemblyNotes?.Trim();
    RequiresPackaging  = requiresPackaging;
    PackagingProductId = requiresPackaging ? packagingProductId : null;
    if (imageUrl is not null) ImageUrl = imageUrl.Trim();

    var steps = hasPrep
        ? prepSteps.OrderBy(s => s.Order).ToList()
        : [];

    PrepStepsJson    = System.Text.Json.JsonSerializer.Serialize(steps);
    TotalPrepTimeMin = steps.Any() ? steps.Sum(s => s.DurationMinutes ?? 0) : null;

    SetUpdatedAt();
}

public void SetImageUrl(string url) { ImageUrl = url.Trim(); SetUpdatedAt(); }
```

- [ ] **Step 5.2: Adicionar mapeamentos EF**

Em `RestRecipeCardConfiguration.cs`, adicione antes do bloco de `HasMany`/`HasOne`:
```csharp
builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(2000);
builder.Property(x => x.HasPrep).HasColumnName("has_prep").HasDefaultValue(true).IsRequired();
builder.Property(x => x.PrepStepsJson).HasColumnName("prep_steps_json")
    .HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
builder.Property(x => x.TotalPrepTimeMin).HasColumnName("total_prep_time_min");
builder.Property(x => x.AssemblyNotes).HasColumnName("assembly_notes").HasMaxLength(2000);
builder.Property(x => x.RequiresPackaging).HasColumnName("requires_packaging")
    .HasDefaultValue(false).IsRequired();
builder.Property(x => x.PackagingProductId).HasColumnName("packaging_product_id");
```

Adicione a FK de embalagem no bloco de relações:
```csharp
builder.HasOne<Nexo.Domain.Entities.Product>()
    .WithMany()
    .HasForeignKey(x => x.PackagingProductId)
    .HasConstraintName("fk_rest_recipe_cards_packaging_product")
    .OnDelete(DeleteBehavior.SetNull)
    .IsRequired(false);
```

- [ ] **Step 5.3: Gerar e aplicar migration**

```bash
cd nexo-backend
dotnet ef migrations add ExtendRestRecipeCard \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

Expected: migration adiciona 7 colunas à tabela `rest_recipe_cards`.

- [ ] **Step 5.4: Commit**

```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestRecipeCard.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestRecipeCardConfiguration.cs \
        nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): extend RestRecipeCard with prep steps, assembly, packaging"
```

---

## Task 6: Atualizar DTOs e Service da Ficha Técnica

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RecipeCardService.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/RecipeCardsController.cs`

- [ ] **Step 6.1: Atualizar DTOs no `RestauranteDtos.cs`**

Substitua os records da seção RECIPE CARD:
```csharp
// ═══════════════════════════════════════════════════════════
// RECIPE CARD
// ═══════════════════════════════════════════════════════════

public record CreateRecipeCardRequest(
    Guid    ProductId,
    decimal Yield,
    string  YieldUnit,
    bool    HasPrep = true,
    string? Notes   = null);

public record PrepStepDto(int Order, string Description, int? DurationMinutes);

public record UpdateRecipeCardRequest(
    decimal           Yield,
    string            YieldUnit,
    bool              HasPrep,
    List<PrepStepDto> PrepSteps,
    string?           AssemblyNotes,
    bool              RequiresPackaging,
    Guid?             PackagingProductId,
    string?           Notes = null);

public record AddIngredientRequest(
    Guid    IngredientProductId,
    decimal Quantity,
    string  Unit);

public record RecipeIngredientDto(
    Guid    Id,
    Guid    IngredientProductId,
    string  IngredientName,
    string  IngredientCode,
    decimal Quantity,
    string  Unit,
    decimal CurrentCostPrice,
    decimal LineCost);

public record RecipeCardDto(
    Guid    Id,
    Guid    ProductId,
    string  ProductName,
    string  ProductCode,
    decimal SalePrice,
    string? ImageUrl,
    decimal Yield,
    string  YieldUnit,
    bool    HasPrep,
    IReadOnlyList<PrepStepDto> PrepSteps,
    int?    TotalPrepTimeMin,
    string? AssemblyNotes,
    bool    RequiresPackaging,
    Guid?   PackagingProductId,
    string? PackagingProductName,
    bool    IsActive,
    string? Notes,
    // Custos calculados
    decimal IngredientCost,
    decimal GasCost,
    decimal LaborCost,
    decimal CalculatedCost,
    decimal CmvPercent,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    DateTime CreatedAt);
```

- [ ] **Step 6.2: Atualizar `RecipeCardService.cs`**

Substitua o serviço completo:
```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class RecipeCardService
{
    private readonly IRecipeCardRepository          _recipes;
    private readonly IProductRepository             _products;
    private readonly IFoodServiceSettingsRepository _settings;
    private readonly ICurrentTenant                 _currentTenant;

    public RecipeCardService(
        IRecipeCardRepository          recipes,
        IProductRepository             products,
        IFoodServiceSettingsRepository settings,
        ICurrentTenant                 currentTenant)
    {
        _recipes       = recipes;
        _products      = products;
        _settings      = settings;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<RecipeCardDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var cards  = await _recipes.GetAllAsync(includeInactive, ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        var result = new List<RecipeCardDto>();
        foreach (var card in cards)
            result.Add(await MapAsync(card, config, ct));
        return result;
    }

    public async Task<RecipeCardDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var card   = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
    {
        var card   = await _recipes.GetByProductIdWithIngredientsAsync(productId, ct)
            ?? throw new NotFoundException("RecipeCard for product", productId);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> CreateAsync(CreateRecipeCardRequest request, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        var existing = await _recipes.GetByProductIdAsync(request.ProductId, ct);
        if (existing is not null)
            throw new ConflictException("A recipe card already exists for this product.");

        var card = RestRecipeCard.Create(
            _currentTenant.Id, request.ProductId,
            request.Yield, request.YieldUnit, request.Notes);

        await _recipes.AddAsync(card, ct);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> UpdateAsync(Guid id, UpdateRecipeCardRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);

        if (request.RequiresPackaging && request.PackagingProductId.HasValue)
        {
            var pkg = await _products.GetByIdAsync(request.PackagingProductId.Value, ct)
                ?? throw new NotFoundException("Packaging product", request.PackagingProductId.Value);
            if (!pkg.IsIngredient)
                throw new DomainException("Packaging must reference a product marked as IsIngredient.");
        }

        var steps = request.PrepSteps.Select(s => new PrepStep(s.Order, s.Description, s.DurationMinutes));

        card.Update(
            request.Yield, request.YieldUnit, request.Notes,
            request.HasPrep, steps,
            request.AssemblyNotes, request.RequiresPackaging, request.PackagingProductId,
            imageUrl: null);

        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> AddIngredientAsync(Guid id, AddIngredientRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);

        var ingProduct = await _products.GetByIdAsync(request.IngredientProductId, ct)
            ?? throw new NotFoundException("Ingredient product", request.IngredientProductId);

        if (!ingProduct.IsIngredient)
            throw new DomainException("Only products marked as IsIngredient can be added as recipe ingredients.");

        var ingredient = card.AddIngredient(
            _currentTenant.Id, request.IngredientProductId, request.Quantity, request.Unit);

        _recipes.TrackIngredient(ingredient);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> RemoveIngredientAsync(Guid id, Guid ingredientId, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.RemoveIngredient(ingredientId);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    public async Task<RecipeCardDto> SetImageAsync(Guid id, string imageUrl, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.SetImageUrl(imageUrl);
        await _recipes.SaveChangesAsync(ct);
        var config = await _settings.GetCurrentStoreAsync(ct);
        return await MapAsync(card, config, ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────────

    private async Task<RecipeCardDto> MapAsync(RestRecipeCard card, FoodServiceSettings? config, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(card.ProductId, ct);
        var ingredientDtos   = new List<RecipeIngredientDto>();
        decimal totalIngCost = 0m;

        foreach (var ing in card.Ingredients)
        {
            var ingProduct = await _products.GetByIdAsync(ing.IngredientProductId, ct);
            var lineCost   = ing.Quantity * (ingProduct?.CostPrice ?? 0m);
            totalIngCost  += lineCost;

            ingredientDtos.Add(new RecipeIngredientDto(
                ing.Id, ing.IngredientProductId,
                ingProduct?.Name ?? string.Empty,
                ingProduct?.Code ?? string.Empty,
                ing.Quantity, ing.Unit,
                ingProduct?.CostPrice ?? 0m,
                lineCost));
        }

        var gasRate   = config?.CostPerMinuteGas   ?? 0m;
        var laborRate = config?.CostPerMinuteLaborRate ?? 0m;
        var prepMin   = (decimal)(card.TotalPrepTimeMin ?? 0);

        var unitIngCost = card.Yield > 0 ? totalIngCost / card.Yield : 0m;
        var gasCost     = prepMin * gasRate;
        var laborCost   = prepMin * laborRate;
        var totalCost   = unitIngCost + gasCost + laborCost;
        var salePrice   = product?.SalePrice ?? 0m;
        var cmvPct      = salePrice > 0 ? (totalCost / salePrice) * 100m : 0m;

        string? packagingName = null;
        if (card.PackagingProductId.HasValue)
        {
            var pkg = await _products.GetByIdAsync(card.PackagingProductId.Value, ct);
            packagingName = pkg?.Name;
        }

        var prepSteps = card.GetPrepSteps()
            .Select(s => new PrepStepDto(s.Order, s.Description, s.DurationMinutes))
            .ToList();

        return new RecipeCardDto(
            card.Id, card.ProductId,
            product?.Name ?? string.Empty,
            product?.Code ?? string.Empty,
            salePrice,
            card.ImageUrl,
            card.Yield, card.YieldUnit,
            card.HasPrep, prepSteps,
            card.TotalPrepTimeMin,
            card.AssemblyNotes,
            card.RequiresPackaging, card.PackagingProductId, packagingName,
            card.IsActive, card.Notes,
            Math.Round(unitIngCost, 4),
            Math.Round(gasCost,     4),
            Math.Round(laborCost,   4),
            Math.Round(totalCost,   4),
            Math.Round(cmvPct,      2),
            ingredientDtos,
            card.CreatedAt);
    }
}
```

- [ ] **Step 6.3: Adicionar endpoint de upload de imagem no controller**

Em `RecipeCardsController.cs`, adicione:
```csharp
/// <summary>Salva imagem do prato em wwwroot/images/recipes/ e grava a URL na ficha.</summary>
[HttpPost("{id:guid}/image")]
public async Task<ActionResult<RecipeCardDto>> UploadImage(
    Guid id, IFormFile file, CancellationToken ct)
{
    if (file is null || file.Length == 0)
        return BadRequest("No file uploaded.");

    var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
    if (!allowed.Contains(file.ContentType))
        return BadRequest("Only JPEG, PNG, and WebP images are accepted.");

    if (file.Length > 5 * 1024 * 1024)
        return BadRequest("Image must be smaller than 5 MB.");

    var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
    var fileName = $"{id:N}{ext}";
    var wwwroot  = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "recipes");
    Directory.CreateDirectory(wwwroot);
    var fullPath = Path.Combine(wwwroot, fileName);

    await using var stream = System.IO.File.Create(fullPath);
    await file.CopyToAsync(stream, ct);

    var imageUrl = $"/images/recipes/{fileName}";
    var dto      = await _service.SetImageAsync(id, imageUrl, ct);
    return Ok(dto);
}
```

No topo do controller, adicione `[Consumes("multipart/form-data")]` somente no método `UploadImage` (o atributo já está na action, não no controller inteiro).

- [ ] **Step 6.4: Build**

```bash
cd nexo-backend && dotnet build
```

Expected: zero erros de compilação.

- [ ] **Step 6.5: Commit**

```bash
git add nexo-backend/src/
git commit -m "feat(restaurante): update RecipeCard service with prep steps, CMV gas/labor, image upload"
```

---

## Task 7: Estender `FoodServiceSettings` com Custos Operacionais

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/FoodServiceSettingsConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/FoodServiceSettingsService.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FoodServiceSettingsController.cs`

- [ ] **Step 7.1: Adicionar campos ao domínio**

Em `FoodServiceSettings.cs`, adicione após `AcceptingOrders`:
```csharp
public decimal CostPerMinuteGas       { get; private set; }
public decimal CostPerMinuteLaborRate { get; private set; }
```

No `CreateDefault`, inicialize:
```csharp
CostPerMinuteGas       = 0m,
CostPerMinuteLaborRate = 0m,
```

Adicione o método:
```csharp
public void UpdateOperationalCosts(decimal costPerMinuteGas, decimal costPerMinuteLaborRate)
{
    CostPerMinuteGas       = costPerMinuteGas       >= 0 ? costPerMinuteGas       : 0;
    CostPerMinuteLaborRate = costPerMinuteLaborRate  >= 0 ? costPerMinuteLaborRate : 0;
    SetUpdatedAt();
}
```

- [ ] **Step 7.2: Mapear colunas**

Em `FoodServiceSettingsConfiguration.cs`, adicione após `BusinessHoursJson`:
```csharp
builder.Property(x => x.CostPerMinuteGas)
    .HasColumnName("cost_per_minute_gas")
    .HasColumnType("numeric(18,4)").HasDefaultValue(0m).IsRequired();
builder.Property(x => x.CostPerMinuteLaborRate)
    .HasColumnName("cost_per_minute_labor")
    .HasColumnType("numeric(18,4)").HasDefaultValue(0m).IsRequired();
```

- [ ] **Step 7.3: Gerar e aplicar migration**

```bash
cd nexo-backend
dotnet ef migrations add AddOperationalCostsToFoodServiceSettings \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

- [ ] **Step 7.4: Atualizar DTOs em `RestauranteDtos.cs`**

Adicione novo record e atualize os existentes (seção FOOD SERVICE SETTINGS):
```csharp
public record UpdateOperationalCostsRequest(decimal CostPerMinuteGas, decimal CostPerMinuteLaborRate);

// Substitua FoodServiceSettingsDto adicionando os 2 campos ao final:
public record FoodServiceSettingsDto(
    Guid     Id,
    string   StoreType,
    bool     CouvertEnabled,
    decimal? CouvertPricePerPerson,
    bool     CouvertAutomatic,
    bool     ServiceFeeEnabled,
    decimal? ServiceFeePercent,
    string   OrderTypesEnabled,
    string?  DisplayName,
    string?  LogoUrl,
    string?  CoverImageUrl,
    string?  Description,
    string?  WhatsAppPhone,
    string?  BusinessHoursJson,
    bool     AcceptingOrders,
    bool     DeliveryEnabled,
    bool     TakeawayEnabled,
    decimal  CostPerMinuteGas,          // ← novo
    decimal  CostPerMinuteLaborRate);   // ← novo
```

- [ ] **Step 7.5: Atualizar `FoodServiceSettingsService.cs`**

No método `Map`, adicione os dois campos ao `FoodServiceSettingsDto`:
```csharp
private static FoodServiceSettingsDto Map(FoodServiceSettings s) => new(
    s.Id, s.StoreType,
    s.CouvertEnabled, s.CouvertPricePerPerson, s.CouvertAutomatic,
    s.ServiceFeeEnabled, s.ServiceFeePercent,
    s.OrderTypesEnabled,
    s.DisplayName, s.LogoUrl, s.CoverImageUrl,
    s.Description, s.WhatsAppPhone, s.BusinessHoursJson,
    s.AcceptingOrders, s.DeliveryEnabled, s.TakeawayEnabled,
    s.CostPerMinuteGas, s.CostPerMinuteLaborRate);  // ← novo
```

Adicione o método:
```csharp
public async Task<FoodServiceSettingsDto> UpdateOperationalCostsAsync(
    UpdateOperationalCostsRequest req, CancellationToken ct = default)
{
    var settings = await _repo.GetCurrentStoreAsync(ct);
    if (settings is null)
    {
        settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
        await _repo.AddAsync(settings, ct);
    }
    settings.UpdateOperationalCosts(req.CostPerMinuteGas, req.CostPerMinuteLaborRate);
    await _repo.SaveChangesAsync(ct);
    return Map(settings);
}
```

- [ ] **Step 7.6: Adicionar endpoint no controller**

Em `FoodServiceSettingsController.cs`, adicione:
```csharp
[HttpPut("costs")]
public async Task<IActionResult> UpdateCosts(
    [FromBody] UpdateOperationalCostsRequest req, CancellationToken ct)
    => Ok(await _service.UpdateOperationalCostsAsync(req, ct));
```

- [ ] **Step 7.7: Build final do backend**

```bash
cd nexo-backend && dotnet build
```

Expected: zero erros.

- [ ] **Step 7.8: Commit**

```bash
git add nexo-backend/src/ nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): add gas/labor cost rates to FoodServiceSettings for CMV calc"
```

---

## Task 8: Teste de Integração — CMV com Gás e MO

**Files:**
- Create: `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/RecipeCardCmvTests.cs`

- [ ] **Step 8.1: Escrever teste**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Products;
using Nexo.Application.Modules.Restaurante;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

[Collection("Integration")]
public class RecipeCardCmvTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RecipeCardCmvTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateApiClient();
    }

    public async Task InitializeAsync()
    {
        var r = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "nexo@2026" });
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        // Garantir módulo restaurante
        await EnsureModuleAsync("restaurante");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task EnsureModuleAsync(string key)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Nexo.Infrastructure.Persistence.NexoDbContext>();
        var tenant = await db.Tenants.FirstAsync();
        if (!db.ModuleSubscriptions.Any(m => m.TenantId == tenant.Id && m.ModuleKey == key))
        {
            db.ModuleSubscriptions.Add(Nexo.Domain.Entities.ModuleSubscription.Create(
                tenant.Id, key, Nexo.Domain.Enums.SubscriptionPlan.Lifetime));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CMV_IncludesGasAndLaborCost_WhenSettingsConfigured()
    {
        // Arrange — create ingredient product (isIngredient=true, costPrice=10)
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ingR = await _client.PostAsJsonAsync("/api/products", new
        {
            code = $"ING-{suffix}", name = $"Ingredient {suffix}",
            unit = "Kg", salePrice = 0m, costPrice = 10m,
            isIngredient = true, trackStock = false
        });
        ingR.StatusCode.Should().Be(HttpStatusCode.Created);
        var ing = await ingR.Content.ReadFromJsonAsync<ProductDto>();

        // Arrange — create menu item product (isIngredient=false, salePrice=40)
        var prodR = await _client.PostAsJsonAsync("/api/products", new
        {
            code = $"PRATO-{suffix}", name = $"Prato {suffix}",
            unit = "Un", salePrice = 40m, costPrice = 0m,
            isIngredient = false, trackStock = false
        });
        var prod = await prodR.Content.ReadFromJsonAsync<ProductDto>();

        // Arrange — configure gas=0.10/min, labor=0.20/min
        await _client.PutAsJsonAsync("/api/restaurante/settings/costs", new
        {
            costPerMinuteGas = 0.10m,
            costPerMinuteLaborRate = 0.20m
        });

        // Arrange — create recipe card (yield=1, 1 ingredient @ 2 Kg, 10min prep)
        var cardR = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards", new
        {
            productId = prod!.Id, yield = 1m, yieldUnit = "porção", hasPrep = true
        });
        cardR.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardR.Content.ReadFromJsonAsync<RecipeCardDto>();

        // Add ingredient: 2 kg @ R$10/kg = R$20
        await _client.PostAsJsonAsync($"/api/restaurante/recipe-cards/{card!.Id}/ingredients", new
        {
            ingredientProductId = ing!.Id, quantity = 2m, unit = "Kg"
        });

        // Update with 10-minute prep step
        var updateR = await _client.PutAsJsonAsync($"/api/restaurante/recipe-cards/{card.Id}", new
        {
            yield = 1m, yieldUnit = "porção", hasPrep = true,
            prepSteps = new[] { new { order = 1, description = "Cozinhar", durationMinutes = 10 } },
            assemblyNotes = (string?)null,
            requiresPackaging = false,
            packagingProductId = (Guid?)null
        });
        updateR.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateR.Content.ReadFromJsonAsync<RecipeCardDto>();

        // Assert
        // ingredientCost = 2kg * 10 / 1 yield = 20.0
        updated!.IngredientCost.Should().Be(20.0000m);
        // gasCost = 10min * 0.10 = 1.0
        updated.GasCost.Should().Be(1.0000m);
        // laborCost = 10min * 0.20 = 2.0
        updated.LaborCost.Should().Be(2.0000m);
        // calculatedCost = 23.0
        updated.CalculatedCost.Should().Be(23.0000m);
        // cmv% = 23/40 * 100 = 57.5
        updated.CmvPercent.Should().Be(57.50m);
        updated.TotalPrepTimeMin.Should().Be(10);
    }

    [Fact]
    public async Task GetProducts_FilterByIsIngredient_ReturnsCorrectSubset()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await _client.PostAsJsonAsync("/api/products", new
        {
            code = $"ING2-{suffix}", name = $"Insumo {suffix}",
            unit = "Kg", salePrice = 0m, isIngredient = true, trackStock = false
        });
        await _client.PostAsJsonAsync("/api/products", new
        {
            code = $"CARD2-{suffix}", name = $"Cardapio {suffix}",
            unit = "Un", salePrice = 20m, isIngredient = false, trackStock = false
        });

        var ingList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products?isIngredient=true");
        var cardList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products?isIngredient=false");

        ingList!.Should().OnlyContain(p => p.IsIngredient);
        cardList!.Should().OnlyContain(p => !p.IsIngredient);
    }
}
```

- [ ] **Step 8.2: Rodar testes**

```bash
cd nexo-backend
dotnet test tests/Nexo.IntegrationTests \
  --filter "FullyQualifiedName~RecipeCardCmvTests" \
  --logger "console;verbosity=normal"
```

Expected: 2 testes passando.

- [ ] **Step 8.3: Commit**

```bash
git add nexo-backend/tests/
git commit -m "test(restaurante): integration tests for CMV calculation with gas and labor rates"
```

---

## Task 9: Frontend — Tipos e API de Produtos

**Files:**
- Modify: `nexo-main/src/modules/products/types/index.ts`
- Modify: `nexo-main/src/modules/products/api/products.api.ts`
- Modify: `nexo-main/src/modules/products/hooks/use-products.ts`

- [ ] **Step 9.1: Adicionar `isIngredient` aos tipos**

Em `types/index.ts`, adicione `isIngredient: boolean` à interface `ProductDto` (após `isActive`):
```typescript
export interface ProductDto {
  id: string;
  code: string;
  barcode: string | null;
  name: string;
  description: string | null;
  categoryId: string | null;
  unit: string;
  costPrice: number;
  salePrice: number;
  trackStock: boolean;
  minStockQuantity: number | null;
  maxStockQuantity: number | null;
  isActive: boolean;
  isIngredient: boolean;       // ← novo
  createdAt: string;
  updatedAt: string;
}
```

Na interface `Product`, adicione `isIngredient: boolean` após `isActive`.

Na função `dtoToProduct`, adicione `isIngredient: dto.isIngredient,`.

Em `emptyProduct`, adicione `isIngredient: false,`.

- [ ] **Step 9.2: Atualizar API**

Em `products.api.ts`, adicione `isIngredient?: boolean` aos dois payload interfaces:
```typescript
export interface CreateProductPayload {
  code: string;
  name: string;
  unit: string;
  salePrice: number;
  costPrice?: number;
  barcode?: string | null;
  description?: string | null;
  categoryId?: string | null;
  trackStock?: boolean;
  minStockQuantity?: number | null;
  maxStockQuantity?: number | null;
  isIngredient?: boolean;   // ← novo
}

export interface UpdateProductPayload {
  name: string;
  unit: string;
  costPrice: number;
  salePrice: number;
  trackStock: boolean;
  barcode?: string | null;
  description?: string | null;
  categoryId?: string | null;
  minStockQuantity?: number | null;
  maxStockQuantity?: number | null;
  isIngredient?: boolean;   // ← novo
}
```

Substitua `fetchProducts`:
```typescript
export function fetchProducts(
  includeInactive = false,
  isIngredient?: boolean
): Promise<ProductDto[]> {
  const params = new URLSearchParams();
  if (includeInactive) params.set("includeInactive", "true");
  if (isIngredient !== undefined) params.set("isIngredient", String(isIngredient));
  const qs = params.toString();
  return apiClient.get<ProductDto[]>(`/products${qs ? `?${qs}` : ""}`);
}
```

- [ ] **Step 9.3: Atualizar hooks**

Em `use-products.ts`, substitua `useProducts`:
```typescript
export function useProducts(options?: { includeInactive?: boolean; isIngredient?: boolean }) {
  const { includeInactive = false, isIngredient } = options ?? {};
  return useQuery({
    queryKey: [...PRODUCTS_KEY, { includeInactive, isIngredient }],
    queryFn: () => fetchProducts(includeInactive, isIngredient),
  });
}
```

- [ ] **Step 9.4: Commit**

```bash
cd nexo-main
git add src/modules/products/
git commit -m "feat(products): add isIngredient to frontend types, API, and hooks"
```

---

## Task 10: Frontend — Atualizar EstoquePage e ProdutosPage

**Files:**
- Modify: `nexo-main/src/modules/inventory/pages/EstoquePage.tsx`
- Modify: `nexo-main/src/modules/products/pages/ProdutosPage.tsx`

- [ ] **Step 10.1: Atualizar `EstoquePage` para filtrar ingredientes**

Em `EstoquePage.tsx`, mude a linha de importação de `useProducts` para incluir o filtro. Substitua o bloco de queries:
```typescript
const { data: stockItems = [], isLoading: loadingStock, isError } = useStockItems();
const { data: products = [] } = useProducts({ isIngredient: true });
```

> O join existente já filtra por `productId`, então os `StockItem`s que não tiverem um produto `isIngredient=true` correspondente não aparecerão no enriquecido. Mas para garantir, também filtre `enriched` por `isIngredient`:

No `useMemo` de `enriched`, após o `map`, adicione `.filter(e => e.isIngredient !== false)`. (Opcional — o join já garante, mas torna a intenção explícita.)

Atualize o `PageHeader`:
```tsx
<PageHeader
  title="Ingredientes"
  description="Insumos e ingredientes usados nos pratos. Controle de estoque e histórico de preços."
  actions={
    <Button onClick={() => navigate("/produtos/novo?tipo=ingrediente")}>
      <Plus className="h-4 w-4 mr-1" /> Novo ingrediente
    </Button>
  }
/>
```

- [ ] **Step 10.2: Atualizar `ProdutosPage` para filtrar cardápio**

Em `ProdutosPage.tsx`, substitua a query:
```typescript
const { data: products = [], isLoading: loadingProducts, isError } = useProducts({ isIngredient: false });
```

- [ ] **Step 10.3: Commit**

```bash
git add nexo-main/src/modules/inventory/pages/EstoquePage.tsx \
        nexo-main/src/modules/products/pages/ProdutosPage.tsx
git commit -m "feat(estoque): filter EstoquePage by isIngredient=true, ProdutosPage by false"
```

---

## Task 11: Frontend — `IngredientPriceSection` e `ProductFormPage`

**Files:**
- Create: `nexo-main/src/modules/products/components/IngredientPriceSection.tsx`
- Modify: `nexo-main/src/modules/products/pages/ProductFormPage.tsx`

- [ ] **Step 11.1: Criar `IngredientPriceSection`**

Crie `IngredientPriceSection.tsx`:
```tsx
import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { apiClient } from "@/services/api-client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { toast } from "sonner";

interface PriceEntry { id: string; price: number; purchasedAt: string; }
interface PriceHistory { lastPrice: number | null; averagePrice: number | null; history: PriceEntry[]; }

const PRICE_KEY = (id: string) => ["product-purchase-prices", id] as const;

function fetchPriceHistory(productId: string): Promise<PriceHistory> {
  return apiClient.get<PriceHistory>(`/products/${productId}/purchase-prices`);
}

interface Props { productId: string; }

export function IngredientPriceSection({ productId }: Props) {
  const qc = useQueryClient();
  const [newPrice, setNewPrice] = useState("");
  const [newDate, setNewDate]   = useState(new Date().toISOString().slice(0, 10));

  const { data } = useQuery({
    queryKey: PRICE_KEY(productId),
    queryFn:  () => fetchPriceHistory(productId),
  });

  const addMut = useMutation({
    mutationFn: () =>
      apiClient.post<PriceEntry>(`/products/${productId}/purchase-prices`, {
        price: parseFloat(newPrice),
        purchasedAt: newDate,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PRICE_KEY(productId) });
      setNewPrice("");
      toast.success("Preço registrado.");
    },
  });

  const fmt = (v: number | null) =>
    v !== null ? `R$ ${v.toFixed(4)}` : "—";

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div className="rounded-lg border p-3 space-y-1">
          <p className="text-xs text-muted-foreground">Última compra</p>
          <p className="text-lg font-semibold">{fmt(data?.lastPrice ?? null)}</p>
        </div>
        <div className="rounded-lg border p-3 space-y-1">
          <p className="text-xs text-muted-foreground">Preço médio (últimas 5)</p>
          <p className="text-lg font-semibold">{fmt(data?.averagePrice ?? null)}</p>
        </div>
      </div>

      <div className="flex gap-2 items-end">
        <div className="flex-1 space-y-1">
          <Label className="text-xs">Nova compra — valor (R$)</Label>
          <Input
            type="number" step="0.0001" min="0"
            placeholder="0,0000"
            value={newPrice}
            onChange={(e) => setNewPrice(e.target.value)}
          />
        </div>
        <div className="space-y-1">
          <Label className="text-xs">Data</Label>
          <Input
            type="date"
            value={newDate}
            onChange={(e) => setNewDate(e.target.value)}
          />
        </div>
        <Button
          size="sm"
          disabled={!newPrice || isNaN(parseFloat(newPrice)) || addMut.isPending}
          onClick={() => addMut.mutate()}
        >
          <Plus className="h-3.5 w-3.5 mr-1" />
          Registrar
        </Button>
      </div>

      {data?.history.length ? (
        <table className="w-full text-xs">
          <thead>
            <tr className="text-muted-foreground border-b">
              <th className="text-left pb-1">Data</th>
              <th className="text-right pb-1">Preço</th>
            </tr>
          </thead>
          <tbody>
            {data.history.map((e) => (
              <tr key={e.id} className="border-b last:border-0">
                <td className="py-1">{new Date(e.purchasedAt).toLocaleDateString("pt-BR")}</td>
                <td className="text-right">{fmt(e.price)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}
```

- [ ] **Step 11.2: Atualizar `ProductFormPage`**

Em `ProductFormPage.tsx`, adicione os imports necessários:
```typescript
import { useNavigate, useLocation } from "react-router-dom";
import { ChefHat, Package } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { IngredientPriceSection } from "../components/IngredientPriceSection";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
```

No componente, adicione:
```typescript
const location = useLocation();
const { session } = useAuth();
const hasRestauranteModule = session?.modules.includes("restaurante") ?? false;

// Pré-setar isIngredient se a URL tiver ?tipo=ingrediente
useEffect(() => {
  if (isNew && location.search.includes("tipo=ingrediente")) {
    setFormData((prev) => ({ ...prev, isIngredient: true }));
  }
}, [isNew, location.search]);
```

Adicione o toggle de tipo antes do formulário principal (dentro do `SectionCard`):
```tsx
{/* Toggle ingrediente / cardápio */}
<div className="flex items-center gap-4 pb-4 border-b mb-4">
  <div className="flex items-center gap-2">
    <Package className="h-4 w-4 text-muted-foreground" />
    <Label className="text-sm font-medium">Item do cardápio</Label>
  </div>
  <Switch
    checked={formData.isIngredient ?? false}
    onCheckedChange={(v) => setFormData((prev) => ({ ...prev, isIngredient: v }))}
  />
  <div className="flex items-center gap-2">
    <ChefHat className="h-4 w-4 text-muted-foreground" />
    <Label className="text-sm font-medium">Ingrediente de estoque</Label>
  </div>
</div>
```

Adicione após o `ProductForm`, antes do fechamento do `SectionCard`:
```tsx
{/* Seção de preços de compra — apenas para ingredientes salvos */}
{!isNew && formData.isIngredient && id && (
  <div className="mt-6 pt-6 border-t space-y-3">
    <h3 className="text-sm font-semibold">Histórico de preços de compra</h3>
    <IngredientPriceSection productId={id} />
  </div>
)}

{/* Link para ficha técnica — apenas para cardápio com restaurante */}
{!isNew && !formData.isIngredient && hasRestauranteModule && id && (
  <div className="mt-6 pt-6 border-t flex items-center justify-between">
    <div>
      <h3 className="text-sm font-semibold">Ficha Técnica</h3>
      <p className="text-xs text-muted-foreground">CMV, ingredientes e modo de preparo.</p>
    </div>
    <Button variant="outline" onClick={() => navigate(`/produtos/${id}/ficha`)}>
      <ChefHat className="h-4 w-4 mr-2" />
      Abrir Ficha Técnica
    </Button>
  </div>
)}
```

No `payload` do `handleSave`, adicione:
```typescript
isIngredient: formData.isIngredient ?? false,
```

- [ ] **Step 11.3: Commit**

```bash
git add nexo-main/src/modules/products/
git commit -m "feat(products): add ingredient type toggle and purchase price history section"
```

---

## Task 12: Frontend — Tipos, API e Hooks da Ficha Técnica

**Files:**
- Create: `nexo-main/src/modules/restaurante/types/recipe-card.types.ts`
- Create: `nexo-main/src/modules/restaurante/api/recipe-card.api.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/use-recipe-card.ts`

- [ ] **Step 12.1: Criar tipos**

Crie `recipe-card.types.ts`:
```typescript
export interface PrepStepDto {
  order: number;
  description: string;
  durationMinutes: number | null;
}

export interface RecipeIngredientDto {
  id: string;
  ingredientProductId: string;
  ingredientName: string;
  ingredientCode: string;
  quantity: number;
  unit: string;
  currentCostPrice: number;
  lineCost: number;
}

export interface RecipeCardDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  salePrice: number;
  imageUrl: string | null;
  yield: number;
  yieldUnit: string;
  hasPrep: boolean;
  prepSteps: PrepStepDto[];
  totalPrepTimeMin: number | null;
  assemblyNotes: string | null;
  requiresPackaging: boolean;
  packagingProductId: string | null;
  packagingProductName: string | null;
  isActive: boolean;
  notes: string | null;
  ingredientCost: number;
  gasCost: number;
  laborCost: number;
  calculatedCost: number;
  cmvPercent: number;
  ingredients: RecipeIngredientDto[];
  createdAt: string;
}

export interface CreateRecipeCardPayload {
  productId: string;
  yield: number;
  yieldUnit: string;
  hasPrep?: boolean;
  notes?: string | null;
}

export interface UpdateRecipeCardPayload {
  yield: number;
  yieldUnit: string;
  hasPrep: boolean;
  prepSteps: PrepStepDto[];
  assemblyNotes: string | null;
  requiresPackaging: boolean;
  packagingProductId: string | null;
  notes?: string | null;
}

export interface AddIngredientPayload {
  ingredientProductId: string;
  quantity: number;
  unit: string;
}
```

- [ ] **Step 12.2: Criar API**

Crie `recipe-card.api.ts`:
```typescript
import { apiClient } from "@/services/api-client";
import type { RecipeCardDto, CreateRecipeCardPayload, UpdateRecipeCardPayload, AddIngredientPayload } from "../types/recipe-card.types";

const BASE = "/restaurante/recipe-cards";

export const fetchRecipeCardByProduct = (productId: string) =>
  apiClient.get<RecipeCardDto>(`${BASE}/product/${productId}`);

export const fetchRecipeCardById = (id: string) =>
  apiClient.get<RecipeCardDto>(`${BASE}/${id}`);

export const createRecipeCard = (payload: CreateRecipeCardPayload) =>
  apiClient.post<RecipeCardDto>(BASE, payload);

export const updateRecipeCard = (id: string, payload: UpdateRecipeCardPayload) =>
  apiClient.put<RecipeCardDto>(`${BASE}/${id}`, payload);

export const addIngredient = (id: string, payload: AddIngredientPayload) =>
  apiClient.post<RecipeCardDto>(`${BASE}/${id}/ingredients`, payload);

export const removeIngredient = (cardId: string, ingredientId: string) =>
  apiClient.delete<RecipeCardDto>(`${BASE}/${cardId}/ingredients/${ingredientId}`);

export const uploadRecipeImage = (cardId: string, file: File): Promise<RecipeCardDto> => {
  const form = new FormData();
  form.append("file", file);
  return apiClient.postForm<RecipeCardDto>(`${BASE}/${cardId}/image`, form);
};
```

> Se `apiClient` não tiver `postForm`, adicione ao seu `api-client.ts`:
> ```typescript
> postForm<T>(url: string, form: FormData): Promise<T> {
>   return this.instance.post(url, form, {
>     headers: { "Content-Type": "multipart/form-data" },
>   }).then(r => r.data);
> }
> ```

- [ ] **Step 12.3: Criar hooks**

Crie `use-recipe-card.ts`:
```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchRecipeCardByProduct, fetchRecipeCardById,
  createRecipeCard, updateRecipeCard,
  addIngredient, removeIngredient, uploadRecipeImage,
  type CreateRecipeCardPayload, type UpdateRecipeCardPayload, type AddIngredientPayload,
} from "../api/recipe-card.api";

export const RECIPE_CARD_KEY = (productId: string) =>
  ["recipe-card", "product", productId] as const;

export function useRecipeCardByProduct(productId: string) {
  return useQuery({
    queryKey: RECIPE_CARD_KEY(productId),
    queryFn:  () => fetchRecipeCardByProduct(productId),
    retry:    false,
  });
}

export function useCreateRecipeCard(productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateRecipeCardPayload) => createRecipeCard(payload),
    onSuccess:  () => qc.invalidateQueries({ queryKey: RECIPE_CARD_KEY(productId) }),
  });
}

export function useUpdateRecipeCard(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateRecipeCardPayload) => updateRecipeCard(cardId, payload),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useAddIngredient(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: AddIngredientPayload) => addIngredient(cardId, payload),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useRemoveIngredient(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ingredientId: string) => removeIngredient(cardId, ingredientId),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}

export function useUploadRecipeImage(cardId: string, productId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => uploadRecipeImage(cardId, file),
    onSuccess:  (data) => qc.setQueryData(RECIPE_CARD_KEY(productId), data),
  });
}
```

- [ ] **Step 12.4: Commit**

```bash
git add nexo-main/src/modules/restaurante/types/recipe-card.types.ts \
        nexo-main/src/modules/restaurante/api/recipe-card.api.ts \
        nexo-main/src/modules/restaurante/hooks/use-recipe-card.ts
git commit -m "feat(restaurante): add recipe card types, API and TanStack Query hooks"
```

---

## Task 13: Frontend — `PrepStepsEditor` e `CmvBar`

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/PrepStepsEditor.tsx`
- Create: `nexo-main/src/modules/restaurante/components/CmvBar.tsx`

- [ ] **Step 13.1: Criar `PrepStepsEditor`**

Crie `PrepStepsEditor.tsx`:
```tsx
import { ArrowUp, ArrowDown, Trash2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { PrepStepDto } from "../types/recipe-card.types";

interface Props {
  steps: PrepStepDto[];
  onChange: (steps: PrepStepDto[]) => void;
}

export function PrepStepsEditor({ steps, onChange }: Props) {
  const add = () =>
    onChange([...steps, { order: steps.length + 1, description: "", durationMinutes: null }]);

  const update = (index: number, patch: Partial<PrepStepDto>) =>
    onChange(steps.map((s, i) => (i === index ? { ...s, ...patch } : s)));

  const remove = (index: number) =>
    onChange(steps.filter((_, i) => i !== index).map((s, i) => ({ ...s, order: i + 1 })));

  const move = (from: number, to: number) => {
    if (to < 0 || to >= steps.length) return;
    const next = [...steps];
    [next[from], next[to]] = [next[to], next[from]];
    onChange(next.map((s, i) => ({ ...s, order: i + 1 })));
  };

  const totalMin = steps.reduce((acc, s) => acc + (s.durationMinutes ?? 0), 0);

  return (
    <div className="space-y-2">
      {steps.map((step, i) => (
        <div key={i} className="flex gap-2 items-start">
          <span className="text-xs text-muted-foreground w-5 pt-2.5 shrink-0">{i + 1}.</span>
          <Input
            className="flex-1 text-sm"
            placeholder="Descrição do passo"
            value={step.description}
            onChange={(e) => update(i, { description: e.target.value })}
          />
          <Input
            className="w-20 text-sm"
            type="number" min="0" placeholder="min"
            value={step.durationMinutes ?? ""}
            onChange={(e) =>
              update(i, { durationMinutes: e.target.value ? parseInt(e.target.value) : null })
            }
          />
          <div className="flex gap-1 shrink-0">
            <Button size="icon" variant="ghost" className="h-8 w-7" onClick={() => move(i, i - 1)}>
              <ArrowUp className="h-3.5 w-3.5" />
            </Button>
            <Button size="icon" variant="ghost" className="h-8 w-7" onClick={() => move(i, i + 1)}>
              <ArrowDown className="h-3.5 w-3.5" />
            </Button>
            <Button size="icon" variant="ghost" className="h-8 w-7 text-destructive" onClick={() => remove(i)}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>
      ))}
      <div className="flex items-center justify-between pt-1">
        <Button size="sm" variant="outline" onClick={add}>
          <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar passo
        </Button>
        {totalMin > 0 && (
          <span className="text-xs text-muted-foreground">Tempo total: {totalMin} min</span>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 13.2: Criar `CmvBar`**

Crie `CmvBar.tsx`:
```tsx
import { cn } from "@/lib/utils";

interface Props {
  ingredientCost: number;
  gasCost: number;
  laborCost: number;
  calculatedCost: number;
  salePrice: number;
  cmvPercent: number;
}

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL", minimumFractionDigits: 2 });
}

function cmvColor(pct: number) {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

export function CmvBar({ ingredientCost, gasCost, laborCost, calculatedCost, salePrice, cmvPercent }: Props) {
  return (
    <div className="sticky bottom-0 z-10 border-t bg-background/95 backdrop-blur px-6 py-3">
      <div className="flex items-center gap-6 flex-wrap text-sm">
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Ingredientes:</span>
          <span className="font-medium text-foreground">{fmt(ingredientCost)}</span>
        </div>
        {gasCost > 0 && (
          <div className="flex items-center gap-1.5 text-muted-foreground">
            <span>Gás:</span>
            <span className="font-medium text-foreground">{fmt(gasCost)}</span>
          </div>
        )}
        {laborCost > 0 && (
          <div className="flex items-center gap-1.5 text-muted-foreground">
            <span>MO:</span>
            <span className="font-medium text-foreground">{fmt(laborCost)}</span>
          </div>
        )}
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Custo total:</span>
          <span className="font-medium text-foreground">{fmt(calculatedCost)}</span>
        </div>
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Venda:</span>
          <span className="font-medium text-foreground">{fmt(salePrice)}</span>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <span className="text-muted-foreground">CMV</span>
          <span className={cn("text-xl font-bold tabular-nums", cmvColor(cmvPercent))}>
            {cmvPercent.toFixed(1)}%
          </span>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 13.3: Commit**

```bash
git add nexo-main/src/modules/restaurante/components/PrepStepsEditor.tsx \
        nexo-main/src/modules/restaurante/components/CmvBar.tsx
git commit -m "feat(restaurante): add PrepStepsEditor and CmvBar components"
```

---

## Task 14: Frontend — `RecipeCardPage`

**Files:**
- Create: `nexo-main/src/modules/restaurante/pages/RecipeCardPage.tsx`

- [ ] **Step 14.1: Criar a página**

Crie `RecipeCardPage.tsx`:
```tsx
import { useState, useEffect, useCallback } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { SectionCard } from "@/components/shared/SectionCard";
import { PageHeader } from "@/components/shared/PageHeader";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useProducts } from "@/modules/products/hooks/use-products";
import { CmvBar } from "../components/CmvBar";
import { PrepStepsEditor } from "../components/PrepStepsEditor";
import {
  useRecipeCardByProduct, useCreateRecipeCard, useUpdateRecipeCard,
  useAddIngredient, useRemoveIngredient, useUploadRecipeImage,
} from "../hooks/use-recipe-card";
import type { PrepStepDto, AddIngredientPayload } from "../types/recipe-card.types";

export default function RecipeCardPage() {
  const { id: productId } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: card, isLoading, isError } = useRecipeCardByProduct(productId!);
  const { data: ingredients = [] } = useProducts({ isIngredient: true });

  const createMut  = useCreateRecipeCard(productId!);
  const updateMut  = useUpdateRecipeCard(card?.id ?? "", productId!);
  const addIngMut  = useAddIngredient(card?.id ?? "", productId!);
  const rmIngMut   = useRemoveIngredient(card?.id ?? "", productId!);
  const uploadMut  = useUploadRecipeImage(card?.id ?? "", productId!);

  // Local form state (synced from card on load)
  const [yield_,    setYield]    = useState(1);
  const [yieldUnit, setYieldUnit] = useState("porção");
  const [hasPrep,   setHasPrep]  = useState(true);
  const [steps,     setSteps]    = useState<PrepStepDto[]>([]);
  const [assembly,  setAssembly] = useState("");
  const [needsPkg,  setNeedsPkg] = useState(false);
  const [pkgId,     setPkgId]    = useState<string | null>(null);
  const [notes,     setNotes]    = useState("");
  const [saving,    setSaving]   = useState(false);
  // New ingredient row state
  const [newIngId,  setNewIngId]  = useState<string>("");
  const [newIngQty, setNewIngQty] = useState<string>("");

  useEffect(() => {
    if (!card) return;
    setYield(card.yield);
    setYieldUnit(card.yieldUnit);
    setHasPrep(card.hasPrep);
    setSteps(card.prepSteps);
    setAssembly(card.assemblyNotes ?? "");
    setNeedsPkg(card.requiresPackaging);
    setPkgId(card.packagingProductId);
    setNotes(card.notes ?? "");
  }, [card]);

  // Compute live CMV from current card data + local state
  const liveCost = card ? (() => {
    const ing    = card.ingredientCost;
    const gas    = card.gasCost;
    const labor  = card.laborCost;
    return { ingredientCost: ing, gasCost: gas, laborCost: labor,
             calculatedCost: card.calculatedCost, salePrice: card.salePrice, cmvPercent: card.cmvPercent };
  })() : null;

  const handleSave = useCallback(async () => {
    if (!productId) return;
    setSaving(true);
    try {
      if (!card) {
        await createMut.mutateAsync({ productId, yield: yield_, yieldUnit, hasPrep, notes: notes || null });
        toast.success("Ficha técnica criada.");
      } else {
        await updateMut.mutateAsync({
          yield: yield_, yieldUnit, hasPrep,
          prepSteps: steps,
          assemblyNotes: assembly || null,
          requiresPackaging: needsPkg,
          packagingProductId: needsPkg ? pkgId : null,
          notes: notes || null,
        });
        toast.success("Ficha técnica salva.");
      }
    } catch {
      toast.error("Erro ao salvar ficha técnica.");
    } finally {
      setSaving(false);
    }
  }, [card, productId, yield_, yieldUnit, hasPrep, steps, assembly, needsPkg, pkgId, notes]);

  const handleAddIngredient = async () => {
    if (!card || !newIngId || !newIngQty) return;
    const ing = ingredients.find(i => i.id === newIngId);
    if (!ing) return;
    try {
      await addIngMut.mutateAsync({ ingredientProductId: newIngId, quantity: parseFloat(newIngQty), unit: ing.unit });
      setNewIngId(""); setNewIngQty("");
    } catch { toast.error("Erro ao adicionar ingrediente."); }
  };

  const handleImageUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !card) return;
    try {
      await uploadMut.mutateAsync(file);
      toast.success("Foto atualizada.");
    } catch { toast.error("Erro ao enviar foto."); }
  };

  if (isLoading) return (
    <div className="space-y-4 p-6"><Skeleton className="h-16 w-full" /><Skeleton className="h-96 w-full" /></div>
  );

  return (
    <div className="flex flex-col min-h-screen">
      <div className="flex-1 space-y-6 p-6 pb-24">
        <PageHeader
          title={card ? `Ficha Técnica — ${card.productName}` : "Nova Ficha Técnica"}
          description="CMV, ingredientes, modo de preparo e montagem do prato."
          actions={
            <div className="flex gap-2">
              <Button variant="outline" onClick={() => navigate(`/produtos/${productId}`)}>
                <ArrowLeft className="h-4 w-4 mr-1" /> Voltar
              </Button>
              <Button onClick={handleSave} disabled={saving}>
                {saving ? "Salvando…" : "Salvar"}
              </Button>
            </div>
          }
        />

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Coluna esquerda */}
          <div className="space-y-6">
            <SectionCard>
              <h3 className="text-sm font-semibold mb-4">Dados gerais</h3>

              {/* Foto */}
              <div className="space-y-2 mb-4">
                <Label className="text-xs">Foto do prato</Label>
                {card?.imageUrl && (
                  <img src={card.imageUrl} alt="prato" className="h-40 rounded-md object-cover w-full" />
                )}
                <label className="flex items-center gap-2 cursor-pointer border rounded-md px-3 py-2 text-sm text-muted-foreground hover:bg-muted/40 transition-colors">
                  <Upload className="h-4 w-4" />
                  {card?.imageUrl ? "Trocar foto" : "Enviar foto"}
                  <input type="file" accept="image/jpeg,image/png,image/webp" className="hidden" onChange={handleImageUpload} />
                </label>
              </div>

              {/* Rendimento */}
              <div className="grid grid-cols-2 gap-3 mb-4">
                <div className="space-y-1">
                  <Label className="text-xs">Rendimento (qtd)</Label>
                  <Input type="number" min="0.001" step="0.001" value={yield_}
                    onChange={e => setYield(parseFloat(e.target.value) || 1)} />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Unidade</Label>
                  <Input value={yieldUnit} onChange={e => setYieldUnit(e.target.value)} placeholder="porção, kg…" />
                </div>
              </div>

              {/* Toggle tem preparo */}
              <div className="flex items-center gap-3">
                <Switch checked={hasPrep} onCheckedChange={setHasPrep} />
                <Label className="text-sm">Tem preparo / produção</Label>
              </div>
            </SectionCard>

            {hasPrep && card && (
              <>
                {/* Ingredientes */}
                <SectionCard>
                  <h3 className="text-sm font-semibold mb-3">Ingredientes</h3>
                  {card.ingredients.map(ing => (
                    <div key={ing.id} className="flex items-center gap-2 py-1.5 border-b last:border-0">
                      <span className="flex-1 text-sm">{ing.ingredientName}</span>
                      <span className="text-sm text-muted-foreground">{ing.quantity} {ing.unit}</span>
                      <span className="text-sm text-muted-foreground w-20 text-right">
                        {ing.lineCost.toLocaleString("pt-BR", { style: "currency", currency: "BRL" })}
                      </span>
                      <Button size="icon" variant="ghost" className="h-7 w-7 text-destructive"
                        onClick={() => rmIngMut.mutate(ing.id)}>
                        <X className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  ))}
                  <div className="flex gap-2 mt-3">
                    <Select value={newIngId} onValueChange={setNewIngId}>
                      <SelectTrigger className="flex-1 text-sm"><SelectValue placeholder="Selecionar ingrediente…" /></SelectTrigger>
                      <SelectContent>
                        {ingredients.filter(i => !card.ingredients.find(ci => ci.ingredientProductId === i.id))
                          .map(i => <SelectItem key={i.id} value={i.id}>{i.name} ({i.unit})</SelectItem>)}
                      </SelectContent>
                    </Select>
                    <Input className="w-24 text-sm" type="number" placeholder="Qtd" value={newIngQty}
                      onChange={e => setNewIngQty(e.target.value)} />
                    <Button size="sm" onClick={handleAddIngredient}
                      disabled={!newIngId || !newIngQty || addIngMut.isPending}>
                      + Add
                    </Button>
                  </div>
                </SectionCard>

                {/* Modo de preparo */}
                <SectionCard>
                  <h3 className="text-sm font-semibold mb-3">Modo de preparo</h3>
                  <PrepStepsEditor steps={steps} onChange={setSteps} />
                </SectionCard>
              </>
            )}
          </div>

          {/* Coluna direita */}
          <div className="space-y-6">
            <SectionCard>
              <h3 className="text-sm font-semibold mb-4">Montagem e embalagem</h3>
              <div className="space-y-4">
                <div className="space-y-1">
                  <Label className="text-xs">Montagem do prato</Label>
                  <Textarea rows={4} value={assembly} onChange={e => setAssembly(e.target.value)}
                    placeholder="Como montar e apresentar o prato…" />
                </div>
                <div className="flex items-center gap-3">
                  <Switch checked={needsPkg} onCheckedChange={setNeedsPkg} />
                  <Label className="text-sm">Requer embalagem</Label>
                </div>
                {needsPkg && (
                  <div className="space-y-1">
                    <Label className="text-xs">Tipo de embalagem (ingrediente do estoque)</Label>
                    <Select value={pkgId ?? ""} onValueChange={v => setPkgId(v || null)}>
                      <SelectTrigger><SelectValue placeholder="Selecionar…" /></SelectTrigger>
                      <SelectContent>
                        {ingredients.map(i => <SelectItem key={i.id} value={i.id}>{i.name}</SelectItem>)}
                      </SelectContent>
                    </Select>
                  </div>
                )}
                <div className="space-y-1">
                  <Label className="text-xs">Observações gerais</Label>
                  <Textarea rows={3} value={notes} onChange={e => setNotes(e.target.value)} placeholder="Notas adicionais…" />
                </div>
              </div>
            </SectionCard>
          </div>
        </div>
      </div>

      {/* Barra de CMV sticky */}
      {liveCost && (
        <CmvBar
          ingredientCost={liveCost.ingredientCost}
          gasCost={liveCost.gasCost}
          laborCost={liveCost.laborCost}
          calculatedCost={liveCost.calculatedCost}
          salePrice={liveCost.salePrice}
          cmvPercent={liveCost.cmvPercent}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 14.2: Commit**

```bash
git add nexo-main/src/modules/restaurante/pages/RecipeCardPage.tsx
git commit -m "feat(restaurante): add RecipeCardPage with ingredients, prep steps, assembly and CMV bar"
```

---

## Task 15: Frontend — Rota + Setup de Custos Operacionais

**Files:**
- Modify: `nexo-main/src/app/router/AppRouter.tsx`
- Modify: `nexo-main/src/modules/restaurante/types/index.ts`
- Modify: `nexo-main/src/modules/restaurante/api/restaurante.api.ts`
- Modify: `nexo-main/src/modules/restaurante/hooks/useFoodSettings.ts`
- Modify: `nexo-main/src/modules/restaurante/pages/RestauranteSetupPage.tsx`

- [ ] **Step 15.1: Adicionar rota**

Em `AppRouter.tsx`, dentro do bloco de "Gestão restaurante — management only", adicione:
```tsx
import RecipeCardPage from "@/modules/restaurante/pages/RecipeCardPage";

// Dentro do bloco <Route element={<RoleRoute path="/restaurante/portal" />}>
//   <Route element={<MainAppLayout />}>
<Route path="/produtos/:id/ficha" element={<RecipeCardPage />} />
```

> Esta rota fica dentro do bloco de `ProtectedRoute` + `RoleRoute path="/estoque"` pois é acessada pela gestão. Alternativamente, coloque dentro do bloco `"/dashboard"` (management). Coloque junto ao `/produtos/:id`:

No bloco `<Route element={<RoleRoute path="/estoque" />}>`:
```tsx
<Route path="/produtos/:id/ficha" element={<RecipeCardPage />} />
```

- [ ] **Step 15.2: Adicionar campos ao tipo `FoodServiceSettingsDto`**

Em `nexo-main/src/modules/restaurante/types/index.ts`, adicione ao final de `FoodServiceSettingsDto`:
```typescript
costPerMinuteGas: number;
costPerMinuteLaborRate: number;
```

- [ ] **Step 15.3: Adicionar função de API**

Em `restaurante.api.ts`, adicione:
```typescript
export function updateOperationalCosts(payload: {
  costPerMinuteGas: number;
  costPerMinuteLaborRate: number;
}): Promise<FoodServiceSettingsDto> {
  return apiClient.put<FoodServiceSettingsDto>("/restaurante/settings/costs", payload);
}
```

- [ ] **Step 15.4: Adicionar mutation ao hook**

Em `useFoodSettings.ts`, adicione:
```typescript
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateOperationalCosts } from "../api/restaurante.api";

export function useUpdateOperationalCosts(storeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: updateOperationalCosts,
    onSuccess: (data) => qc.setQueryData(FOOD_SETTINGS_KEY(storeId), data),
  });
}
```

- [ ] **Step 15.5: Adicionar seção na `RestauranteSetupPage`**

Localize o final do JSX da `RestauranteSetupPage` e adicione uma nova seção antes do fechamento:
```tsx
import { useUpdateOperationalCosts } from "../hooks/useFoodSettings";
import { useAuth } from "@/modules/auth/context/AuthContext";

// Dentro do componente:
const { session } = useAuth();
const { data: settings } = useFoodSettings(session?.storeId ?? "");
const costsMut = useUpdateOperationalCosts(session?.storeId ?? "");

const [gasRate,   setGasRate]   = useState("0");
const [laborRate, setLaborRate] = useState("0");

useEffect(() => {
  if (settings) {
    setGasRate(String(settings.costPerMinuteGas));
    setLaborRate(String(settings.costPerMinuteLaborRate));
  }
}, [settings]);

// No JSX (adicionar como novo SectionCard):
<SectionCard className="mt-6">
  <h3 className="text-sm font-semibold mb-1">Custos operacionais</h3>
  <p className="text-xs text-muted-foreground mb-4">
    Usados no cálculo automático de CMV nas fichas técnicas dos pratos.
  </p>
  <div className="grid grid-cols-2 gap-4">
    <div className="space-y-1">
      <Label className="text-xs">Custo por minuto de gás (R$)</Label>
      <Input type="number" step="0.0001" min="0" value={gasRate}
        onChange={e => setGasRate(e.target.value)} />
    </div>
    <div className="space-y-1">
      <Label className="text-xs">Custo por minuto de mão de obra (R$)</Label>
      <Input type="number" step="0.0001" min="0" value={laborRate}
        onChange={e => setLaborRate(e.target.value)} />
    </div>
  </div>
  <Button className="mt-4" size="sm"
    disabled={costsMut.isPending}
    onClick={() => costsMut.mutate({
      costPerMinuteGas: parseFloat(gasRate) || 0,
      costPerMinuteLaborRate: parseFloat(laborRate) || 0,
    })}>
    {costsMut.isPending ? "Salvando…" : "Salvar custos"}
  </Button>
</SectionCard>
```

- [ ] **Step 15.6: Build do frontend**

```bash
cd nexo-main && npm run build
```

Expected: zero erros de TypeScript/build.

- [ ] **Step 15.7: Commit final**

```bash
git add nexo-main/src/
git commit -m "feat(restaurante): wire RecipeCardPage route, operational costs settings section"
```

---

## Verificação Final

- [ ] Backend: `dotnet test tests/Nexo.IntegrationTests` — todos os testes passando (incluindo os novos 2)
- [ ] Frontend dev server: `npm run dev` em `nexo-main` — sem erros no console
- [ ] Fluxo completo manual:
  1. Criar um ingrediente em `/estoque` (ex: "Filé Mignon", kg, custo R$80)
  2. Registrar um preço de compra via seção "Histórico de preços"
  3. Criar um produto do cardápio em `/produtos` (ex: "Filé ao Molho")
  4. Clicar em "Abrir Ficha Técnica" → confirmar navegação para `/produtos/:id/ficha`
  5. Adicionar o ingrediente, criar 2 etapas de preparo, preencher montagem
  6. Verificar CMV bar atualizada
  7. Em `/restaurante/configurar`, adicionar custo de gás e MO e salvar
  8. Recarregar ficha técnica — CMV deve refletir os custos operacionais
