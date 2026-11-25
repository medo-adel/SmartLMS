using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Options;

namespace SmartLMS.Infrastructure.Services;

/// <summary>Default transport: sends nothing.</summary>
public sealed class DisabledEmailTransport(ILogger<DisabledEmailTransport> logger) : IEmailTransport
{
    public bool IsEnabled => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email sending is disabled (Email:Enabled=false). A message was not sent.");
        return Task.CompletedTask;
    }
}

/// <summary>SMTP transport. Used only when Email:Enabled is true with a host and from-address. Not exercised against a real server in this project.</summary>
public sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public bool IsEnabled => true;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(o.PickupDirectory))
        {
            // Local development outbox: real .eml files, no server. The directory is created if missing.
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = Path.GetFullPath(o.PickupDirectory);
            Directory.CreateDirectory(client.PickupDirectoryLocation);
        }
        else
        {
            client.Host = o.Host; client.Port = o.Port; client.EnableSsl = o.UseSsl; client.DeliveryMethod = SmtpDeliveryMethod.Network;
            if (!string.IsNullOrEmpty(o.UserName)) client.Credentials = new NetworkCredential(o.UserName, o.Password);
        }
        using var mail = new MailMessage(new MailAddress(o.FromAddress, o.FromName), new MailAddress(message.To)) { Subject = message.Subject, Body = message.Body, IsBodyHtml = false };
        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>Turns an OTP into an email. Phone destinations are skipped because no SMS provider exists. The code is logged only when Otp:LogCodes is true (Development).</summary>
public sealed class EmailOtpSender(IEmailTransport transport, ILogger<EmailOtpSender> logger, IOptions<OtpOptions> options) : IOtpSender
{
    public async Task SendAsync(string destination, string purpose, string code, CancellationToken ct)
    {
        if (options.Value.LogCodes) logger.LogWarning("DEV OTP for {Destination} ({Purpose}): {Code}", destination, purpose, code);
        if (!destination.Contains('@'))
        {
            logger.LogWarning("OTP for a phone destination was not delivered: no SMS provider is configured.");
            return;
        }
        var minutes = options.Value.ExpiryMinutes;
        var subject = purpose == "PasswordReset" ? "Your SmartLMS password reset code" : "Your SmartLMS verification code";
        var body = $"Your code is {code}. It expires in {minutes} minutes. If you did not ask for it, ignore this email.";
        await transport.SendAsync(new EmailMessage(destination, subject, body), ct);
    }
}
