using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>Wave 2 (Comercial): pure rules of budgets, templates, change orders, selections, materials, measurements.</summary>
public class BuildCommercialTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 30);

    private static BuildBudget Budget(decimal margin = 0m) => BuildBudget.Create(Tenant, User, "Casa térrea", null, margin);

    private static BuildBudgetItem Item(BuildBudget b, string name, BuildCostKind kind, decimal qty, decimal unitCost,
        string? stageName = null)
        => BuildBudgetItem.Create(Tenant, b.Id, name, "Categoria", qty, "un", unitCost, null, kind, stageName);

    // ── Templates: scaling ────────────────────────────────────────────────────

    [Fact]
    public void Template_item_per_square_meter_scales_with_the_area_and_fixed_quantity_does_not()
    {
        var t = Guid.NewGuid();
        var perM2 = BuildBudgetTemplateItem.Create(Tenant, t, 1, "Piso cerâmico", BuildCostKind.Material, null, "m²", 1.1m, null, 45m, "Acabamento");
        var fixedQty = BuildBudgetTemplateItem.Create(Tenant, t, 2, "Projeto", BuildCostKind.Service, null, "un", null, 1m, 3000m, null);

        perM2.QuantityFor(120m).Should().Be(132m);
        perM2.QuantityFor(73.5m).Should().Be(80.85m);
        perM2.QuantityFor(0.333m).Should().Be(0.366m);   // 3 decimals
        fixedQty.QuantityFor(120m).Should().Be(1m);
        fixedQty.QuantityFor(null).Should().Be(1m);
        perM2.Category.Should().Be("Material");          // default category from the kind
    }

    [Fact]
    public void Template_item_per_square_meter_needs_an_area()
    {
        var item = BuildBudgetTemplateItem.Create(Tenant, Guid.NewGuid(), 1, "Reboco", BuildCostKind.Labor, null, "m²", 2m, null, 18m, null);
        item.Invoking(i => i.QuantityFor(null)).Should().Throw<DomainException>().Which.Code.Should().Be("template_area_required");
        item.Invoking(i => i.QuantityFor(0m)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1.0, 2.0)]
    public void Template_item_needs_exactly_one_kind_of_quantity(double? perM2, double? fixedQty)
    {
        var act = () => BuildBudgetTemplateItem.Create(Tenant, Guid.NewGuid(), 1, "X", BuildCostKind.Other, null, "un",
            (decimal?)perM2, (decimal?)fixedQty, 1m, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("template_item_quantity");
    }

    // ── Totals per kind and per m² ────────────────────────────────────────────

    [Fact]
    public void Totals_split_by_kind_apply_the_margin_and_divide_by_the_area()
    {
        var items = new (BuildCostKind, decimal)[]
        {
            (BuildCostKind.Material, 1000m), (BuildCostKind.Material, 500.50m), (BuildCostKind.Labor, 800m),
            (BuildCostKind.Service, 200m), (BuildCostKind.Equipment, 99.50m), (BuildCostKind.Other, 400m),
        };
        var t = BuildBudgetCalculator.Totals(items, 10m, 100m);

        t.Material.Should().Be(1500.50m);
        t.Labor.Should().Be(800m);
        t.Service.Should().Be(200m);
        t.Equipment.Should().Be(99.50m);
        t.Other.Should().Be(400m);
        t.Subtotal.Should().Be(3000m);
        t.Total.Should().Be(3300m);
        t.MarginValue.Should().Be(300m);
        t.CostPerM2.Should().Be(30m);
        t.TotalPerM2.Should().Be(33m);
    }

    [Fact]
    public void Totals_without_area_have_no_per_square_meter_and_round_to_cents()
    {
        var t = BuildBudgetCalculator.Totals([(BuildCostKind.Labor, 100m)], 12.345m, null);
        t.Total.Should().Be(112.35m);
        t.CostPerM2.Should().BeNull();
        t.TotalPerM2.Should().BeNull();
        BuildBudgetCalculator.Totals([(BuildCostKind.Labor, 100m)], 0m, 0m).TotalPerM2.Should().BeNull();
    }

    [Fact]
    public void Budget_dto_exposes_kind_totals_cost_per_m2_and_legacy_items_count_as_other()
    {
        var b = Budget(20m);
        b.SetTerms(null, null, null, 50m);
        var items = new[] { Item(b, "Cimento", BuildCostKind.Material, 10m, 40m), BuildBudgetItem.Create(Tenant, b.Id, "Legado", "Mão de obra", 1m, "vb", 100m) };
        b.RecalculateTotals(items);

        var dto = BuildBudgetCalculator.ToDto(b, items, Today);
        dto.Totals!.Material.Should().Be(400m);
        dto.Totals.Other.Should().Be(100m);
        dto.FinalPrice.Should().Be(600m);
        dto.CostPerM2.Should().Be(10m);
        dto.PricePerM2.Should().Be(12m);
        dto.Items.Single(i => i.Name == "Legado").Kind.Should().Be("Other");
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_sent_budget_past_its_validity_is_expired_and_cannot_be_approved()
    {
        var b = Budget();
        b.SetTerms(Today.AddDays(-1), null, null, null);
        b.IsExpiredOn(Today).Should().BeFalse("only a Sent budget expires");
        b.MarkSent();
        b.IsExpiredOn(Today).Should().BeTrue();
        b.IsExpiredOn(Today.AddDays(-1)).Should().BeFalse("valid through the last day");

        b.Invoking(x => x.Approve(Today, User, "Ana")).Should().Throw<DomainException>().Which.Code.Should().Be("budget_expired");
        b.Status.Should().Be(BuildBudgetStatus.Sent);
    }

    [Fact]
    public void Approval_and_rejection_record_who_and_when()
    {
        var b = Budget();
        b.SetTerms(Today, null, null, null);
        b.MarkSent();
        b.Approve(Today, User, "Ana");
        b.ApprovedBy.Should().Be(User);
        b.ApprovedByName.Should().Be("Ana");
        b.ApprovedAt.Should().NotBeNull();

        var r = Budget();
        r.Reject(User, "Bruno", "Cliente achou caro");
        r.RejectedByName.Should().Be("Bruno");
        r.RejectionReason.Should().Be("Cliente achou caro");
    }

    // ── Versions / duplicate ──────────────────────────────────────────────────

    [Fact]
    public void A_new_version_copies_the_terms_points_to_the_parent_and_keeps_the_family()
    {
        var v1 = Budget(10m);
        v1.SetTerms(Today.AddDays(10), "Pagamento em 3x", "margem apertada", 80m);
        v1.Invoking(b => b.NewVersion(User, 2)).Should().Throw<DomainException>().Which.Code.Should().Be("budget_version_not_allowed");
        v1.MarkSent();

        var v2 = v1.NewVersion(User, 2);
        v2.Version.Should().Be(2);
        v2.ParentBudgetId.Should().Be(v1.Id);
        v2.FamilyId.Should().Be(v1.Id);
        v2.Status.Should().Be(BuildBudgetStatus.Draft);
        v2.Notes.Should().Be("Pagamento em 3x");
        v2.MarginPercent.Should().Be(10m);

        v2.MarkSent();
        var v3 = v2.NewVersion(User, 3);
        v3.FamilyId.Should().Be(v1.Id);
        v3.ParentBudgetId.Should().Be(v2.Id);

        var copy = v1.Duplicate(User);
        copy.Version.Should().Be(1);
        copy.ParentBudgetId.Should().BeNull();
        copy.FamilyId.Should().Be(copy.Id);
    }

    // ── PDF model ─────────────────────────────────────────────────────────────

    [Fact]
    public void Pdf_model_has_prices_with_margin_and_never_the_internal_notes_nor_the_margin()
    {
        var b = Budget(17.5m);
        b.SetTerms(Today.AddDays(15), "Validade de 15 dias.", "SEGREDO-INTERNO margem negociável", 100m);
        var items = new[]
        {
            Item(b, "Bloco cerâmico", BuildCostKind.Material, 3m, 33.33m, "Alvenaria"),
            Item(b, "Pedreiro", BuildCostKind.Labor, 10m, 180m, "Alvenaria"),
            Item(b, "Projeto elétrico", BuildCostKind.Service, 1m, 1234.56m),
        };
        b.RecalculateTotals(items);

        var model = BuildBudgetPdfModelBuilder.Build(b, items, "Construtora Silva", null, "11 4000-0000", "Residência Alfa",
            "Maria", "Rua A, 1", null, Today);

        typeof(BuildBudgetPdfModel).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Internal") || n.Contains("Margin") || n.Contains("Cost"));
        typeof(BuildBudgetPdfLine).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Cost") || n.Contains("Margin"));
        var json = JsonSerializer.Serialize(model);
        json.Should().NotContain("SEGREDO-INTERNO");
        json.Should().NotContain("17.5");

        model.Total.Should().Be(b.FinalPrice);
        model.Groups.Sum(g => g.Lines.Sum(l => l.Total)).Should().Be(b.FinalPrice, "lines add up to the price, rounding included");
        model.Groups.Select(g => g.Kind).Should().Equal("Material", "Labor", "Service");
        model.Groups[1].Lines[0].UnitPrice.Should().Be(211.50m);        // 180 × 1.175
        model.Groups[0].Lines[0].Stage.Should().Be("Alvenaria");
        model.Notes.Should().Be("Validade de 15 dias.");
        model.TotalPerM2.Should().Be(Math.Round(b.FinalPrice / 100m, 2));
        model.Version.Should().Be(1);
    }

    // ── Change orders ─────────────────────────────────────────────────────────

    private static BuildChangeOrder ChangeOrder(decimal cost = 1500m, int days = 5, Guid? stage = null)
        => BuildChangeOrder.Create(Tenant, Guid.NewGuid(), 1, "Troca de piso", null, BuildChangeOrderReason.ClientRequest, cost,
            days, days == 0 ? stage : stage ?? Guid.NewGuid(), User, "Ana");

    [Fact]
    public void Change_order_approval_adds_the_cost_to_the_approved_budget_only_when_there_is_one()
    {
        var project = BuildProject.Create(Tenant, User, "Obra", "Cliente", BuildProjectType.House);
        project.ApplyChangeOrderCost(1000m).Should().BeFalse();
        project.BudgetApproved.Should().BeNull();

        project.ApproveBudget(100_000m);
        project.ApplyChangeOrderCost(1500.555m).Should().BeTrue();
        project.BudgetApproved.Should().Be(101_500.56m);
        project.ApplyChangeOrderCost(-500m).Should().BeTrue();
        project.BudgetApproved.Should().Be(101_000.56m);
        project.Invoking(p => p.ApplyChangeOrderCost(-200_000m)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("change_order_negative_contract");
    }

    [Fact]
    public void Change_order_schedule_impact_moves_only_the_affected_stage_end()
    {
        var stage = BuildStage.Create(Tenant, Guid.NewGuid(), "Acabamento", 1, null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        stage.ShiftPlannedEnd(7).Should().BeTrue();
        stage.PlannedEndDate.Should().Be(new DateOnly(2026, 12, 7));
        stage.ShiftPlannedEnd(-3).Should().BeTrue();
        stage.PlannedEndDate.Should().Be(new DateOnly(2026, 12, 4));
        stage.ShiftPlannedEnd(0).Should().BeFalse();
        stage.Invoking(s => s.ShiftPlannedEnd(-60)).Should().Throw<DomainException>().Which.Code.Should().Be("stage_end_before_start");

        BuildStage.Create(Tenant, Guid.NewGuid(), "Sem datas", 2).ShiftPlannedEnd(5).Should().BeFalse();
    }

    [Fact]
    public void Change_order_is_decided_once_and_the_portal_only_decides_what_was_sent()
    {
        var co = ChangeOrder();
        co.Invoking(c => c.Approve(BuildDecisionChannel.Portal, BuildChangeOrder.PortalDecider, null, null, "Maria", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("change_order_not_sent");

        co.Send(DateTime.UtcNow);
        co.Approve(BuildDecisionChannel.Portal, BuildChangeOrder.PortalDecider, null, "ok", "Maria", DateTime.UtcNow);
        co.Status.Should().Be(BuildChangeOrderStatus.Approved);
        co.DecisionChannel.Should().Be(BuildDecisionChannel.Portal);
        co.SignerName.Should().Be("Maria");

        co.Invoking(c => c.Reject(BuildDecisionChannel.Internal, "Ana", User, null, null, DateTime.UtcNow)).Should().Throw<ConflictException>();
        co.Invoking(c => c.Cancel()).Should().Throw<ConflictException>();
        co.Invoking(c => c.Update("x", null, BuildChangeOrderReason.Other, 1m, 0, null)).Should().Throw<DomainException>();

        var draft = ChangeOrder(days: 0);
        draft.Approve(BuildDecisionChannel.Internal, "Ana", User, null, null, DateTime.UtcNow);   // the company may approve a draft
        draft.Status.Should().Be(BuildChangeOrderStatus.Approved);
    }

    [Fact]
    public void Change_order_with_a_schedule_impact_needs_the_affected_stage()
    {
        var act = () => BuildChangeOrder.Create(Tenant, Guid.NewGuid(), 1, "Prazo", null, BuildChangeOrderReason.SiteCondition, 0m, 10,
            null, User, "Ana");
        act.Should().Throw<DomainException>().Which.Code.Should().Be("change_order_stage_required");
        ChangeOrder(cost: -250m, days: 0).CostImpact.Should().Be(-250m);
    }

    // ── Selections ────────────────────────────────────────────────────────────

    [Fact]
    public void Selection_choice_only_while_open_and_up_to_the_deadline()
    {
        var s = BuildSelection.Create(Tenant, Guid.NewGuid(), "Piso da sala", null, null, Today, true, User);
        s.Invoking(x => x.Choose(Guid.NewGuid(), null, Today.AddDays(1), DateTime.UtcNow, BuildDecisionChannel.Portal))
            .Should().Throw<DomainException>().Which.Code.Should().Be("selection_deadline_passed");

        var option = Guid.NewGuid();
        s.Choose(option, "Gostei", Today, DateTime.UtcNow, BuildDecisionChannel.Portal);
        s.Status.Should().Be(BuildSelectionStatus.Submitted);
        s.Invoking(x => x.Choose(option, null, Today, DateTime.UtcNow, BuildDecisionChannel.Portal)).Should().Throw<ConflictException>();

        s.Confirm(null, "Ana", DateTime.UtcNow);
        s.Status.Should().Be(BuildSelectionStatus.Confirmed);
        s.Invoking(x => x.Confirm(null, "Ana", DateTime.UtcNow)).Should().Throw<ConflictException>();

        var open = BuildSelection.Create(Tenant, Guid.NewGuid(), "Bancada", null, null, null, true, User);
        open.Invoking(x => x.Confirm(null, "Ana", DateTime.UtcNow)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("selection_option_required");
    }

    [Fact]
    public void Option_changes_the_contract_when_it_costs_more_less_or_has_a_lead_time()
    {
        var sel = Guid.NewGuid();
        BuildSelectionOption.Create(Tenant, sel, 1, "Padrão", null, 0m, true, null, null).ChangesContract.Should().BeFalse();
        BuildSelectionOption.Create(Tenant, sel, 2, "Porcelanato", null, 2500m, false, null, null).ChangesContract.Should().BeTrue();
        BuildSelectionOption.Create(Tenant, sel, 3, "Vinílico", null, -300m, false, null, null).ChangesContract.Should().BeTrue();
        BuildSelectionOption.Create(Tenant, sel, 4, "Importado", null, 0m, false, null, 20).ChangesContract.Should().BeTrue();
    }

    // ── Materials ─────────────────────────────────────────────────────────────

    [Fact]
    public void Material_deliveries_are_partial_until_the_requested_quantity_and_never_exceed_it()
    {
        var m = BuildMaterialRequest.Create(Tenant, Guid.NewGuid(), null, "Cimento CP-II", "saco", 100m, Today.AddDays(-1), null, User, "Ana");
        m.IsLate(Today).Should().BeTrue();
        m.Invoking(x => x.RegisterDelivery(10m, Today)).Should().Throw<DomainException>().Which.Code.Should().Be("material_not_ordered");

        m.Quote(null, 32.9m, DateTime.UtcNow);
        m.PlaceOrder(null, null, DateTime.UtcNow);
        m.EstimatedUnitCost.Should().Be(32.9m);
        m.RegisterDelivery(40m, Today);
        m.Status.Should().Be(BuildMaterialRequestStatus.PartiallyDelivered);
        m.Invoking(x => x.RegisterDelivery(61m, Today)).Should().Throw<DomainException>().Which.Code.Should().Be("delivery_exceeds_quantity");
        m.RegisterDelivery(60m, Today);
        m.Status.Should().Be(BuildMaterialRequestStatus.Delivered);
        m.IsLate(Today).Should().BeFalse();
        m.Invoking(x => x.Cancel(DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    // ── Measurements ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(30, 30)]
    [InlineData(30, 55)]
    [InlineData(0, 100)]
    public void Measured_percent_is_cumulative_between_previous_and_100(int previous, int measured)
        => BuildMeasurementItem.Validate(previous, measured);

    [Theory]
    [InlineData(40, 39, "measurement_below_previous")]
    [InlineData(40, 101, "measurement_percent_range")]
    [InlineData(0, -1, "measurement_percent_range")]
    public void Measured_percent_below_previous_or_above_100_is_refused(int previous, int measured, string code)
    {
        var act = () => BuildMeasurementItem.Validate(previous, measured);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Measurement_item_is_rechecked_against_the_stage_at_approval_and_approval_happens_once()
    {
        var item = BuildMeasurementItem.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), 20, 50, null);
        item.DeltaPercent.Should().Be(30);
        item.RefreshPrevious(35);
        item.DeltaPercent.Should().Be(15);
        item.Invoking(i => i.RefreshPrevious(60)).Should().Throw<DomainException>().Which.Code.Should().Be("measurement_below_previous");

        var m = BuildMeasurement.Create(Tenant, Guid.NewGuid(), 1, Today.AddDays(-30), Today, null, User, "Ana");
        m.Approve(User, "Ana", DateTime.UtcNow);
        m.Invoking(x => x.Approve(User, "Ana", DateTime.UtcNow)).Should().Throw<ConflictException>();
        m.Invoking(x => x.Update(Today, Today, null)).Should().Throw<DomainException>().Which.Code.Should().Be("measurement_not_draft");

        var bad = () => BuildMeasurement.Create(Tenant, Guid.NewGuid(), 1, Today, Today.AddDays(-1), null, User, "Ana");
        bad.Should().Throw<DomainException>().Which.Code.Should().Be("measurement_period_invalid");
    }
}
