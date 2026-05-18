namespace Accounting.Application.Abstractions;

public interface IPasswordHasher
{
    /// <summary>Returns the salted hash to persist in <c>users.password_hash</c>.</summary>
    string Hash(string plaintext);

    /// <summary>Constant-time comparison. Returns false on any malformed hash.</summary>
    bool Verify(string plaintext, string hash);
}
