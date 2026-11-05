using Nexo.Domain.Exceptions;

namespace Nexo.Application.Common;

/// <summary>
/// Reads an enum value sent by the client. An unknown value is the caller's mistake: a <see cref="DomainException"/>
/// (422) that lists what is valid, never the ArgumentException of Enum.Parse (500). For values read from the database,
/// where an unknown one really is a server failure, keep Enum.Parse.
/// </summary>
public static class EnumInput
{
    /// <param name="value">What the client sent.</param>
    /// <param name="problem">The start of the message, e.g. "Tipo de conta inválido".</param>
    public static TEnum Parse<TEnum>(string? value, string problem) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            // TryParse also accepts "1" and "A,B", which would land on a member nobody asked for
            && string.Equals(parsed.ToString(), value!.Trim(), StringComparison.OrdinalIgnoreCase))
            return parsed;

        throw new DomainException($"{problem}. Use um destes valores: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }
}
