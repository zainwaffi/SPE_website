namespace SPE_website.Shared.Models;

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;

    /// <summary>Display name on the From header.</summary>
    public string FromName { get; set; } = "SPE Chapter";

    /// <summary>
    /// A monitored human address replies should go to. A "noreply" sender with no reply path is a
    /// spam signal at Microsoft in particular; leave blank to fall back to <see cref="From"/>.
    /// </summary>
    public string ReplyTo { get; set; } = string.Empty;
}
