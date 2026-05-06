using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Categories;
using Nexo.Application.Features.Customers;
using Nexo.Application.Features.Financial;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Settings;
using Nexo.Application.Features.Stock;
using Nexo.Application.Features.Stores;
using Nexo.Application.Features.Suppliers;
using Nexo.Application.Features.Users;
using Nexo.Application.Modules.Restaurante;
using Nexo.Application.Modules.Varejo;

namespace Nexo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<TenantService>();
        services.AddScoped<SettingsService>();

        // CORE business services
        services.AddScoped<CustomerService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<ProductService>();
        services.AddScoped<ProductPurchasePriceService>();
        services.AddScoped<StockService>();
        services.AddScoped<SaleService>();
        services.AddScoped<CashService>();
        services.AddScoped<FinancialService>();

        // ── Módulo Restaurante ────────────────────────────────────────────────
        services.AddScoped<AreaService>();
        services.AddScoped<TableService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RecipeCardService>();
        services.AddScoped<ModifierGroupService>();
        services.AddScoped<FoodServiceSettingsService>();

        // ── Módulo Varejo ─────────────────────────────────────────────────────
        services.AddScoped<PurchaseService>();
        services.AddScoped<PriceListService>();

        // FluentValidation — scan this assembly for all validators
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
