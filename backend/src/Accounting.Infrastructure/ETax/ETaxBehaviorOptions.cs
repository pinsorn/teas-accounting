namespace Accounting.Infrastructure.ETax;

/// <summary>
/// Runtime switches for the e-Tax submission pipeline. Bound from <c>ETax</c> section
/// in appsettings — disabled by default, so unit/dev environments do not attempt to
/// sign or send emails unless explicitly opted in.
/// </summary>
public sealed class ETaxBehaviorOptions
{
    /// <summary>Master switch — when false, the auto-trigger is skipped silently.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>When true, <c>TaxInvoiceService.PostAsync</c> auto-builds, signs, and emails XML.</summary>
    public bool AutoSendOnTaxInvoicePost { get; init; } = false;
}
