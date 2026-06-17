using System;
using Accounting.Api.Tests.Fixtures;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// Security-feature unit tests (no DB / no HTTP): the MFA AES key is no longer committed; it is
/// supplied at first-run via the git-ignored appsettings.Secrets.json. OtpNetTotpService now reads
/// the key LAZILY via IOptionsMonitor — so an unconfigured instance must boot fine, and MFA
/// *enrolment* must fail with a clear error instead of crashing or using an empty key.
/// </summary>
public sealed class InstanceMfaKeyTests
{
    private static OtpNetTotpService Service(string keyBase64) =>
        new(new StaticOptionsMonitor<MfaOptions>(new MfaOptions { MfaAesKeyBase64 = keyBase64 }));

    // 32 zero bytes → valid AES-256 key, base64-encoded.
    private static string ValidKey() => Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Construction_with_empty_key_does_not_throw()
    {
        // Boot must stay healthy when no key is configured yet (first-run, pre-setup).
        var act = () => Service("");
        act.Should().NotThrow();
    }

    [Fact]
    public void Encrypt_without_configured_key_throws_clear_error()
    {
        var svc = Service("");
        var act = () => svc.Encrypt("JBSWY3DPEHPK3PXP");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    [Fact]
    public void Encrypt_with_wrong_length_key_throws()
    {
        // 16 bytes (AES-128) is rejected — we require AES-256 (exactly 32 bytes).
        var svc = Service(Convert.ToBase64String(new byte[16]));
        var act = () => svc.Encrypt("JBSWY3DPEHPK3PXP");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void Encrypt_with_invalid_base64_throws()
    {
        var svc = Service("not-valid-base64!!!");
        var act = () => svc.Encrypt("JBSWY3DPEHPK3PXP");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*base64*");
    }

    [Fact]
    public void GenerateSecret_works_without_a_configured_key()
    {
        // Provisioning steps that don't touch the AES key must work pre-setup.
        var svc = Service("");
        var secret = svc.GenerateSecret();
        secret.Should().NotBeNullOrWhiteSpace();
        var uri = svc.BuildProvisioningUri(secret, "user@example.com", "TEAS");
        uri.Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public void Encrypt_then_Decrypt_roundtrips_with_a_valid_key()
    {
        var svc = Service(ValidKey());
        const string base32 = "JBSWY3DPEHPK3PXP";
        var cipher = svc.Encrypt(base32);
        svc.Decrypt(cipher).Should().Be(base32);
    }
}
