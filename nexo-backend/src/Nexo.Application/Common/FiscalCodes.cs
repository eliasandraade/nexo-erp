namespace Nexo.Application.Common;

/// <summary>
/// Check digits and formats of Brazilian fiscal identifiers (CPF, CNPJ, GTIN/EAN, NCM, CEST, código IBGE).
/// Pure functions on digits only — masks (dots, dashes, slashes) are ignored.
/// </summary>
public static class FiscalCodes
{
    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static bool IsCpf(string? value)
    {
        var d = Digits(value);
        if (d.Length != 11 || AllSame(d)) return false;
        return d[9] - '0' == Mod11(d, 9, 10) && d[10] - '0' == Mod11(d, 10, 11);

        static int Mod11(string d, int count, int startWeight)
        {
            var sum = 0;
            for (var i = 0; i < count; i++) sum += (d[i] - '0') * (startWeight - i);
            var r = sum * 10 % 11;
            return r == 10 ? 0 : r;
        }
    }

    public static bool IsCnpj(string? value)
    {
        var d = Digits(value);
        if (d.Length != 14 || AllSame(d)) return false;
        return d[12] - '0' == Check(d, 12) && d[13] - '0' == Check(d, 13);

        static int Check(string d, int count)
        {
            var sum = 0;
            var weight = count - 7;   // 12 → 5,4,3,2,9..2 ; 13 → 6,5,4,3,2,9..2
            for (var i = 0; i < count; i++)
            {
                sum += (d[i] - '0') * weight;
                weight = weight == 2 ? 9 : weight - 1;
            }
            var r = sum % 11;
            return r < 2 ? 0 : 11 - r;
        }
    }

    /// <summary>GTIN-8/12/13/14 (EAN/UPC) with a valid check digit — the "código de barras" of the NF-e.</summary>
    public static bool IsGtin(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length is not (8 or 12 or 13 or 14) || !raw.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = raw.Length - 2; i >= 0; i--)
            sum += (raw[i] - '0') * ((raw.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == raw[^1] - '0';
    }

    /// <summary>NCM: 8 digits (the table itself is not checked — it changes and comes from the Receita).</summary>
    public static bool IsNcm(string? value) => Exactly(value, 8);

    /// <summary>CEST: 7 digits.</summary>
    public static bool IsCest(string? value) => Exactly(value, 7);

    /// <summary>Código IBGE do município: 7 digits, starting with a valid state code (11–53).</summary>
    public static bool IsIbgeCity(string? value)
        => Exactly(value, 7) && int.Parse(value!.Trim()[..2]) is >= 11 and <= 53;

    private static bool Exactly(string? value, int length)
    {
        var raw = (value ?? string.Empty).Trim();
        return raw.Length == length && raw.All(char.IsAsciiDigit);
    }

    private static bool AllSame(string d) => d.All(c => c == d[0]);
}
