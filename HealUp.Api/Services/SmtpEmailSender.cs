using System.Net;
using System.Net.Mail;

namespace HealUp.Api.Services;

public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
{
    public async Task<bool> TrySendAsync(string toAddress, string subject, string plainBody, CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection("Smtp");
        if (!section.GetValue("Enabled", false))
            return false;

        var host = section["SmtpServer"];
        var port = section.GetValue("Port", 587);
        var user = section["Username"];
        var pass = section["Password"];
        var fromEmail = section["SenderEmail"];
        var fromName = section["SenderName"] ?? "HealUp";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            logger.LogWarning("HealUp SMTP: missing host, sender, username or password.");
            return false;
        }

        using var msg = new MailMessage
        {
            From = new MailAddress(fromEmail.Trim(), fromName.Trim()),
            Subject = subject,
            Body = plainBody,
            IsBodyHtml = false,
        };
        msg.To.Add(toAddress.Trim());

        using var client = new SmtpClient(host.Trim(), port)
        {
            EnableSsl = section.GetValue("UseStartTls", true),
            Credentials = new NetworkCredential(user.Trim(), pass),
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        try
        {
            await client.SendMailAsync(msg, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HealUp SMTP send failed to {To}", toAddress);
            return false;
        }
    }
}
