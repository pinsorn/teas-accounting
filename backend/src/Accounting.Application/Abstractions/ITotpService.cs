namespace Accounting.Application.Abstractions;

/// <summary>RFC 6238 TOTP — issuance, encrypted storage, verification.</summary>
public interface ITotpService
{
    /// <summary>Generate a fresh shared secret as a Base32 string for QR provisioning.</summary>
    string GenerateSecret();

    /// <summary>Encrypt a Base32 secret for storage in <c>users.mfa_secret_enc</c>.</summary>
    byte[] Encrypt(string base32Secret);

    /// <summary>Decrypt the stored ciphertext back to a Base32 secret.</summary>
    string Decrypt(byte[] cipherText);

    /// <summary>Verify a 6-digit code against the encrypted secret. Accepts ±1 step skew.</summary>
    bool Verify(byte[] encryptedSecret, string code);

    /// <summary>Build the otpauth:// URI for the QR code (RFC 6238 §3 / Google Authenticator).</summary>
    string BuildProvisioningUri(string base32Secret, string accountName, string issuer);
}
