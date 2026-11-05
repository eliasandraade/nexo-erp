using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.MultiTenancy;
using NSubstitute;
using Xunit;

namespace Nexo.UnitTests.Billing;

/// <summary>
/// A decisão dos três portões quando a empresa está sem acesso ao módulo. A integração cobre o caminho pelo HTTP. Aqui ficam os
/// casos que o HTTP não alcança: rota isenta com portão, que hoje não existe, e o cache da empresa atrasado em relação ao do portão.
/// </summary>
public class ExpiredModuleGateTests
{
    private const string Module = "varejo";
    private static readonly IActionResult NeverHad = new ForbidResult();

    private static readonly Func<string, bool> IsVarejo = key => key == Module;

    private static ICurrentTenant Tenant(string[] active, string[] expired)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.ActiveModules.Returns(active);
        tenant.ExpiredModules.Returns(expired);
        return tenant;
    }

    private static DefaultHttpContext Request(string method, string template = "api/sales")
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Method = method;
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, template));
        return context;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public void A_module_the_tenant_never_had_keeps_the_refusal_of_the_gate(string method)
    {
        var result = ExpiredModuleGate.Decide(Request(method), Tenant(["build"], ["service"]), IsVarejo, NeverHad);

        result.Should().BeSameAs(NeverHad);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void An_expired_module_reads(string method)
    {
        ExpiredModuleGate.Decide(Request(method), Tenant([], [Module]), IsVarejo, NeverHad).Should().BeNull();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void An_expired_module_refuses_writes_with_the_standard_error(string method)
    {
        var context = Request(method);
        context.TraceIdentifier = "req-1";

        var result = ExpiredModuleGate.Decide(context, Tenant(["build"], [Module]), IsVarejo, NeverHad);

        var json = result.Should().BeOfType<JsonResult>().Subject;
        json.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        var body = System.Text.Json.JsonSerializer.SerializeToElement(json.Value);
        body.GetProperty("code").GetString().Should().Be(TenantAccessPolicy.ErrorCode);
        body.GetProperty("error").GetString().Should().Contain("somente leitura");
        body.GetProperty("details").GetArrayLength().Should().Be(0);
        body.GetProperty("requestId").GetString().Should().Be("req-1");
    }

    /// <summary>O cache do portão já viu o fim do período, o da empresa ainda não: quem acabou de vencer não vira "nunca teve".</summary>
    [Theory]
    [InlineData("GET", false)]
    [InlineData("POST", true)]
    public void A_module_still_listed_as_active_in_the_tenant_cache_counts_as_expired(string method, bool refused)
    {
        var result = ExpiredModuleGate.Decide(Request(method), Tenant([Module], []), IsVarejo, NeverHad);

        if (refused) result.Should().BeOfType<JsonResult>();
        else result.Should().BeNull();
    }

    [Fact]
    public void A_route_the_policy_exempts_passes_the_gate()
    {
        ExpiredModuleGate.Decide(Request("PUT", "api/users/{id:guid}"), Tenant([], [Module]), IsVarejo, NeverHad).Should().BeNull();
    }

    // ── A regra na entidade ───────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ModuleSubscription Canceled(string module)
    {
        var row = ModuleSubscription.CreateAdminGrant(TenantId, module);
        row.Cancel();
        return row;
    }

    [Fact]
    public void A_row_without_access_is_expired_and_a_module_with_no_row_is_not()
    {
        var pastDueAfterGrace = ModuleSubscription.CreateAdminGrant(TenantId, "pastdue");
        pastDueAfterGrace.MarkPastDue(Now.AddDays(-(ModuleSubscription.PastDueGraceDays + 1)));
        var suspended = ModuleSubscription.CreateAdminGrant(TenantId, "suspended");
        suspended.SyncFromStripe(SubscriptionStatus.Suspended, Now.AddDays(-1), Now.AddDays(20), false);

        var expired = ModuleSubscription.ExpiredKeys(
        [
            ModuleSubscription.CreateTrial(TenantId, "trial", days: -9999),
            ModuleSubscription.CreateAdminGrant(TenantId, "ended", expiresAt: Now.AddDays(-1)),
            pastDueAfterGrace,
            Canceled("canceled"),
            suspended,
        ], Now);

        expired.Should().Equal("canceled", "ended", "pastdue", "suspended", "trial");
    }

    [Fact]
    public void A_row_with_access_is_not_expired()
    {
        var inGrace = ModuleSubscription.CreateAdminGrant(TenantId, "grace");
        inGrace.MarkPastDue(Now.AddDays(-(ModuleSubscription.PastDueGraceDays - 1)));

        ModuleSubscription.ExpiredKeys(
        [
            ModuleSubscription.CreateAdminGrant(TenantId, "no-end"),
            ModuleSubscription.CreateAdminGrant(TenantId, "future", expiresAt: Now.AddDays(1)),
            inGrace,
        ], Now).Should().BeEmpty();
    }

    [Fact]
    public void A_module_with_any_row_with_access_is_not_expired()
    {
        ModuleSubscription.ExpiredKeys([Canceled(Module), ModuleSubscription.CreateAdminGrant(TenantId, Module)], Now).Should().BeEmpty();
    }

    // ── O contexto da requisição ──────────────────────────────────────────────

    [Fact]
    public void A_company_is_read_only_when_resolved_with_no_module_with_access()
    {
        var tenant = new CurrentTenantService();
        tenant.IsReadOnly.Should().BeFalse("sem contexto não há empresa");
        tenant.ExpiredModules.Should().BeEmpty();

        tenant.Set(TenantId, "empresa", [], [Module]);

        tenant.IsReadOnly.Should().BeTrue();
        tenant.ExpiredModules.Should().Equal(Module);
    }

    [Fact]
    public void A_company_with_a_module_with_access_is_not_read_only()
    {
        var tenant = new CurrentTenantService();

        tenant.Set(TenantId, "empresa", ["build"]);

        tenant.IsReadOnly.Should().BeFalse();
        tenant.ExpiredModules.Should().BeEmpty();
    }
}
