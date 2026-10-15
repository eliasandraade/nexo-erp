namespace Nexo.Application.Common;

/// <summary>
/// Check digits and formats of Brazilian fiscal identifiers (CPF, CNPJ — numeric and the
/// alphanumeric one issued since July 2026 —, GTIN/EAN, NCM, CEST, código IBGE). Pure functions;
/// masks (dots, dashes, slashes) are ignored.
/// </summary>
public static class FiscalCodes
{
    /// <summary>IBGE state codes (first two digits of a city code) by UF.</summary>
    public static readonly IReadOnlyDictionary<string, int> IbgeStateCodes = new Dictionary<string, int>
    {
        ["RO"] = 11, ["AC"] = 12, ["AM"] = 13, ["RR"] = 14, ["PA"] = 15, ["AP"] = 16, ["TO"] = 17,
        ["MA"] = 21, ["PI"] = 22, ["CE"] = 23, ["RN"] = 24, ["PB"] = 25, ["PE"] = 26, ["AL"] = 27, ["SE"] = 28, ["BA"] = 29,
        ["MG"] = 31, ["ES"] = 32, ["RJ"] = 33, ["SP"] = 35,
        ["PR"] = 41, ["SC"] = 42, ["RS"] = 43,
        ["MS"] = 50, ["MT"] = 51, ["GO"] = 52, ["DF"] = 53,
    };

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Letters and digits only, upper case — how a CPF/CNPJ is stored (the new CNPJ has letters).</summary>
    public static string Document(string? value)
        => new((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsCpf(string? value)
    {
        var d = Document(value);
        if (d.Length != 11 || !d.All(char.IsAsciiDigit) || AllSame(d)) return false;
        return d[9] - '0' == Mod11(d, 9, 10) && d[10] - '0' == Mod11(d, 10, 11);

        static int Mod11(string d, int count, int startWeight)
        {
            var sum = 0;
            for (var i = 0; i < count; i++) sum += (d[i] - '0') * (startWeight - i);
            var r = sum * 10 % 11;
            return r == 10 ? 0 : r;
        }
    }

    /// <summary>
    /// CNPJ, numeric or alphanumeric (12 letters/digits + 2 numeric check digits). Each character
    /// counts as its ASCII code minus 48 — the Receita rule, which keeps the old numeric CNPJ valid.
    /// </summary>
    public static bool IsCnpj(string? value)
    {
        var d = Document(value);
        if (d.Length != 14 || !char.IsAsciiDigit(d[12]) || !char.IsAsciiDigit(d[13]) || AllSame(d)) return false;
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

    /// <summary>
    /// GTIN-8/12/13/14 (EAN/UPC) with a valid check digit — the "código de barras" of the NF-e.
    /// In-store codes (GTIN-13 starting with 2, e.g. scale labels) and all-zero codes are not GTINs:
    /// on the nota they go as "SEM GTIN".
    /// </summary>
    public static bool IsGtin(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length is not (8 or 12 or 13 or 14) || !raw.All(char.IsAsciiDigit)) return false;
        if (raw.All(c => c == '0') || (raw.Length == 13 && raw[0] == '2')) return false;
        var sum = 0;
        for (var i = raw.Length - 2; i >= 0; i--)
            sum += (raw[i] - '0') * ((raw.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == raw[^1] - '0';
    }

    /// <summary>NCM: 8 digits (the table itself is not checked — it changes and comes from the Receita).</summary>
    public static bool IsNcm(string? value) => Exactly(value, 8);

    /// <summary>CEST: 7 digits.</summary>
    public static bool IsCest(string? value) => Exactly(value, 7);

    /// <summary>Código IBGE do município: 7 digits, starting with a valid state code.</summary>
    public static bool IsIbgeCity(string? value)
        => Exactly(value, 7) && IbgeStateCodes.Values.Contains(int.Parse(value!.Trim()[..2]));

    /// <summary>The city code belongs to the UF (3550308 is in SP, not in RJ).</summary>
    public static bool IbgeCityInState(string? cityCode, string? state)
        => IsIbgeCity(cityCode)
           && IbgeStateCodes.TryGetValue((state ?? string.Empty).Trim().ToUpperInvariant(), out var code)
           && int.Parse(cityCode!.Trim()[..2]) == code;

    private static bool Exactly(string? value, int length)
    {
        var raw = (value ?? string.Empty).Trim();
        return raw.Length == length && raw.All(char.IsAsciiDigit);
    }

    private static bool AllSame(string d) => d.All(c => c == d[0]);
}
