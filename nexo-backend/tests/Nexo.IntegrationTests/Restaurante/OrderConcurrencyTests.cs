using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Npgsql;

namespace Nexo.IntegrationTests.Restaurante;

/// <summary>
/// Requisições realmente concorrentes sobre a mesma comanda: fechar, incluir e remover item, cozinha, cancelar e pagar.
/// A comanda fechada é a venda: o que a comanda diz que foi consumido tem de ser o que a venda cobra e o que sai do
/// estoque, e uma comanda fechada ou paga nunca volta atrás.
///
/// A janela da corrida é aberta de propósito, sem sleep: de outra conexão, o teste segura um <c>FOR UPDATE</c>
/// (<see cref="RowGate"/>) numa linha que a primeira requisição vai precisar. Ela para dentro do PostgreSQL exatamente ali, e
/// o teste só segue quando o próprio <c>pg_stat_activity</c> mostra a espera. A segunda requisição então termina, ou fica
/// esperando a primeira. O resultado não depende de quem foi mais rápido.
/// </summary>
[Collection("Integration")]
public class OrderConcurrencyTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private HttpClient _client = null!;
    private string _connectionString = null!;

    public OrderConcurrencyTests(TestWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            _connectionString = db.Database.GetConnectionString()!;
            var tenantId = await db.Users.IgnoreQueryFilters()
                .Where(u => u.Login == TestCredentials.AdminLogin).Select(u => u.TenantId).SingleAsync();
            if (!await db.ModuleSubscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId && s.ModuleKey == "restaurante"))
            {
                db.ModuleSubscriptions.Add(ModuleSubscription.CreateFromStripe(
                    tenantId, "restaurante", "sub_test_restaurante", "price_test_restaurante", PlanType.Lifetime, DateTime.UtcNow, null));
                await db.SaveChangesAsync();
            }
        }

        var open = await _client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(OpeningBalance: 0, Notes: "corrida na comanda"));
        open.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════
    // Incluir item × fechar
    // ═══════════════════════════════════════════════════════════

    /// <summary>O fechamento já leu os itens e está gravando a venda quando o garçom inclui mais um.</summary>
    [Fact]
    public async Task An_item_added_while_the_comanda_closes_never_stays_out_of_the_sale()
    {
        var dish = await ProductAsync(10m);
        var extra = await ProductAsync(7m);
        var order = await OpenWithItemsAsync(dish);

        await using var gate = await RowGate.HoldAsync(_connectionString, "products", "id", dish);
        var close = Close(order.Id);
        await WaitUntilParkedAsync(1);

        var add = AddItem(order.Id, extra);
        await WaitUntilParkedAsync(2, orFinished: add);
        await gate.ReleaseAsync();

        (await close).StatusCode.Should().Be(HttpStatusCode.OK);
        (await add).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a comanda fechou antes do item chegar");

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Closed);
        state.ComandaItems.Should().Be(state.SaleItems, "o que a comanda diz que foi consumido é o que a venda cobra");
    }

    /// <summary>
    /// A inclusão já leu a comanda aberta e vai gravar o item quando a comanda fecha e é paga. Sem a trava, o item cai numa
    /// comanda já paga: sem pagamento, sem baixa de estoque. O portão é na escrita da tabela de itens, e não numa linha: o
    /// INSERT para antes de tocar na comanda, e a chave estrangeira não atrapalha o pagamento.
    /// </summary>
    [Fact]
    public async Task An_item_still_being_written_when_the_comanda_closes_never_lands_in_a_paid_comanda()
    {
        var dish = await ProductAsync(10m);
        var extra = await ProductAsync(7m);
        var order = await OpenWithItemsAsync(dish);

        await using var gate = await RowGate.HoldWritesAsync(_connectionString, "rest_order_items");
        var add = AddItem(order.Id, extra);
        await WaitUntilParkedAsync(1);

        var close = Close(order.Id);
        await WaitUntilParkedAsync(2, orFinished: close);
        if (close.IsCompleted)
            (await PayCash(order.Id, 10m)).StatusCode.Should().Be(HttpStatusCode.OK, "sem trava, a comanda fecha e é paga com o item ainda sendo gravado");
        await gate.ReleaseAsync();

        (await add).StatusCode.Should().Be(HttpStatusCode.OK, "o item chegou antes do fechamento");
        (await close).StatusCode.Should().Be(HttpStatusCode.OK);

        var closed = await StateOfAsync(order.Id);
        if (closed.Status == RestOrderStatus.Closed)
            (await PayCash(order.Id, closed.SaleItems)).StatusCode.Should().Be(HttpStatusCode.OK);

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Paid);
        state.ComandaItems.Should().Be(17m);
        state.SaleItems.Should().Be(17m, "todo item da comanda paga foi cobrado");
        state.Paid.Should().Be(17m);
        state.StockDeducted.Should().Be(state.ComandaQuantity, "todo item da comanda paga saiu do estoque");
    }

    // ═══════════════════════════════════════════════════════════
    // Fechar × fechar
    // ═══════════════════════════════════════════════════════════

    /// <summary>Duplo clique em "fechar conta": uma venda só, sem rascunho órfão.</summary>
    [Fact]
    public async Task Closing_twice_at_once_makes_a_single_sale()
    {
        var dish = await ProductAsync(10m);
        var order = await OpenWithItemsAsync(dish);

        await using var gate = await RowGate.HoldAsync(_connectionString, "products", "id", dish);
        var first = Close(order.Id);
        await WaitUntilParkedAsync(1);

        var second = Close(order.Id);
        await WaitUntilParkedAsync(2, orFinished: second);
        await gate.ReleaseAsync();

        var statuses = new[] { (await first).StatusCode, (await second).StatusCode };
        statuses.Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, "só um fechamento vence");

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Closed);
        state.SalesOfTheComanda.Should().Be(1, "a comanda tem uma venda só");
    }

    /// <summary>Um produto desativado depois de pedido faz o fechamento falhar no meio: nenhuma venda fica para trás.</summary>
    [Fact]
    public async Task A_close_that_fails_midway_leaves_no_sale_behind()
    {
        var dish = await ProductAsync(10m);
        var side = await ProductAsync(7m);
        var order = await OpenWithItemsAsync(dish, products: side);
        (await _client.PostAsync($"/api/products/{side}/deactivate", null)).EnsureSuccessStatusCode();

        (await Close(order.Id)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Open);
        state.SalesOfTheComanda.Should().Be(0, "a venda e a comanda fecham juntas, ou nenhuma delas");
    }

    // ═══════════════════════════════════════════════════════════
    // Cancelar × fechar
    // ═══════════════════════════════════════════════════════════

    /// <summary>Dois atos que se excluem: ou a comanda fecha, ou é cancelada, nunca os dois.</summary>
    [Fact]
    public async Task Cancelling_while_the_comanda_closes_leaves_a_single_outcome()
    {
        var dish = await ProductAsync(10m);
        var table = await TableAsync();
        var order = await OpenWithItemsAsync(dish, table);

        await using var gate = await RowGate.HoldAsync(_connectionString, "products", "id", dish);
        var close = Close(order.Id);
        await WaitUntilParkedAsync(1);

        var cancel = _client.PostAsync($"/api/restaurante/orders/{order.Id}/cancel", null);
        await WaitUntilParkedAsync(2, orFinished: cancel);
        await gate.ReleaseAsync();

        (await close).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancel).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a comanda já estava fechada quando o cancelamento leu");

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Closed);
        state.CancelledAt.Should().BeNull("uma comanda fechada não foi cancelada");
        state.SalesOfTheComanda.Should().Be(1);
        state.TableStatus.Should().Be(RestTableStatus.Occupied, "a mesa só libera no pagamento");
    }

    // ═══════════════════════════════════════════════════════════
    // Remover item × fechar
    // ═══════════════════════════════════════════════════════════

    /// <summary>O item é removido enquanto o fechamento grava a venda: a venda não pode cobrar um item que a comanda cancelou.</summary>
    [Fact]
    public async Task Removing_an_item_while_the_comanda_closes_never_charges_a_cancelled_item()
    {
        var dish = await ProductAsync(10m);
        var side = await ProductAsync(7m);
        var order = await OpenWithItemsAsync(dish, products: side);
        var sideItem = order.Items.Single(i => i.ProductId == side);

        await using var gate = await RowGate.HoldAsync(_connectionString, "products", "id", dish);
        var close = Close(order.Id);
        await WaitUntilParkedAsync(1);

        var remove = _client.DeleteAsync($"/api/restaurante/orders/{order.Id}/items/{sideItem.Id}");
        await WaitUntilParkedAsync(2, orFinished: remove);
        await gate.ReleaseAsync();

        (await close).StatusCode.Should().Be(HttpStatusCode.OK);
        (await remove).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a comanda já estava fechada quando a remoção leu");

        var state = await StateOfAsync(order.Id);
        state.ComandaItems.Should().Be(state.SaleItems, "a venda cobra o que a comanda tem de ativo");
    }

    // ═══════════════════════════════════════════════════════════
    // Cozinha × fechar × pagar
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// A cozinha marca o último prato como pronto enquanto a comanda fecha e é paga. A comanda paga não pode voltar para
    /// "pronta": ela poderia ser fechada e paga de novo, com outra venda, outro pagamento e outra baixa de estoque.
    /// </summary>
    [Fact]
    public async Task A_kitchen_update_never_reopens_a_paid_comanda()
    {
        var dish = await ProductAsync(10m);
        var order = await OpenWithItemsAsync(dish);
        var item = order.Items.Single();
        (await Kitchen(order.Id, item.Id, "Preparing")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var gate = await RowGate.HoldAsync(_connectionString, "rest_order_items", "id", item.Id);
        var ready = Kitchen(order.Id, item.Id, "Ready");
        await WaitUntilParkedAsync(1);

        var close = Close(order.Id);
        await WaitUntilParkedAsync(2, orFinished: close);
        if (close.IsCompleted)
            (await PayCash(order.Id, 10m)).StatusCode.Should().Be(HttpStatusCode.OK);
        await gate.ReleaseAsync();

        (await ready).StatusCode.Should().Be(HttpStatusCode.OK);
        (await close).StatusCode.Should().Be(HttpStatusCode.OK);

        var state = await StateOfAsync(order.Id);
        state.Status.Should().BeOneOf(new[] { RestOrderStatus.Closed, RestOrderStatus.Paid }, "fechada ou paga, a comanda não reabre");
        (await Close(order.Id)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fechar de novo geraria outra venda");
    }

    // ═══════════════════════════════════════════════════════════
    // Pagar × pagar
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Pagamento duplicado com todos dentro da janela: um paga e parou na baixa de estoque; os outros já passaram pelas
    /// checagens de fora da transação e esperam a trava. Só um pagamento, uma baixa e uma baixa de ficha técnica.
    /// </summary>
    [Fact]
    public async Task Paying_at_once_charges_and_deducts_stock_only_once()
    {
        var dish = await ProductAsync(10m);
        var ingredient = await ProductAsync(1m, isIngredient: true);
        var cooked = await ProductAsync(20m);
        await RecipeAsync(cooked, ingredient, quantity: 2m);
        var order = await OpenWithItemsAsync(dish, products: cooked);
        (await Close(order.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var gate = await RowGate.HoldAsync(_connectionString, "stock_items", "product_id", dish);
        var calls = Enumerable.Range(0, 4).Select(_ => PayCash(order.Id, 30m)).ToList();
        await WaitUntilParkedAsync(4);
        await gate.ReleaseAsync();

        var statuses = (await Task.WhenAll(calls)).Select(r => r.StatusCode).ToList();
        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(1, "só um pagamento vence");
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(3, "os outros encontram a comanda paga");

        var state = await StateOfAsync(order.Id);
        state.Status.Should().Be(RestOrderStatus.Paid);
        state.PaymentLines.Should().Be(1);
        state.Paid.Should().Be(30m);
        state.StockDeducted.Should().Be(1m, "o prato sem ficha técnica baixa uma vez");
        (await RecipeDeductedAsync(order.Id)).Should().Be(2m, "o insumo da ficha técnica baixa uma vez");
        (await StockOfAsync(dish)).Should().Be(99m);
        (await StockOfAsync(ingredient)).Should().Be(98m);
    }

    /// <summary>
    /// Duas comandas diferentes pagas ao mesmo tempo, com o mesmo insumo na ficha técnica: o segundo pagamento perde a
    /// concorrência otimista do estoque. É um conflito para tentar de novo, como na conclusão da venda, e não um erro do
    /// servidor. E nenhuma baixa se perde. Em Pix, de propósito: em dinheiro, a trava do caixa já põe os dois em fila.
    /// </summary>
    [Fact]
    public async Task Paying_two_comandas_that_share_an_ingredient_is_a_retry_and_loses_no_stock()
    {
        var ingredient = await ProductAsync(1m, isIngredient: true);
        var cooked = await ProductAsync(20m);
        await RecipeAsync(cooked, ingredient, quantity: 2m);
        var a = await OpenWithItemsAsync(cooked);
        var b = await OpenWithItemsAsync(cooked);
        (await Close(a.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Close(b.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var gate = await RowGate.HoldAsync(_connectionString, "stock_items", "product_id", ingredient);
        var payA = PayPix(a.Id, 20m);
        var payB = PayPix(b.Id, 20m);
        await WaitUntilParkedAsync(2);
        await gate.ReleaseAsync();

        var responses = new[] { (Order: a.Id, Response: await payA), (Order: b.Id, Response: await payB) };
        responses.Select(r => r.Response.StatusCode).Should().BeEquivalentTo(
            new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, "um paga; o outro é avisado para tentar de novo");

        var loser = responses.Single(r => r.Response.StatusCode != HttpStatusCode.OK).Order;
        (await StateOfAsync(loser)).Status.Should().Be(RestOrderStatus.Closed, "o pagamento recusado não deixou nada para trás");
        (await StateOfAsync(loser)).PaymentLines.Should().Be(0);
        (await StockOfAsync(ingredient)).Should().Be(98m);

        (await PayPix(loser, 20m)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StockOfAsync(ingredient)).Should().Be(96m, "as duas comandas baixaram o insumo");
    }

    // ═══════════════════════════════════════════════════════════
    // Comandas diferentes fechando juntas
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// O número da venda é max+1. Com o fechamento numa transação, a venda só aparece no commit: sem cuidado, quem criasse
    /// outra venda enquanto isso pegaria o mesmo número e receberia 409. Comandas e vendas diferentes não têm por que colidir.
    /// </summary>
    [Fact]
    public async Task Sales_created_while_another_comanda_closes_get_their_own_numbers()
    {
        var dish = await ProductAsync(10m);
        var other = await ProductAsync(12m);
        var slow = await OpenWithItemsAsync(dish);
        var fast = await OpenWithItemsAsync(other);

        await using var gate = await RowGate.HoldAsync(_connectionString, "products", "id", dish);
        var closeSlow = Close(slow.Id);
        await WaitUntilParkedAsync(1);

        var closeFast = Close(fast.Id);
        var counter = _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        await WaitUntilParkedAsync(3, orFinished: Task.WhenAll(closeFast, counter));
        await gate.ReleaseAsync();

        (await closeSlow).StatusCode.Should().Be(HttpStatusCode.OK);
        (await closeFast).StatusCode.Should().Be(HttpStatusCode.OK);
        (await counter).StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var saleIds = new[] { (await StateOfAsync(slow.Id)).SaleId, (await StateOfAsync(fast.Id)).SaleId };
        var numbers = await db.Sales.IgnoreQueryFilters().AsNoTracking().Where(s => saleIds.Contains(s.Id)).Select(s => s.Number).ToListAsync();
        numbers.Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════
    // Tudo junto, sem ordem imposta
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Sem portão nenhum: fechamentos, inclusões, remoção e cozinha ao mesmo tempo. Nenhuma resposta 500 (um deadlock
    /// apareceria aqui), e a venda sempre igual à comanda.
    /// </summary>
    [Fact]
    public async Task Everything_at_once_keeps_the_comanda_and_the_sale_equal()
    {
        var extra = await ProductAsync(7m);
        for (var round = 0; round < 5; round++)
        {
            var dish = await ProductAsync(10m);
            var side = await ProductAsync(3m);
            var order = await OpenWithItemsAsync(dish, products: side);
            var sideItem = order.Items.Single(i => i.ProductId == side);
            var dishItem = order.Items.Single(i => i.ProductId == dish);

            var calls = new List<Task<HttpResponseMessage>>
            {
                Close(order.Id),
                Close(order.Id),
                _client.DeleteAsync($"/api/restaurante/orders/{order.Id}/items/{sideItem.Id}"),
                Kitchen(order.Id, dishItem.Id, "Preparing"),
            };
            calls.AddRange(Enumerable.Range(0, 4).Select(_ => AddItem(order.Id, extra)));
            var responses = await Task.WhenAll(calls);
            responses.Should().OnlyContain(r => (int)r.StatusCode < 500, "uma corrida nunca é erro do servidor");

            var state = await StateOfAsync(order.Id);
            state.Status.Should().Be(RestOrderStatus.Closed, $"rodada {round}");
            state.SalesOfTheComanda.Should().Be(1, $"rodada {round}: uma venda só");
            state.ComandaItems.Should().Be(state.SaleItems, $"rodada {round}: a venda cobra o que a comanda tem de ativo");
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// De outra conexão, segura uma linha (<c>FOR UPDATE</c>) ou as escritas de uma tabela (<c>LOCK TABLE ... SHARE</c>) até ser
    /// solto: quem precisar delas para no PostgreSQL. As leituras comuns seguem livres.
    /// </summary>
    private sealed class RowGate : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private bool _released;

        private RowGate(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<RowGate> HoldAsync(string connectionString, string table, string column, Guid value)
        {
            var (connection, transaction) = await BeginAsync(connectionString);
            await using var command = new NpgsqlCommand($"SELECT 1 FROM nexo.{table} WHERE {column} = @value FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("value", value);
            (await command.ExecuteScalarAsync()).Should().NotBeNull($"a linha de {table} precisa existir para o portão fazer sentido");
            return new RowGate(connection, transaction);
        }

        public static async Task<RowGate> HoldWritesAsync(string connectionString, string table)
        {
            var (connection, transaction) = await BeginAsync(connectionString);
            await using var command = new NpgsqlCommand($"LOCK TABLE nexo.{table} IN SHARE MODE", connection, transaction);
            await command.ExecuteNonQueryAsync();
            return new RowGate(connection, transaction);
        }

        private static async Task<(NpgsqlConnection, NpgsqlTransaction)> BeginAsync(string connectionString)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return (connection, await connection.BeginTransactionAsync());
        }

        public async Task ReleaseAsync()
        {
            if (_released) return;
            _released = true;
            await _transaction.RollbackAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Espera até <paramref name="parked"/> conexões estarem esperando uma trava no PostgreSQL, ou até
    /// <paramref name="orFinished"/> terminar: sem a trava da comanda, a segunda requisição não espera, ela termina.
    /// O intervalo só decide de quanto em quanto se olha: não decide o resultado.
    /// </summary>
    private async Task WaitUntilParkedAsync(int parked, Task? orFinished = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (orFinished is { IsCompleted: true }) return;
            if ((long)(await command.ExecuteScalarAsync())! >= parked) return;
            DateTime.UtcNow.Should().BeBefore(deadline, $"{parked} requisição(ões) deveriam estar esperando uma trava");
            await Task.Delay(20);
        }
    }

    /// <summary>O que o banco diz da comanda e da venda dela, sem passar pela API.</summary>
    private sealed record OrderState(
        RestOrderStatus Status, DateTime? CancelledAt, Guid? SaleId, RestTableStatus? TableStatus,
        decimal ComandaItems, decimal ComandaQuantity,
        int SalesOfTheComanda, decimal SaleItems, decimal Paid, int PaymentLines, decimal StockDeducted);

    private async Task<OrderState> StateOfAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var order = await db.RestOrders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == orderId);
        var active = await db.RestOrderItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.OrderId == orderId && i.Status != RestOrderItemStatus.Cancelled).ToListAsync();
        var table = order.TableId is { } tableId
            ? await db.RestTables.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tableId)
            : null;

        var notes = $"Comanda #{order.OrderNumber}";
        var sales = await db.Sales.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(s => s.TenantId == order.TenantId && s.StoreId == order.StoreId && s.Notes == notes);
        var saleItems = await db.SaleItems.IgnoreQueryFilters().AsNoTracking().Where(i => i.SaleId == order.SaleId).ToListAsync();
        var payments = await db.SalePayments.IgnoreQueryFilters().AsNoTracking().Where(p => p.SaleId == order.SaleId).ToListAsync();
        var deducted = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ReferenceType == "Sale" && m.ReferenceId == order.SaleId && m.MovementType == StockMovementType.SaleOutput)
            .SumAsync(m => m.Quantity);

        return new OrderState(order.Status, order.CancelledAt, order.SaleId, table?.Status,
            active.Sum(i => i.Total), active.Sum(i => i.Quantity),
            sales, saleItems.Sum(i => i.Total), payments.Sum(p => p.Amount), payments.Count, deducted);
    }

    private async Task<decimal> RecipeDeductedAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ReferenceType == "Order" && m.ReferenceId == orderId && m.MovementType == StockMovementType.RecipeOutput)
            .SumAsync(m => m.Quantity);
    }

    private async Task<decimal> StockOfAsync(Guid productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.StockItems.IgnoreQueryFilters().AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.CurrentQuantity).SingleAsync();
    }

    private Task<HttpResponseMessage> Close(Guid orderId) => _client.PostAsync($"/api/restaurante/orders/{orderId}/close", null);

    private Task<HttpResponseMessage> AddItem(Guid orderId, Guid productId)
        => _client.PostAsJsonAsync($"/api/restaurante/orders/{orderId}/items", new AddOrderItemRequest(productId, 1m));

    private Task<HttpResponseMessage> Kitchen(Guid orderId, Guid itemId, string status)
        => _client.PatchAsJsonAsync($"/api/restaurante/orders/{orderId}/items/{itemId}/status", new UpdateOrderItemStatusRequest(status));

    private Task<HttpResponseMessage> PayCash(Guid orderId, decimal amount)
        => _client.PostAsJsonAsync($"/api/restaurante/orders/{orderId}/pay", new PayOrderRequest([new PaymentInputDto("Cash", "Cash", amount)]));

    private Task<HttpResponseMessage> PayPix(Guid orderId, decimal amount)
        => _client.PostAsJsonAsync($"/api/restaurante/orders/{orderId}/pay", new PayOrderRequest([new PaymentInputDto("Pix", "Cash", amount)]));

    /// <summary>Abre uma comanda (na mesa, se houver; senão, no balcão) e inclui um item de cada produto.</summary>
    private async Task<OrderDto> OpenWithItemsAsync(Guid product, Guid? table = null, params Guid[] products)
    {
        var opened = await _client.PostAsJsonAsync("/api/restaurante/orders",
            table is null ? new OpenOrderRequest("Counter") : new OpenOrderRequest("DineIn", TableId: table));
        opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var order = (await opened.Content.ReadFromJsonAsync<OrderDto>())!;
        foreach (var id in products.Prepend(product))
        {
            var added = await AddItem(order.Id, id);
            added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
            order = (await added.Content.ReadFromJsonAsync<OrderDto>())!;
        }
        return order;
    }

    private async Task<Guid> ProductAsync(decimal price, bool isIngredient = false)
    {
        var code = $"RCE-{Guid.NewGuid():N}"[..20];
        var created = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: $"Corrida {code}", Unit: "Un", SalePrice: price, CostPrice: price / 2, TrackStock: true, IsIngredient: isIngredient));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>())!;
        (await _client.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product.Id, 100, "ManualEntry", "corrida")))
            .EnsureSuccessStatusCode();
        return product.Id;
    }

    private async Task RecipeAsync(Guid productId, Guid ingredientId, decimal quantity)
    {
        var card = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards", new CreateRecipeCardRequest(productId, 1m, YieldUnit: "Un"));
        card.StatusCode.Should().Be(HttpStatusCode.Created, await card.Content.ReadAsStringAsync());
        var cardId = (await card.Content.ReadFromJsonAsync<RecipeCardDto>())!.Id;
        (await _client.PostAsJsonAsync($"/api/restaurante/recipe-cards/{cardId}/ingredients", new AddIngredientRequest(ingredientId, quantity, Unit: "Un")))
            .EnsureSuccessStatusCode();
    }

    private async Task<Guid> TableAsync()
    {
        var area = await _client.PostAsJsonAsync("/api/restaurante/areas", new CreateAreaRequest($"Corrida {Guid.NewGuid():N}"[..20]));
        area.StatusCode.Should().Be(HttpStatusCode.Created, await area.Content.ReadAsStringAsync());
        var table = await _client.PostAsJsonAsync("/api/restaurante/tables",
            new CreateTableRequest((await area.Content.ReadFromJsonAsync<AreaDto>())!.Id, $"C{Guid.NewGuid():N}"[..8]));
        table.StatusCode.Should().Be(HttpStatusCode.Created, await table.Content.ReadAsStringAsync());
        return (await table.Content.ReadFromJsonAsync<TableDto>())!.Id;
    }
}
