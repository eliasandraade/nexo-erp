using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// BCrypt-backed password hasher.
/// Work factor 12 is the production default — provides ~250ms on modern hardware.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
            throw new ArgumentException("Password cannot be empty.", nameof(plainPassword));

        return BCrypt.Net.BCrypt.HashPassword(plainPassword, WorkFactor);
    }

    public bool Verify(string plainPassword, string hash)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(plainPassword, hash);
        }
        catch
        {
            return false;
        }
    }
}
