using System.Security.Cryptography;
using System.Text;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;
using OtpNet;

namespace Accounting.Infrastructure.Identity;

public sealed class MfaOptions
{
    /// <summary>Base64-encoded 32-byte AES key. Generated once and stored in .env. NEVER rotate without re-enrolling all users.</summary>
    public required string MfaAesKeyBase64 { get; init; }

    public string Issuer { get; init; } = "TEAS";
}

/// <summary>
/// TOTP backed by Otp.NET. Shared secrets are stored AES-256-GCM-encrypted in <c>users.mfa_secret_enc</c>.
/// </summary>
public sealed class OtpNetTotpService : ITotpService
{
    private readonly byte[] _aesKey;
    private readonly string _issuer;

    public OtpNetTotpService(IOptions<MfaOptions> options)
    {
        var opts = options.Value;
        _aesKey = Convert.FromBase64String(opts.MfaAesKeyBase64);
        if (_aesKey.Length != 32)
            throw new InvalidOperationException("Mfa:MfaAesKeyBase64 must decode to exactly 32 bytes (AES-256 key).");
        _issuer = opts.Issuer;
    }

    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(20); // 160-bit per RFC 4226 §4
        return Base32Encoding.ToString(bytes);
    }

    public byte[] Encrypt(string base32Secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(base32Secret);
        var nonce  = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag    = new byte[16];

        using var aes = new AesGcm(_aesKey, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        // Layout: nonce(12) | tag(16) | cipher(N)
        var output = new byte[12 + 16 + cipher.Length];
        Buffer.BlockCopy(nonce,  0, output,  0,  12);
        Buffer.BlockCopy(tag,    0, output, 12, 16);
        Buffer.BlockCopy(cipher, 0, output, 28, cipher.Length);
        return output;
    }

    public string Decrypt(byte[] cipherText)
    {
        if (cipherText.Length < 28)
            throw new CryptographicException("Encrypted MFA secret too short.");

        var nonce  = cipherText.AsSpan(0, 12);
        var tag    = cipherText.AsSpan(12, 16);
        var cipher = cipherText.AsSpan(28);
        var plain  = new byte[cipher.Length];

        using var aes = new AesGcm(_aesKey, tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public bool Verify(byte[] encryptedSecret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var base32 = Decrypt(encryptedSecret);
        var secret = Base32Encoding.ToBytes(base32);
        var totp   = new Totp(secret);
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public string BuildProvisioningUri(string base32Secret, string accountName, string issuer)
    {
        var iss = Uri.EscapeDataString(issuer);
        var acc = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{iss}:{acc}?secret={base32Secret}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }
}
