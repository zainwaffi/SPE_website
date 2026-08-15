namespace SPE_website.Shared.Models;

/// <summary>
/// Outcome of an attempted notification email. <see cref="Services.EmailService"/> never throws,
/// so callers use this to tell the difference between "delivered", "email is switched off in
/// config", and "the SMTP server rejected it" — and can surface that difference in the UI.
/// </summary>
public record EmailResult(bool Sent, string? Error)
{
    public static EmailResult Success() => new(true, null);
    public static EmailResult Failure(string error) => new(false, error);
}
