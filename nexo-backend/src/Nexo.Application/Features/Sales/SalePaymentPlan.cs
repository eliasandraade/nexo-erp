using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Sales;

/// <summary>One validated payment line: what is applied to the sale and, for physical cash, the troco.</summary>
public sealed record PlannedPayment(
    PaymentMethod Method,
    PaymentType Type,
    decimal Applied,
    decimal Change,
    DateTime? DueDate)
{
    /// <summary>Physical money: the only kind of payment that moves the cash drawer.</summary>
    public bool MovesCashDrawer => Method == PaymentMethod.Cash && Type == PaymentType.Cash;

    /// <summary>Paid now by Pix, card or transfer: settled revenue in the financeiro, never in the drawer.</summary>
    public bool IsSettledNonCash => Type == PaymentType.Cash && Method != PaymentMethod.Cash;

    public decimal Tendered => Applied + Change;
}

/// <summary>
/// Turns the payment lines sent by a client into what the sale will actually record. Pure — no I/O —
/// so every money rule of the checkout is unit-tested in one place:
/// <list type="bullet">
///   <item>amounts are positive with at most two decimals; method and type are known values;</item>
///   <item>a prazo (Credit) needs a due date;</item>
///   <item>the lines must cover the total; any excess is troco, and troco can only come from
///         physical cash — paying more than the total by Pix or card is rejected;</item>
///   <item>the troco is taken from the cash lines (last first) and never consumes a whole line —
///         cash handed over and handed straight back is a typing error, not a payment.</item>
/// </list>
/// After the plan, Σ Applied == total exactly.
/// </summary>
public static class SalePaymentPlan
{
    public static IReadOnlyList<PlannedPayment> Build(IReadOnlyList<PaymentInput>? payments, decimal total)
    {
        if (payments is null || payments.Count == 0)
            throw new DomainException("Informe ao menos um pagamento para concluir a venda.");
        if (total <= 0)
            throw new DomainException("O total da venda precisa ser maior que zero.");

        var lines = payments.Select(Parse).ToList();

        var received = lines.Sum(l => l.Amount);
        if (received < total)
            throw new DomainException(
                $"Pagamento insuficiente: recebido {received:F2}, total {total:F2}.");

        var change = received - total;
        var cashReceived = lines.Where(l => l.MovesCashDrawer).Sum(l => l.Amount);
        if (change > 0 && change >= cashReceived)
            throw new DomainException(change > cashReceived
                ? "Só pagamento em dinheiro pode ter troco. Pix, cartão e transferência devem ser do valor exato."
                : "O troco consumiria todo o valor em dinheiro. Revise os valores informados.");

        // Take the troco from the cash lines, last first.
        var applied = lines.Select(l => l.Amount).ToArray();
        var changes = new decimal[lines.Count];
        var remaining = change;
        for (var i = lines.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (!lines[i].MovesCashDrawer) continue;
            var take = Math.Min(remaining, applied[i] - 0.01m);   // keep at least one cent applied
            if (take <= 0) continue;
            applied[i] -= take;
            changes[i] += take;
            remaining  -= take;
        }
        if (remaining > 0)
            throw new DomainException("O troco consumiria todo o valor em dinheiro. Revise os valores informados.");

        return lines
            .Select((l, i) => new PlannedPayment(l.Method, l.Type, applied[i], changes[i], l.DueDate))
            .ToList();
    }

    private sealed record ParsedLine(PaymentMethod Method, PaymentType Type, decimal Amount, DateTime? DueDate)
    {
        public bool MovesCashDrawer => Method == PaymentMethod.Cash && Type == PaymentType.Cash;
    }

    private static ParsedLine Parse(PaymentInput p)
    {
        if (!Enum.TryParse<PaymentMethod>(p.Method, ignoreCase: true, out var method) || !Enum.IsDefined(method)
            || method == PaymentMethod.Mixed)
            throw new DomainException($"Forma de pagamento inválida: '{p.Method}'.");
        if (!Enum.TryParse<PaymentType>(p.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            throw new DomainException($"Tipo de pagamento inválido: '{p.Type}'.");
        if (p.Amount <= 0)
            throw new DomainException("Os valores de pagamento devem ser positivos.");
        if (decimal.Round(p.Amount, 2) != p.Amount)
            throw new DomainException("Valores de pagamento têm no máximo duas casas decimais.");
        if (type == PaymentType.Credit && p.DueDate is null)
            throw new DomainException("Pagamento a prazo exige data de vencimento.");

        return new ParsedLine(method, type, p.Amount, p.DueDate);
    }
}
