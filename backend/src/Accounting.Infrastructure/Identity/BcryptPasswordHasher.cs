using Accounting.Application.Abstractions;

namespace Accounting.Infrastructure.Identity;

/// <summary>BCrypt-Next, work factor 12 — ~250 ms per hash on a modern server.</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plaintext) =>
        BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);

    public bool Verify(string plaintext, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(plaintext, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
