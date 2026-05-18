using System.Security.Cryptography;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 14 — mints external API keys. Plaintext is shown ONCE (Stripe
/// pattern); only the bcrypt hash + a deterministic lookup prefix are stored.
/// </summary>
public static class ApiKeyGenerator
{
    private const string Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Leading chars stored as <c>KeyPrefix</c> — lookup + UI display.</summary>
    public const int PrefixLength = 16;

    public sealed record Minted(string Plaintext, string KeyPrefix, string KeyHash);

    public static Minted New()
    {
        Span<byte> buf = stackalloc byte[40];
        RandomNumberGenerator.Fill(buf);
        var sb = new System.Text.StringBuilder("key_", 44);
        foreach (var b in buf) sb.Append(Alphabet[b % Alphabet.Length]);
        var plaintext = sb.ToString();                       // key_ + 40 chars
        var prefix = plaintext[..PrefixLength];              // deterministic, unique enough
        var hash = BCrypt.Net.BCrypt.HashPassword(plaintext);
        return new Minted(plaintext, prefix, hash);
    }

    /// <summary>The deterministic lookup prefix for a presented key.</summary>
    public static string? PrefixOf(string presentedKey) =>
        presentedKey is { Length: >= PrefixLength } && presentedKey.StartsWith("key_", StringComparison.Ordinal)
            ? presentedKey[..PrefixLength]
            : null;
}
