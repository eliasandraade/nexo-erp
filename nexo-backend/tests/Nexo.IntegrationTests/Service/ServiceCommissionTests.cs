using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Repositories.Modules.Service;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

/// <summary>
/// End-to-end coverage for Service commissions (PR C): the three recognition sources (paid order,
/// appointment without order, package consumption), the rate precedence, idempotency, the
/// cross-source anti-double-count rules, payout closing (incl. concurrent closings) and payout
/// payment (settled Payable in the financeiro, exactly once). Every test uses a fresh professional,
/// so assertions scoped to it are independent of the rest of the shared database.
/// </summary>
[Collection("Integration")]
public class ServiceCommissionTests
{
    private const string Base = "/api/v1/service";
    private const string ForeignTaxId = "66555444000188";

    private readonly TestWebApplicationFactory _factory;
    public ServiceCommissionTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate / authorization ─────────────────────────────────────────────────
    [Fact]
    public async Task Commissions_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync($"{Base}/commissions/entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Closing_and_paying_require_a_manager_role()
    {
        var seller = await LoginAsSellerOfAdminTenantAsync();
        (await seller.GetAsync($"{Base}/commissions/summary")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await seller.PostAsJsonAsync($"{Base}/commissions/payouts", new
        {
            professionalId = Guid.NewGuid(), periodStart = DateTime.UtcNow.AddDays(-1), periodEnd = DateTime.UtcNow,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.PostAsJsonAsync($"{Base}/commissions/payouts/{Guid.NewGuid()}/pay", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Source: order ────────────────────────────────────────────────────────
    [Fact]
    public async Task Unpaid_order_generates_no_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));

        (await EntriesAsync(c, prof)).Should().BeEmpty();
    }

    [Fact]
    public async Task Partially_paid_order_generates_no_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));

        await PayAsync(c, order.Id, 99.99m);

        (await EntriesAsync(c, prof)).Should().BeEmpty("commission is recognised only when the comanda is fully paid");
    }

    [Fact]
    public async Task Fully_paid_order_generates_one_entry_per_item()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var corte = await CatalogAsync(c, 60m, 50m);                 // catalog rate
        var barba = await CatalogAsync(c, 40m, null);                // falls back to the professional
        var order = await OrderAsync(c, await CustomerAsync(c), orderProfessional: prof,
            (corte, null), (barba, null));

        await PayAsync(c, order.Id, 50m);
        (await EntriesAsync(c, prof)).Should().BeEmpty();
        await PayAsync(c, order.Id, 50m);                            // settles 100

        var entries = await EntriesAsync(c, prof);
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.GetProperty("source").GetString() == "OrderItem");
        var byBase = entries.ToDictionary(e => e.GetProperty("baseAmount").GetDecimal());
        byBase[60m].GetProperty("commissionPercent").GetDecimal().Should().Be(50m);
        byBase[60m].GetProperty("commissionAmount").GetDecimal().Should().Be(30m);
        byBase[40m].GetProperty("commissionPercent").GetDecimal().Should().Be(10m);
        byBase[40m].GetProperty("commissionAmount").GetDecimal().Should().Be(4m);
        entries.Select(e => e.GetProperty("sourceId").GetGuid())
            .Should().BeEquivalentTo(order.ItemIds);
    }

    [Fact]
    public async Task Backdated_payment_is_recognized_at_server_time()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var before = DateTime.UtcNow.AddSeconds(-5);
        await PaidOrderAsync(c, await CustomerAsync(c), prof, 100m, paidAt: DateTime.UtcNow.AddDays(-40));

        (await EntriesAsync(c, prof)).Single().GetProperty("recognizedAt").GetDateTime().ToUniversalTime()
            .Should().BeAfter(before, "a backdated payment must not land in an already-closed period");
    }

    [Fact]
    public async Task Item_professional_takes_precedence_over_the_order_professional()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var orderProf = await ProfessionalAsync(c, 10m);
        var itemProf  = await ProfessionalAsync(c, 20m);
        var order = await OrderAsync(c, await CustomerAsync(c), orderProf, (await CatalogAsync(c, 100m, null), itemProf));

        await PayAsync(c, order.Id, 100m);

        (await EntriesAsync(c, orderProf)).Should().BeEmpty();
        var e = (await EntriesAsync(c, itemProf)).Single();
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Resettling_an_order_does_not_duplicate_its_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 30m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));

        var payment = await PayAsync(c, order.Id, 100m);             // 1st recognition
        (await c.PostAsJsonAsync($"{Base}/payments/{payment}/void", new { reason = "lançado errado" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await PayAsync(c, order.Id, 100m);                            // settles again → earns again

        var entries = await EntriesAsync(c, prof);
        entries.Count(IsActiveEarning).Should().Be(1, "only one commission counts for the item");
        entries.Count(e => e.GetProperty("reversedAt").ValueKind != JsonValueKind.Null).Should().Be(1);
        (await SummaryAsync(c, prof)).GetProperty("openAmount").GetDecimal().Should().Be(30m);
    }

    // ── Reversal (voided payments) ───────────────────────────────────────────
    [Fact]
    public async Task Voiding_a_payment_reverses_the_open_commission_and_unfreezes_the_item()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 30m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        var payment = await PayAsync(c, order.Id, 100m);

        (await c.PostAsJsonAsync($"{Base}/payments/{payment}/void", new { reason = "estorno" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("reversedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await SummaryAsync(c, prof)).GetProperty("openAmount").GetDecimal().Should().Be(0m);
        (await ClosePayoutAsync(c, prof, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a reversed commission is never paid out");
        (await c.PutAsJsonAsync($"{Base}/orders/{order.Id}/items/{order.ItemIds.Single()}", new { quantity = 1m, professionalId = prof }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the item is no longer commissioned");
    }

    [Fact]
    public async Task Voiding_one_of_two_payments_keeps_nothing_and_a_partial_void_of_an_unsettled_order_is_harmless()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        var p1 = await PayAsync(c, order.Id, 40m);
        (await c.PostAsJsonAsync($"{Base}/payments/{p1}/void", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EntriesAsync(c, prof)).Should().BeEmpty();

        await PayAsync(c, order.Id, 60m);
        var p3 = await PayAsync(c, order.Id, 40m);                   // settles
        (await EntriesAsync(c, prof)).Count(IsActiveEarning).Should().Be(1);
        (await c.PostAsJsonAsync($"{Base}/payments/{p3}/void", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EntriesAsync(c, prof)).Count(IsActiveEarning).Should().Be(0);
    }

    [Fact]
    public async Task Voiding_after_the_commission_was_paid_out_discounts_it_from_the_next_payout()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var customer = await CustomerAsync(c);
        var order = await OrderAsync(c, customer, prof, (await CatalogAsync(c, 100m, null), null));
        var payment = await PayAsync(c, order.Id, 100m);              // +10
        var payoutId = await ClosedPayoutIdAsync(c, prof);
        (await c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await c.PostAsJsonAsync($"{Base}/payments/{payment}/void", new { reason = "chargeback" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var reversal = (await EntriesAsync(c, prof, "open")).Single();
        reversal.GetProperty("kind").GetString().Should().Be("Reversal");
        reversal.GetProperty("commissionAmount").GetDecimal().Should().Be(-10m);
        (await ClosePayoutAsync(c, prof, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a period that only owes back cannot be paid");

        await PaidOrderAsync(c, customer, prof, 300m);                // +30
        var next = await (await ClosePayoutAsync(c, prof, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1)))
            .Content.ReadFromJsonAsync<JsonElement>();
        next.GetProperty("payout").GetProperty("totalAmount").GetDecimal().Should().Be(20m, "30 earned − 10 discounted");
        next.GetProperty("payout").GetProperty("entryCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Voiding_the_same_payment_twice_reverses_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        var payment = await PayAsync(c, order.Id, 100m);
        var payoutId = await ClosedPayoutIdAsync(c, prof);

        var c2 = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var results = await Task.WhenAll(
            c.PostAsJsonAsync($"{Base}/payments/{payment}/void", new { reason = "a" }),
            c2.PostAsJsonAsync($"{Base}/payments/{payment}/void", new { reason = "b" }));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await EntriesAsync(c, prof)).Count(e => e.GetProperty("kind").GetString() == "Reversal").Should().Be(1);
        payoutId.Should().NotBeEmpty();
    }

    // ── Financial module guards ──────────────────────────────────────────────
    [Fact]
    public async Task Service_generated_lancamentos_cannot_be_changed_or_forged_in_the_financial_module()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        var payment = await PayAsync(c, order.Id, 100m);
        var txId = (await ServiceTransactionAsync("SvcPayment", payment)).Id;

        (await c.PostAsync($"/api/financial/transactions/{txId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ServiceTransactionAsync("SvcPayment", payment)).Status.Should().Be(TransactionStatus.Paid);

        var accounts = await c.GetFromJsonAsync<JsonElement[]>("/api/financial/accounts");
        var payableAccount = accounts!.First(a => a.GetProperty("accountType").GetString() == "Payable").GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync("/api/financial/transactions", new
        {
            financialAccountId = payableAccount, transactionType = "Payable", amount = 10m, description = "forjado",
            dueDate = DateTime.UtcNow, referenceType = "SvcCommissionPayout", referenceId = Guid.NewGuid(),
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static bool IsActiveEarning(JsonElement e)
        => e.GetProperty("kind").GetString() == "Earning" && e.GetProperty("reversedAt").ValueKind == JsonValueKind.Null;

    private static async Task<JsonElement> SummaryAsync(HttpClient c, Guid prof)
        => (await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/summary?professionalId={prof}"))!.Single();

    private async Task<FinancialTransaction> ServiceTransactionAsync(string referenceType, Guid referenceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId);
    }

    [Fact]
    public async Task Commissioned_order_item_can_no_longer_be_changed_or_removed()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 30m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        await PayAsync(c, order.Id, 100m);
        var itemId = order.ItemIds.Single();

        (await c.PutAsJsonAsync($"{Base}/orders/{order.Id}/items/{itemId}", new { quantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.DeleteAsync($"{Base}/orders/{order.Id}/items/{itemId}"))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Naming_the_professional_after_full_payment_recognizes_the_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 25m);
        var order = await OrderAsync(c, await CustomerAsync(c), orderProfessional: null, (await CatalogAsync(c, 80m, null), null));
        await PayAsync(c, order.Id, 80m);
        (await EntriesAsync(c, prof)).Should().BeEmpty("nobody to pay yet");

        (await c.PutAsJsonAsync($"{Base}/orders/{order.Id}", new { professionalId = prof }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await EntriesAsync(c, prof)).Should().ContainSingle()
            .Which.GetProperty("commissionAmount").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Removing_an_unpaid_item_that_settles_the_order_recognizes_the_rest()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof,
            (await CatalogAsync(c, 60m, null), null), (await CatalogAsync(c, 40m, null), null));
        await PayAsync(c, order.Id, 60m);
        (await EntriesAsync(c, prof)).Should().BeEmpty();

        (await c.DeleteAsync($"{Base}/orders/{order.Id}/items/{order.ItemIds[1]}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("sourceId").GetGuid().Should().Be(order.ItemIds[0]);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(30m);
    }

    // ── Rate precedence ──────────────────────────────────────────────────────
    [Fact]
    public async Task Catalog_rate_is_used_when_the_service_has_one()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 200m, 35m), null));
        await PayAsync(c, order.Id, 200m);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("commissionPercent").GetDecimal().Should().Be(35m);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(70m);
    }

    [Fact]
    public async Task Professional_default_rate_is_used_when_the_service_has_none()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 12.5m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 80m, null), null));
        await PayAsync(c, order.Id, 80m);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("commissionPercent").GetDecimal().Should().Be(12.5m);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(10m);
    }

    [Fact]
    public async Task No_rate_anywhere_creates_no_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, null);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 80m, null), null));
        await PayAsync(c, order.Id, 80m);

        (await EntriesAsync(c, prof)).Should().BeEmpty();
    }

    [Fact]
    public async Task Rate_changes_after_recognition_do_not_rewrite_history()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        await PayAsync(c, order.Id, 100m);

        (await c.PutAsJsonAsync($"{Base}/professionals/{prof}", new { name = "Renamed", defaultCommissionPercent = 90m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await EntriesAsync(c, prof)).Single().GetProperty("commissionAmount").GetDecimal().Should().Be(10m);
    }

    // ── Source: appointment ──────────────────────────────────────────────────
    [Fact]
    public async Task Completed_appointment_without_order_generates_commission_on_its_price_snapshot()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var appt = await AppointmentAsync(c, await CustomerAsync(c), prof, await CatalogAsync(c, 75m, null));

        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress");
        (await EntriesAsync(c, prof)).Should().BeEmpty("only completion recognises");
        await ProgressAppointmentAsync(c, appt, "Completed");

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("source").GetString().Should().Be("Appointment");
        e.GetProperty("sourceId").GetGuid().Should().Be(appt);
        e.GetProperty("baseAmount").GetDecimal().Should().Be(75m);
        e.GetProperty("commissionPercent").GetDecimal().Should().Be(40m);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(30m);
    }

    [Fact]
    public async Task Appointment_rate_is_snapshotted_at_booking()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var appt = await AppointmentAsync(c, await CustomerAsync(c), prof, await CatalogAsync(c, 100m, null));

        (await c.PutAsJsonAsync($"{Base}/professionals/{prof}", new { name = "Changed", defaultCommissionPercent = 5m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");

        (await EntriesAsync(c, prof)).Single().GetProperty("commissionPercent").GetDecimal().Should().Be(40m);
    }

    [Fact]
    public async Task Appointment_with_a_linked_order_is_commissioned_only_through_the_order()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var appt = await AppointmentAsync(c, await CustomerAsync(c), prof, await CatalogAsync(c, 60m, null));

        var order = await OrderFromAppointmentAsync(c, appt);
        await PayAsync(c, order, 60m);
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");

        var entries = await EntriesAsync(c, prof);
        entries.Should().ContainSingle();
        entries[0].GetProperty("source").GetString().Should().Be("OrderItem");
    }

    [Fact]
    public async Task Order_opened_after_completion_does_not_commission_the_appointment_service_again()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var appt = await AppointmentAsync(c, await CustomerAsync(c), prof, await CatalogAsync(c, 60m, null));
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");   // appointment entry

        var order = await OrderFromAppointmentAsync(c, appt);
        var extra = await CatalogAsync(c, 20m, null);
        (await c.PostAsJsonAsync($"{Base}/orders/{order}/items", new { catalogItemId = extra, quantity = 1m, professionalId = prof }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await PayAsync(c, order, 80m);

        var entries = await EntriesAsync(c, prof);
        entries.Should().HaveCount(2, "the appointment service once + the extra item");
        entries.Count(e => e.GetProperty("source").GetString() == "Appointment").Should().Be(1);
        entries.Single(e => e.GetProperty("source").GetString() == "OrderItem")
            .GetProperty("baseAmount").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Completing_an_appointment_twice_does_not_duplicate_its_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var appt = await AppointmentAsync(c, await CustomerAsync(c), prof, await CatalogAsync(c, 50m, null));
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");

        (await c.PatchAsJsonAsync($"{Base}/appointments/{appt}/status", new { status = "Completed" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await EntriesAsync(c, prof)).Should().ContainSingle();
    }

    // ── Source: package consumption ──────────────────────────────────────────
    [Fact]
    public async Task Package_consumption_generates_commission_and_stores_the_snapshots()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var catalog = await CatalogAsync(c, 50m, 20m);
        var cp = await CustomerPackageAsync(c, await CustomerAsync(c), price: 200m, (catalog, 4m));

        var consumed = await ConsumeAsync(c, cp, catalog, 1m, prof);

        var usage = consumed.GetProperty("usages").EnumerateArray().Single();
        usage.GetProperty("professionalId").GetGuid().Should().Be(prof);
        usage.GetProperty("baseAmountSnapshot").GetDecimal().Should().Be(50m);     // 200 / 4 units
        usage.GetProperty("commissionPercentSnapshot").GetDecimal().Should().Be(20m);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("source").GetString().Should().Be("PackageUsage");
        e.GetProperty("sourceId").GetGuid().Should().Be(usage.GetProperty("id").GetGuid());
        e.GetProperty("baseAmount").GetDecimal().Should().Be(50m);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(10m);
    }

    [Fact]
    public async Task Package_value_is_prorated_over_every_unit_the_package_includes()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var corte = await CatalogAsync(c, 60m, null);
        var barba = await CatalogAsync(c, 30m, null);
        var cp = await CustomerPackageAsync(c, await CustomerAsync(c), price: 300m, (corte, 4m), (barba, 2m));

        await ConsumeAsync(c, cp, barba, 2m, prof);                    // 2 of 6 units → 100

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("baseAmount").GetDecimal().Should().Be(100m);
        e.GetProperty("commissionAmount").GetDecimal().Should().Be(50m);
    }

    [Fact]
    public async Task Package_consumption_without_a_professional_generates_no_commission()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var catalog = await CatalogAsync(c, 50m, 20m);
        var cp = await CustomerPackageAsync(c, await CustomerAsync(c), price: 200m, (catalog, 4m));

        var consumed = await ConsumeAsync(c, cp, catalog, 1m, professionalId: null);

        var usage = consumed.GetProperty("usages").EnumerateArray().Single();
        usage.GetProperty("professionalId").ValueKind.Should().Be(JsonValueKind.Null);
        usage.GetProperty("commissionPercentSnapshot").ValueKind.Should().Be(JsonValueKind.Null);
        (await CountEntriesForSourceAsync(SvcCommissionSource.PackageUsage, usage.GetProperty("id").GetGuid()))
            .Should().Be(0);
    }

    [Fact]
    public async Task Package_consumption_with_an_unknown_professional_returns_404()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var catalog = await CatalogAsync(c, 50m, 20m);
        var cp = await CustomerPackageAsync(c, await CustomerAsync(c), price: 200m, (catalog, 4m));

        (await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
                new { catalogItemId = catalog, quantity = 1m, professionalId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Order_item_covered_by_a_package_is_commissioned_once_through_the_package()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var customer = await CustomerAsync(c);
        var catalog = await CatalogAsync(c, 40m, null);
        var cp = await CustomerPackageAsync(c, customer, price: 120m, (catalog, 3m));
        var order = await OrderAsync(c, customer, prof, (catalog, null));

        // Consumption inherits the professional from the order item it points at.
        (await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
                new { catalogItemId = catalog, quantity = 1m, orderId = order.Id, orderItemId = order.ItemIds.Single() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await PayAsync(c, order.Id, 40m);

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("source").GetString().Should().Be("PackageUsage");
        e.GetProperty("baseAmount").GetDecimal().Should().Be(40m);    // 120 / 3
    }

    [Fact]
    public async Task Appointment_completed_then_paid_with_a_package_is_commissioned_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var customer = await CustomerAsync(c);
        var catalog = await CatalogAsync(c, 60m, null);
        var cp = await CustomerPackageAsync(c, customer, price: 160m, (catalog, 4m));
        var appt = await AppointmentAsync(c, customer, prof, catalog);
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");   // appointment entry

        var r = await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
            new { catalogItemId = catalog, quantity = 1m, appointmentId = appt });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usages").EnumerateArray().Single();
        usage.GetProperty("appointmentId").GetGuid().Should().Be(appt);
        usage.GetProperty("professionalId").GetGuid().Should().Be(prof, "inherited from the appointment");

        (await EntriesAsync(c, prof)).Should().ContainSingle()
            .Which.GetProperty("source").GetString().Should().Be("Appointment");
    }

    [Fact]
    public async Task Package_consumed_for_an_appointment_before_completion_is_commissioned_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var customer = await CustomerAsync(c);
        var catalog = await CatalogAsync(c, 60m, null);
        var cp = await CustomerPackageAsync(c, customer, price: 160m, (catalog, 4m));
        var appt = await AppointmentAsync(c, customer, prof, catalog);

        (await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
            new { catalogItemId = catalog, quantity = 1m, appointmentId = appt })).StatusCode.Should().Be(HttpStatusCode.OK);
        await ProgressAppointmentAsync(c, appt, "Confirmed", "InProgress", "Completed");

        var e = (await EntriesAsync(c, prof)).Single();
        e.GetProperty("source").GetString().Should().Be("PackageUsage");
        e.GetProperty("baseAmount").GetDecimal().Should().Be(40m);   // 160 / 4
    }

    [Fact]
    public async Task Package_consumption_linked_to_an_unrelated_appointment_is_rejected()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 40m);
        var catalog = await CatalogAsync(c, 60m, null);
        var cp = await CustomerPackageAsync(c, await CustomerAsync(c), price: 160m, (catalog, 4m));
        var otherCustomersAppt = await AppointmentAsync(c, await CustomerAsync(c), prof, catalog);

        (await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
                new { catalogItemId = catalog, quantity = 1m, appointmentId = otherCustomersAppt }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
                new { catalogItemId = catalog, quantity = 1m, appointmentId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Order_link_without_an_item_still_covers_the_matching_order_item()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 50m);
        var customer = await CustomerAsync(c);
        var catalog = await CatalogAsync(c, 40m, null);
        var cp = await CustomerPackageAsync(c, customer, price: 120m, (catalog, 3m));
        var order = await OrderAsync(c, customer, prof, (catalog, null));

        var r = await c.PostAsJsonAsync($"{Base}/customer-packages/{cp}/consume",
            new { catalogItemId = catalog, quantity = 1m, orderId = order.Id });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usages").EnumerateArray().Single()
            .GetProperty("orderItemId").GetGuid().Should().Be(order.ItemIds.Single());
        await PayAsync(c, order.Id, 40m);

        (await EntriesAsync(c, prof)).Should().ContainSingle()
            .Which.GetProperty("source").GetString().Should().Be("PackageUsage");
    }

    [Fact]
    public async Task Concurrent_payments_that_together_settle_the_order_still_recognize_it()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c2 = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));

        var pay = (HttpClient client) => client.PostAsJsonAsync($"{Base}/payments",
            new { orderId = order.Id, amount = 50m, method = "Pix", paidAt = DateTime.UtcNow });
        var results = await Task.WhenAll(pay(c), pay(c2));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await EntriesAsync(c, prof)).Should().ContainSingle()
            .Which.GetProperty("commissionAmount").GetDecimal().Should().Be(10m);
    }

    // ── Idempotency at the database ──────────────────────────────────────────
    [Fact]
    public async Task Recognising_the_same_source_twice_keeps_a_single_entry()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 30m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        await PayAsync(c, order.Id, 100m);
        var existing = await FindEntryAsync(SvcCommissionSource.OrderItem, order.ItemIds.Single());

        // Replay of the trigger (e.g. a retried request on another replica) through the repository.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var replay = SvcCommissionEntry.Create(existing.TenantId, prof, existing.CustomerId,
            SvcCommissionSource.OrderItem, existing.SourceId, 100m, 30m, DateTime.UtcNow);
        db.SvcCommissionEntries.Add(replay);
        db.Entry(replay).Property("StoreId").CurrentValue = existing.StoreId;

        var added = await new SvcCommissionRepository(db).TryAddEntryAsync(replay);

        added.Should().BeFalse();
        (await CountEntriesForSourceAsync(SvcCommissionSource.OrderItem, existing.SourceId)).Should().Be(1);
    }

    [Fact]
    public async Task A_duplicate_inside_a_transaction_does_not_poison_the_rest_of_it()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 30m);
        var order = await OrderAsync(c, await CustomerAsync(c), prof, (await CatalogAsync(c, 100m, null), null));
        await PayAsync(c, order.Id, 100m);
        var existing = await FindEntryAsync(SvcCommissionSource.OrderItem, order.ItemIds.Single());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var repo = new SvcCommissionRepository(db);
        await using var tx = await db.Database.BeginTransactionAsync();

        var duplicate = SvcCommissionEntry.Create(existing.TenantId, prof, existing.CustomerId,
            SvcCommissionSource.OrderItem, existing.SourceId, 100m, 30m, DateTime.UtcNow);
        db.SvcCommissionEntries.Add(duplicate);
        db.Entry(duplicate).Property("StoreId").CurrentValue = existing.StoreId;
        (await repo.TryAddEntryAsync(duplicate)).Should().BeFalse();

        // Same transaction keeps working after the unique violation (EF rolled back to its savepoint).
        var fresh = SvcCommissionEntry.Create(existing.TenantId, prof, existing.CustomerId,
            SvcCommissionSource.Appointment, Guid.NewGuid(), 10m, 30m, DateTime.UtcNow);
        db.SvcCommissionEntries.Add(fresh);
        db.Entry(fresh).Property("StoreId").CurrentValue = existing.StoreId;
        (await repo.TryAddEntryAsync(fresh)).Should().BeTrue();
        await tx.CommitAsync();

        (await CountEntriesForSourceAsync(SvcCommissionSource.Appointment, fresh.SourceId)).Should().Be(1);
        (await CountEntriesForSourceAsync(SvcCommissionSource.OrderItem, existing.SourceId)).Should().Be(1);
    }

    // ── Payout: closing ──────────────────────────────────────────────────────
    [Fact]
    public async Task Closing_takes_only_the_open_entries_of_the_professional_in_the_period()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof  = await ProfessionalAsync(c, 10m);
        var other = await ProfessionalAsync(c, 10m);
        var customer = await CustomerAsync(c);
        await PaidOrderAsync(c, customer, prof, 100m);                 // 10
        await PaidOrderAsync(c, customer, prof, 250m);                 // 25
        await PaidOrderAsync(c, customer, other, 500m);                // other professional
        var old = await PaidOrderAsync(c, customer, prof, 1000m);
        await AgeEntryAsync(old.ItemIds.Single(), days: 40);           // recognised last month → outside period

        var resp = await ClosePayoutAsync(c, prof, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddMinutes(1));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var payout = detail.GetProperty("payout");
        payout.GetProperty("status").GetString().Should().Be("Pending");
        payout.GetProperty("totalAmount").GetDecimal().Should().Be(35m);
        payout.GetProperty("entryCount").GetInt32().Should().Be(2);
        detail.GetProperty("entries").GetArrayLength().Should().Be(2);

        var open = await EntriesAsync(c, prof, "open");
        open.Should().ContainSingle().Which.GetProperty("sourceId").GetGuid().Should().Be(old.ItemIds.Single());
        (await EntriesAsync(c, other, "open")).Should().ContainSingle();
    }

    [Fact]
    public async Task Closed_entries_never_enter_another_payout()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        await PaidOrderAsync(c, await CustomerAsync(c), prof, 100m);
        var from = DateTime.UtcNow.AddDays(-1);

        (await ClosePayoutAsync(c, prof, from, DateTime.UtcNow.AddMinutes(1))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ClosePayoutAsync(c, prof, from, DateTime.UtcNow.AddMinutes(1)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "nothing is left open to close");

        await PaidOrderAsync(c, await CustomerAsync(c), prof, 300m);  // new open entry
        var second = await (await ClosePayoutAsync(c, prof, from, DateTime.UtcNow.AddMinutes(1)))
            .Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("payout").GetProperty("totalAmount").GetDecimal().Should().Be(30m);
        second.GetProperty("payout").GetProperty("entryCount").GetInt32().Should().Be(1);

        var payouts = await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/payouts?professionalId={prof}");
        payouts!.Should().HaveCount(2);
        payouts.Sum(p => p.GetProperty("totalAmount").GetDecimal()).Should().Be(40m);
    }

    [Fact]
    public async Task Concurrent_closings_never_capture_the_same_entries_twice()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var customer = await CustomerAsync(c);
        for (var i = 0; i < 3; i++) await PaidOrderAsync(c, customer, prof, 100m);
        var from = DateTime.UtcNow.AddDays(-1);
        var to   = DateTime.UtcNow.AddMinutes(1);

        var c2 = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var results = await Task.WhenAll(
            ClosePayoutAsync(c, prof, from, to), ClosePayoutAsync(c2, prof, from, to),
            ClosePayoutAsync(c, prof, from, to));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode)
            .Should().OnlyContain(s => s == HttpStatusCode.Conflict || s == HttpStatusCode.UnprocessableEntity);

        var payouts = await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/payouts?professionalId={prof}");
        payouts!.Should().ContainSingle().Which.GetProperty("totalAmount").GetDecimal().Should().Be(30m);
        var settled = await EntriesAsync(c, prof, "settled");
        settled.Should().HaveCount(3);
        settled.Select(e => e.GetProperty("payoutId").GetGuid()).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Closing_validates_the_request()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync($"{Base}/commissions/payouts", new
        {
            professionalId = Guid.NewGuid(), periodStart = DateTime.UtcNow, periodEnd = DateTime.UtcNow.AddDays(-1),
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClosePayoutAsync(c, Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Payout: payment ──────────────────────────────────────────────────────
    [Fact]
    public async Task Paying_a_payout_posts_a_settled_payable_exactly_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 20m);
        await PaidOrderAsync(c, await CustomerAsync(c), prof, 150m);
        var payoutId = await ClosedPayoutIdAsync(c, prof);

        (await CountTransactionsAsync(payoutId)).Should().Be(0, "closing never touches the financeiro");

        var paidAt = DateTime.UtcNow.AddMinutes(-5);
        var first = await c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { paidAt });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await first.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Paid");

        var second = await c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { });
        second.StatusCode.Should().Be(HttpStatusCode.OK, "a repeated request is an idempotent no-op");

        var txs = await TransactionsAsync(payoutId);
        txs.Should().ContainSingle();
        txs[0].TransactionType.Should().Be(TransactionType.Payable);
        txs[0].Status.Should().Be(TransactionStatus.Paid);
        txs[0].Amount.Should().Be(30m);
        txs[0].ReferenceType.Should().Be("SvcCommissionPayout");
    }

    [Fact]
    public async Task Concurrent_payments_of_a_payout_post_a_single_expense()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 20m);
        await PaidOrderAsync(c, await CustomerAsync(c), prof, 100m);
        var payoutId = await ClosedPayoutIdAsync(c, prof);
        var c2 = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var results = await Task.WhenAll(
            c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { }),
            c2.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { }),
            c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { }));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await CountTransactionsAsync(payoutId)).Should().Be(1);
    }

    [Fact]
    public async Task Paying_validates_the_request()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.PostAsJsonAsync($"{Base}/commissions/payouts/{Guid.NewGuid()}/pay", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var prof = await ProfessionalAsync(c, 20m);
        await PaidOrderAsync(c, await CustomerAsync(c), prof, 100m);
        var payoutId = await ClosedPayoutIdAsync(c, prof);
        (await c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { paidAt = DateTime.UtcNow.AddDays(3) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_reports_open_pending_and_paid_amounts()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var prof = await ProfessionalAsync(c, 10m);
        var customer = await CustomerAsync(c);
        await PaidOrderAsync(c, customer, prof, 100m);
        var paid = await ClosedPayoutIdAsync(c, prof);
        await c.PostAsJsonAsync($"{Base}/commissions/payouts/{paid}/pay", new { });
        await PaidOrderAsync(c, customer, prof, 200m);
        await ClosedPayoutIdAsync(c, prof);
        await PaidOrderAsync(c, customer, prof, 400m);

        var summary = (await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/summary?professionalId={prof}"))!.Single();
        summary.GetProperty("openAmount").GetDecimal().Should().Be(40m);
        summary.GetProperty("openCount").GetInt32().Should().Be(1);
        summary.GetProperty("pendingPayoutAmount").GetDecimal().Should().Be(20m);
        summary.GetProperty("paidPayoutAmount").GetDecimal().Should().Be(10m);
    }

    // ── Multi-tenancy ────────────────────────────────────────────────────────
    [Fact]
    public async Task Commissions_of_another_tenant_are_invisible()
    {
        var (entryId, payoutId, professionalId) = await SeedForeignCommissionAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);

        (await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/entries?professionalId={professionalId}"))!
            .Should().BeEmpty();
        (await c.GetFromJsonAsync<JsonElement[]>($"{Base}/commissions/entries"))!
            .Should().NotContain(e => e.GetProperty("id").GetGuid() == entryId);
        (await c.GetAsync($"{Base}/commissions/payouts/{payoutId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"{Base}/commissions/payouts/{payoutId}/pay", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ClosePayoutAsync(c, professionalId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountTransactionsAsync(payoutId)).Should().Be(0);
    }

    // ── Helpers: API ─────────────────────────────────────────────────────────
    private sealed record OrderRef(Guid Id, IReadOnlyList<Guid> ItemIds);

    private static async Task<Guid> CustomerAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/customers", new
        {
            personType = "Individual", name = "Cli " + Guid.NewGuid().ToString("N")[..8],
            documentType = "CPF", documentNumber = Guid.NewGuid().ToString("N")[..11],
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ProfessionalAsync(HttpClient c, decimal? defaultPercent)
    {
        var r = await c.PostAsJsonAsync($"{Base}/professionals",
            new { name = "Prof " + Guid.NewGuid().ToString("N")[..6], defaultCommissionPercent = defaultPercent });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CatalogAsync(HttpClient c, decimal price, decimal? commissionPercent)
    {
        var r = await c.PostAsJsonAsync($"{Base}/catalog", new
        {
            name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 30, price,
            commissionPercent, requiresSubject = false,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<OrderRef> OrderAsync(
        HttpClient c, Guid customerId, Guid? orderProfessional, params (Guid CatalogId, Guid? ItemProfessional)[] items)
    {
        var ord = await c.PostAsJsonAsync($"{Base}/orders", new { customerId, professionalId = orderProfessional });
        ord.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ord.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        JsonElement last = default;
        foreach (var (catalogId, itemProf) in items)
        {
            var r = await c.PostAsJsonAsync($"{Base}/orders/{orderId}/items",
                new { catalogItemId = catalogId, quantity = 1m, professionalId = itemProf });
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            last = await r.Content.ReadFromJsonAsync<JsonElement>();
        }
        var itemIds = items.Length == 0 ? [] : last.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        return new OrderRef(orderId, itemIds);
    }

    /// <summary>One-item order for the professional, paid in full (one entry = 10% × price when the default is 10%).</summary>
    private static async Task<OrderRef> PaidOrderAsync(HttpClient c, Guid customerId, Guid prof, decimal price, DateTime? paidAt = null)
    {
        var order = await OrderAsync(c, customerId, prof, (await CatalogAsync(c, price, null), null));
        await PayAsync(c, order.Id, price, paidAt);
        return order;
    }

    private static async Task<Guid> PayAsync(HttpClient c, Guid orderId, decimal amount, DateTime? paidAt = null)
    {
        var r = await c.PostAsJsonAsync($"{Base}/payments",
            new { orderId, amount, method = "Pix", paidAt = paidAt ?? DateTime.UtcNow });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AppointmentAsync(HttpClient c, Guid customerId, Guid prof, Guid catalogId)
    {
        var start = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 500_000) * 30);
        var r = await c.PostAsJsonAsync($"{Base}/appointments", new
        {
            customerId, professionalId = prof, catalogItemId = catalogId, startsAt = start, endsAt = start.AddMinutes(30),
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task ProgressAppointmentAsync(HttpClient c, Guid appointmentId, params string[] statuses)
    {
        foreach (var status in statuses)
            (await c.PatchAsJsonAsync($"{Base}/appointments/{appointmentId}/status", new { status }))
                .StatusCode.Should().Be(HttpStatusCode.OK, $"transition to {status}");
    }

    private static async Task<Guid> OrderFromAppointmentAsync(HttpClient c, Guid appointmentId)
    {
        var r = await c.PostAsync($"{Base}/orders/from-appointment/{appointmentId}", null);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CustomerPackageAsync(
        HttpClient c, Guid customerId, decimal price, params (Guid CatalogId, decimal Quantity)[] items)
    {
        var pkg = await c.PostAsJsonAsync($"{Base}/packages",
            new { name = "Pkg " + Guid.NewGuid().ToString("N")[..6], price, validityDays = 30 });
        pkg.StatusCode.Should().Be(HttpStatusCode.Created);
        var packageId = (await pkg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var (catalogId, qty) in items)
            (await c.PostAsJsonAsync($"{Base}/packages/{packageId}/items",
                new { catalogItemId = catalogId, includedQuantity = qty })).StatusCode.Should().Be(HttpStatusCode.OK);
        var assign = await c.PostAsJsonAsync($"{Base}/customer-packages",
            new { packageId, customerId, startsAt = DateTime.UtcNow.AddMinutes(-1) });
        assign.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await assign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ConsumeAsync(HttpClient c, Guid cpId, Guid catalogId, decimal quantity, Guid? professionalId)
    {
        var r = await c.PostAsJsonAsync($"{Base}/customer-packages/{cpId}/consume",
            new { catalogItemId = catalogId, quantity, professionalId });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> EntriesAsync(HttpClient c, Guid professionalId, string? status = null)
    {
        var url = $"{Base}/commissions/entries?professionalId={professionalId}" + (status is null ? "" : $"&status={status}");
        return (await c.GetFromJsonAsync<JsonElement[]>(url))!.ToList();
    }

    private static Task<HttpResponseMessage> ClosePayoutAsync(HttpClient c, Guid prof, DateTime from, DateTime to)
        => c.PostAsJsonAsync($"{Base}/commissions/payouts", new { professionalId = prof, periodStart = from, periodEnd = to });

    private static async Task<Guid> ClosedPayoutIdAsync(HttpClient c, Guid prof)
    {
        var r = await ClosePayoutAsync(c, prof, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("payout").GetProperty("id").GetGuid();
    }

    // ── Helpers: database ────────────────────────────────────────────────────
    private async Task<SvcCommissionEntry> FindEntryAsync(SvcCommissionSource source, Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.SvcCommissionEntries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.Source == source && e.SourceId == sourceId);
    }

    /// <summary>Moves an entry's recognition date into the past (an entry from an earlier period).</summary>
    private async Task AgeEntryAsync(Guid sourceId, int days)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var when = DateTime.UtcNow.AddDays(-days);
        await db.SvcCommissionEntries.IgnoreQueryFilters().Where(e => e.SourceId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RecognizedAt, when));
    }

    private async Task<int> CountEntriesForSourceAsync(SvcCommissionSource source, Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.SvcCommissionEntries.IgnoreQueryFilters().CountAsync(e => e.Source == source && e.SourceId == sourceId);
    }

    private async Task<List<FinancialTransaction>> TransactionsAsync(Guid payoutId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ReferenceType == "SvcCommissionPayout" && t.ReferenceId == payoutId).ToListAsync();
    }

    private async Task<int> CountTransactionsAsync(Guid payoutId) => (await TransactionsAsync(payoutId)).Count;

    /// <summary>A Vendedor in the admin's tenant — has the service module but no manager role.</summary>
    private async Task<HttpClient> LoginAsSellerOfAdminTenantAsync()
    {
        const string login = "svc.commission.seller";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: "Commission Seller",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Vendedor));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
    }

    private async Task<(Guid EntryId, Guid PayoutId, Guid ProfessionalId)> SeedForeignCommissionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Commission Isolation Corp", ForeignTaxId, "admin@comm-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "CIC Store", "comm-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var customer = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
        db.Customers.Add(customer);
        var prof = SvcProfessional.Create(t.Id, "Foreign Prof", defaultCommissionPercent: 10m);
        db.SvcProfessionals.Add(prof);
        db.Entry(prof).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();

        var entry = SvcCommissionEntry.Create(t.Id, prof.Id, customer.Id, SvcCommissionSource.Appointment,
            Guid.NewGuid(), 100m, 10m, DateTime.UtcNow);
        db.SvcCommissionEntries.Add(entry);
        db.Entry(entry).Property("StoreId").CurrentValue = storeId;
        var payout = SvcCommissionPayout.Create(t.Id, prof.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 10m, 1);
        db.SvcCommissionPayouts.Add(payout);
        db.Entry(payout).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return (entry.Id, payout.Id, prof.Id);
    }
}
