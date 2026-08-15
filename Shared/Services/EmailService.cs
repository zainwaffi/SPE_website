using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;
using SPE_website.Shared.Models;

namespace SPE_website.Shared.Services;

/// <summary>
/// Thin MailKit/SMTP wrapper for outbound HTML notification emails (strikes, task
/// assignments). Never throws: every failure path — unconfigured SMTP, a missing or
/// malformed recipient address, a rejected send — comes back as a failed
/// <see cref="EmailResult"/> so the caller can report it instead of the notification
/// disappearing silently.
/// </summary>
public class EmailService(IOptions<EmailSettings> opts, ILogger<EmailService> logger)
{
    private readonly EmailSettings _settings = opts.Value;

    /// <summary>SMTP operations hang rather than fail fast on a blocked port; cap them so a Blazor click never wedges.</summary>
    private const int SmtpTimeoutMs = 20_000;

    public async Task<EmailResult> SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
        {
            logger.LogWarning("Email not configured — skipping send to {Email}", toEmail);
            return EmailResult.Failure("Email is not configured on the server (no SMTP host), so no notification was sent.");
        }

        if (string.IsNullOrWhiteSpace(toEmail) || !MailboxAddress.TryParse(toEmail, out var recipient))
        {
            logger.LogWarning("Cannot send email — recipient address is missing or invalid: {Email}", toEmail);
            return EmailResult.Failure("The member has no valid email address on record, so no notification was sent.");
        }

        recipient.Name = toName;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.From));
        message.To.Add(recipient);
        message.Subject = subject;

        // MimeKit defaults the Message-ID domain to the local machine's hostname, which on a
        // container or VM is meaningless and reads as a spam signal. Pin it to the From domain.
        // (Gmail's submission service rewrites Message-ID anyway; this only pays off once mail
        // goes out through a provider on a domain we control.)
        var fromDomain = _settings.From.Split('@').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(fromDomain))
            message.MessageId = MimeUtils.GenerateMessageId(fromDomain);

        // Give recipients somewhere to reply. Filters weight a reachable reply path, and a member
        // questioning a strike should not be answering into a black hole.
        var replyTo = string.IsNullOrWhiteSpace(_settings.ReplyTo) ? _settings.From : _settings.ReplyTo;
        if (MailboxAddress.TryParse(replyTo, out var replyToAddress))
            message.ReplyTo.Add(replyToAddress);

        // Send multipart/alternative rather than HTML alone — a text-less HTML mail from a bulk
        // sender is a strong spam signal, and these notices were landing in spam.
        message.Body = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = ToPlainText(htmlBody)
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient { Timeout = SmtpTimeoutMs };
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls);

            if (!string.IsNullOrWhiteSpace(_settings.Username))
                await client.AuthenticateAsync(_settings.Username, _settings.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            logger.LogInformation("Sent \"{Subject}\" to {Email}", subject, toEmail);
            return EmailResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send \"{Subject}\" to {Email}", subject, toEmail);
            return EmailResult.Failure(ex.Message);
        }
    }

    /// <summary>Crude HTML-to-text for the plain-text alternative part: block tags become line breaks, the rest are dropped.</summary>
    private static string ToPlainText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<(br|/p|/div|/h[1-6]|/li)[^>]*>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Carry link targets into the text part. Dropping them leaves the two alternatives saying
        // different things, which filters read as a cloaking attempt.
        text = System.Text.RegularExpressions.Regex.Replace(text, """<a\b[^>]*\bhref\s*=\s*["']([^"']+)["'][^>]*>(.*?)</a>""",
            "$2 ($1)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return string.Join("\n", text.Split('\n').Select(l => l.Trim())).Trim();
    }
}
