using Accounting.Application.Abstractions;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.ETax;

public sealed class ETaxEmailOptions
{
    public required string SmtpHost { get; init; }
    public int    SmtpPort     { get; init; } = 1025;
    public string? Username    { get; init; }
    public string? Password    { get; init; }
    public required string FromEmail   { get; init; }
    public string  RdCcAddress { get; init; } = "csemail@rd.go.th";
    public bool    UseSsl      { get; init; } = false;

    // Sprint 13c — Tier 2 safety. When RedirectAllToEmail is set, every send
    // (To + Cc) is diverted there (Tier 1 dev inbox / Tier 2 UAT mailbox);
    // production = null → real customer + RD cc. WhitelistDomains is the
    // alternative guard: only recipients in these domains may be emailed.
    public string?   RedirectAllToEmail { get; init; }
    public string[]? WhitelistDomains   { get; init; }
}

/// <summary>
/// Sends the signed e-Tax XML as an email attachment with <c>csemail@rd.go.th</c> in CC —
/// this matches the official RD e-Tax-by-Email protocol (customer + RD see the same message simultaneously).
/// </summary>
public sealed class ETaxEmailSender : IETaxEmailSender
{
    private readonly ETaxEmailOptions _opts;
    public ETaxEmailSender(IOptions<ETaxEmailOptions> opts) => _opts = opts.Value;

    public async Task<ETaxDeliveryResult> SendAsync(
        string toEmail, string subject, ETaxSignedDocument xml, byte[]? pdfA3, CancellationToken ct)
    {
        // Tier-2 safety guard: divert (RedirectAllToEmail) and/or enforce the
        // domain whitelist BEFORE building the message. A whitelist violation
        // throws — the submission pipeline catches it and records 'SendFailed'.
        var r = ETaxRecipientResolver.Resolve(toEmail, _opts.RdCcAddress, _opts.RedirectAllToEmail);
        ETaxRecipientResolver.EnsureWhitelisted(r.To, _opts.WhitelistDomains);

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(_opts.FromEmail));
        msg.To.Add(MailboxAddress.Parse(r.To));
        msg.Cc.Add(MailboxAddress.Parse(r.Cc));
        msg.Subject = subject;

        var body = new BodyBuilder
        {
            TextBody = $"e-Tax Invoice ตามไฟล์แนบ\nSHA-256: {xml.Sha256}\n\n— TEAS",
        };
        body.Attachments.Add("etax.xml", xml.Bytes, ContentType.Parse("application/xml"));
        if (pdfA3 is { Length: > 0 })
            body.Attachments.Add("etax.pdf", pdfA3, ContentType.Parse("application/pdf"));
        msg.Body = body.ToMessageBody();

        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort,
                _opts.UseSsl ? MailKit.Security.SecureSocketOptions.StartTls
                             : MailKit.Security.SecureSocketOptions.None, ct);
            if (!string.IsNullOrEmpty(_opts.Username))
                await smtp.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct);

            await smtp.SendAsync(msg, ct);
            await smtp.DisconnectAsync(true, ct);

            return new ETaxDeliveryResult(true, DateTimeOffset.UtcNow, msg.MessageId ?? string.Empty,
                null, r.To, r.Cc, r.Redirected);
        }
        catch (Exception ex)
        {
            return new ETaxDeliveryResult(false, DateTimeOffset.UtcNow, msg.MessageId ?? string.Empty,
                ex.Message, r.To, r.Cc, r.Redirected);
        }
    }
}
