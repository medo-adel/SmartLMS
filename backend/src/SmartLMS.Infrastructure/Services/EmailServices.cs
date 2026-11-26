using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SmartLMS.Application.Interfaces;
using SmartLMS.Infrastructure.Identity;
using SmartLMS.Infrastructure.Persistence;
using SmartLMS.Infrastructure.Options;

namespace SmartLMS.Infrastructure.Services;

/// <summary>Default transport: sends nothing.</summary>
public sealed class DisabledEmailTransport(ILogger<DisabledEmailTransport> logger) : IEmailTransport
{
    public bool IsEnabled => false;
    public string DeliveryMode => "disabled";

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
    // A configured SMTP host always wins: the local outbox is a development fallback, never a silent override.
    public string DeliveryMode => string.IsNullOrWhiteSpace(options.Value.Host) && !string.IsNullOrWhiteSpace(options.Value.PickupDirectory) ? "local-outbox" : "mailbox";

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient();
        if (string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.PickupDirectory))
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

/// <summary>
/// Transactional and learning emails (welcome, enrollment, receipt, course completion). Every send is best-effort:
/// a mail failure never breaks the request that triggered it, and nothing is sent when the transport is disabled.
/// The language follows the recipient's PreferredLanguage (Arabic when it starts with "ar", English otherwise).
/// </summary>
public sealed class TransactionalMailer(ApplicationDbContext db, IEmailTransport transport, ILogger<TransactionalMailer> logger)
{
    public async Task WelcomeAsync(Guid userId, CancellationToken ct)
    {
        var u = await User(userId, ct); if (u is null) return;
        await Send(u,
            "Welcome to your academy",
            "أهلاً بيك في أكاديميتك",
            $"Hi {u.FullName},\n\nYour email is verified and your account is ready. Open the app to browse courses and start learning.",
            $"أهلاً {u.FullName},\n\nتم تفعيل بريدك وحسابك جاهز. افتح التطبيق وتصفح الكورسات وابدأ التعلم.", ct);
    }

    public async Task EnrollmentAsync(Guid userId, Guid courseId, CancellationToken ct)
    {
        var u = await User(userId, ct); if (u is null) return;
        var title = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.Title).FirstOrDefaultAsync(ct);
        if (title is null) return;
        await Send(u,
            $"You're enrolled: {title}",
            $"تم تسجيلك في: {title}",
            $"Hi {u.FullName},\n\nYour enrollment in \"{title}\" is active. You can start the first lesson now.",
            $"أهلاً {u.FullName},\n\nاشتراكك في \"{title}\" بقى فعال. تقدر تبدأ أول درس دلوقتي.", ct);
    }

    public async Task OrderReceiptAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var u = await User(userId, ct); if (u is null) return;
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct); if (o is null) return;
        var lines = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId).Select(i => i.Description).ToListAsync(ct);
        var itemsEn = string.Join("\n", lines.Select(l => "- " + l)); var itemsAr = itemsEn;
        await Send(u,
            $"Receipt for your order ({o.Total:0.00} {o.Currency})",
            $"إيصال طلبك ({o.Total:0.00} {o.Currency})",
            $"Hi {u.FullName},\n\nThanks for your purchase. Your order is paid in full.\n\n{itemsEn}\n\nTotal: {o.Total:0.00} {o.Currency}",
            $"أهلاً {u.FullName},\n\nشكراً لشرائك. تم دفع طلبك بالكامل.\n\n{itemsAr}\n\nالإجمالي: {o.Total:0.00} {o.Currency}", ct);
    }

    public async Task CourseCompletedAsync(Guid userId, Guid courseId, CancellationToken ct)
    {
        var u = await User(userId, ct); if (u is null) return;
        var title = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.Title).FirstOrDefaultAsync(ct);
        if (title is null) return;
        await Send(u,
            $"Congratulations on finishing {title}",
            $"مبروك! خلصت {title}",
            $"Hi {u.FullName},\n\nYou completed every lesson in \"{title}\". Your certificate is ready in the app.",
            $"أهلاً {u.FullName},\n\nخلصت كل دروس \"{title}\". شهادتك جاهزة في التطبيق.", ct);
    }

    /// <summary>Course approval decision to the owning instructor.</summary>
    public async Task ApprovalResultAsync(Guid userId, Guid courseId, bool approved, string? comment, CancellationToken ct)
    {
        var u = await User(userId, ct); if (u is null) return;
        var title = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.Title).FirstOrDefaultAsync(ct);
        if (title is null) return;
        var noteEn = string.IsNullOrWhiteSpace(comment) ? "" : $"\n\nReviewer note: {comment}";
        var noteAr = string.IsNullOrWhiteSpace(comment) ? "" : $"\n\nملاحظة المراجع: {comment}";
        await Send(u,
            approved ? $"Course approved: {title}" : $"Course needs changes: {title}",
            approved ? $"تم اعتماد كورسك: {title}" : $"كورسك يحتاج تعديلات: {title}",
            approved ? $"Hi {u.FullName},\n\n'{title}' passed review and is now published for students.{noteEn}" : $"Hi {u.FullName},\n\n'{title}' was reviewed and needs changes before it can be published.{noteEn}",
            approved ? $"أهلاً {u.FullName},\n\n'{title}' اجتاز المراجعة وبقى منشور للطلاب.{noteAr}" : $"أهلاً {u.FullName},\n\n'{title}' اتمراجع ويحتاج تعديلات قبل النشر.{noteAr}", ct);
    }

    /// <summary>Live-session reminder for background jobs: no tenant or user context exists, so the job passes the recipient fields it loaded itself.</summary>
    public async Task LiveSessionReminderAsync(string toEmail, string fullName, string? preferredLanguage, string sessionTitle, string courseTitle, DateTime startsAtUtc, CancellationToken ct)
    {
        var when = startsAtUtc.ToString("u");
        await SendRaw(toEmail, preferredLanguage,
            $"Starting soon: {sessionTitle}",
            $"يبدأ قريباً: {sessionTitle}",
            $"Hi {fullName},\n\nThe live session '{sessionTitle}' ({courseTitle}) starts at {when}. Open the app to join.",
            $"أهلاً {fullName},\n\nالبث المباشر '{sessionTitle}' ({courseTitle}) يبدأ {when}. افتح التطبيق للانضمام.", ct);
    }

    /// <summary>Sends when the transport is enabled; never throws. The language follows the recipient's preference.</summary>
    public async Task SendRaw(string toEmail, string? preferredLanguage, string enSubject, string arSubject, string enBody, string arBody, CancellationToken ct)
    {
        if (!transport.IsEnabled) return;
        var ar = preferredLanguage?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        try { await transport.SendAsync(new EmailMessage(toEmail, ar ? arSubject : enSubject, ar ? arBody : enBody), ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Transactional email to {Email} failed; the triggering action is unaffected.", toEmail); }
    }

    private async Task<ApplicationUser?> User(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Email != null, ct);

    private async Task Send(ApplicationUser u, string enSubject, string arSubject, string enBody, string arBody, CancellationToken ct)
    {
        if (u.Email is null) return;
        await SendRaw(u.Email, u.PreferredLanguage, enSubject, arSubject, enBody, arBody, ct);
    }
}
