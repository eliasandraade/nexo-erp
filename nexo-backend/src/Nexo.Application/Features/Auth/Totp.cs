using System.Security.Cryptography;
using System.Text;

namespace Nexo.Application.Features.Auth;

/// <summary>
/// Time-based one-time passwords (RFC 6238 over RFC 4226: HMAC-SHA1, 30 s steps, 6 digits) — what
/// Google Authenticator, Microsoft Authenticator, 1Password, Authy etc. generate. Pure functions.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    /// <summary>Steps accepted on each side of now (clock drift of the phone).</summary>
    public const int Drift = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new 160-bit secret (the size RFC 4226 recommends for SHA-1).</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        // HMAC-SHA1 is what RFC 6238 specifies and the only algorithm every authenticator app honours;
        // as an HMAC (not a bare digest) it carries none of SHA-1's collision weakness.
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, secret);
        hmac.AppendData(counter);
        var hash = hmac.GetHashAndReset();
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// The step the code belongs to (now ± drift), or null. Codes at or before
    /// <paramref name="lastUsedStep"/> are refused — a code works once.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTime nowUtc, long? lastUsedStep)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits) return null;
        var now = StepAt(nowUtc);
        for (var d = -Drift; d <= Drift; d++)
        {
            var step = now + d;
            if (lastUsedStep is { } used && step <= used) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)))
                return step;
        }
        return null;
    }

    public static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>otpauth:// link the authenticator apps read (QR code or tap on the phone).</summary>
    public static string OtpAuthUri(string issuer, string account, byte[] secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
           $"?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={(int)Step.TotalSeconds}";
}
