using System.Globalization;
using System.Text;
using Nexo.Application.Common;
using Nexo.Domain.Enums;

namespace Nexo.Infrastructure.Reports;

/// <summary>
/// Sales as a spreadsheet the way a Brazilian Excel opens it with a double-click: UTF-8 with BOM,
/// ";" between columns, decimal comma, Brazil local time. Cells that a spreadsheet would read as a
/// formula (=, +, -, @) are prefixed with an apostrophe — a customer named "=HYPERLINK(...)" stays text.
/// </summary>
public static class SalesCsv
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly string[] Header =
        ["Número", "Data", "Situação", "Cliente", "Vendedor", "Subtotal", "Desconto", "Total", "Devolvido", "Líquido", "Pagamento"];

    public static async Task WriteAsync(Stream output, IAsyncEnumerable<SaleExportRow> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 16 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(Line(Header));
        await foreach (var r in rows.WithCancellation(ct))
            await writer.WriteLineAsync(Line(Row(r)));
        await writer.FlushAsync(ct);
    }

    public static string[] Row(SaleExportRow r) =>
    [
        r.Number.ToString(CultureInfo.InvariantCulture),
        BusinessClock.ToBrazil(r.At).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        StatusLabel(r.Status),
        Text(r.Customer),
        Text(r.Seller),
        Money(r.Subtotal),
        Money(r.Discount),
        Money(r.Total),
        Money(r.Returned),
        Money(r.Status == SaleStatus.Cancelled ? 0 : r.Total - r.Returned),
        string.Join(" + ", r.Methods.Distinct().Select(MethodLabel)),
    ];

    public static string Line(IEnumerable<string> cells) => string.Join(';', cells.Select(Cell));

    private static string Money(decimal v) => v.ToString("0.00", PtBr);

    /// <summary>Free text typed by someone (names): never let a spreadsheet run it as a formula.</summary>
    private static string Text(string? value)
    {
        var v = value ?? "";
        return v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + v : v;
    }

    private static string Cell(string v)
        => v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    public static string StatusLabel(SaleStatus s) => s switch
    {
        SaleStatus.Confirmed => "Confirmada",
        SaleStatus.Paid => "Paga",
        SaleStatus.Cancelled => "Cancelada",
        SaleStatus.PartiallyReturned => "Devolvida em parte",
        SaleStatus.Returned => "Devolvida",
        _ => "Rascunho",
    };

    public static string MethodLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "Dinheiro",
        PaymentMethod.Debit => "Cartão de débito",
        PaymentMethod.Credit => "Cartão de crédito",
        PaymentMethod.Pix => "Pix",
        PaymentMethod.Transfer => "Transferência",
        PaymentMethod.Check => "Cheque",
        PaymentMethod.Mixed => "Misto",
        _ => "Outro",
    };
}
