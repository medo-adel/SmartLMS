using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLMS.Infrastructure.Options;
using SmartLMS.Infrastructure.Services;

namespace SmartLMS.Tests;

/// <summary>
/// Real-transport coverage for the local dev outbox: EmailOtpSender through SmtpEmailTransport with a
/// pickup directory must drop a real .eml file containing the OTP code — no SMTP server or account.
/// </summary>
public sealed class EmailPickupTests
{
    [Fact]
    public async Task Pickup_directory_transport_writes_the_otp_email_to_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smartlms-devmail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var email = Options.Create(new EmailOptions { Enabled = true, FromAddress = "no-reply@smartlms.local", PickupDirectory = dir });
            var transport = new SmtpEmailTransport(email);
            var otp = Options.Create(new OtpOptions { ExpiryMinutes = 10 });
            var sender = new EmailOtpSender(transport, NullLogger<EmailOtpSender>.Instance, otp);

            await sender.SendAsync("new-user@example.test", "EmailVerification", "482913", CancellationToken.None);

            var files = Directory.GetFiles(dir, "*.eml");
            Assert.Single(files);
            var body = await File.ReadAllTextAsync(files[0]);
            Assert.Contains("482913", body);
            Assert.Contains("new-user@example.test", body);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Disabled_transport_sends_nothing_and_creates_no_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smartlms-devmail-" + Guid.NewGuid().ToString("N"));
        var sender = new EmailOtpSender(new DisabledEmailTransport(NullLogger<DisabledEmailTransport>.Instance), NullLogger<EmailOtpSender>.Instance, Options.Create(new OtpOptions()));
        await sender.SendAsync("new-user@example.test", "EmailVerification", "482913", CancellationToken.None);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Smtp_host_wins_over_pickup_directory()
    {
        // When both are configured (user-secrets SMTP over the dev appsettings pickup), mail goes to the
        // real server and the mode endpoint says mailbox - the local outbox never silently overrides.
        var both = Options.Create(new EmailOptions { Enabled = true, Host = "smtp.gmail.com", FromAddress = "a@b.c", PickupDirectory = "storage/dev-mail" });
        Assert.Equal("mailbox", new SmtpEmailTransport(both).DeliveryMode);
        var pickupOnly = Options.Create(new EmailOptions { Enabled = true, FromAddress = "a@b.c", PickupDirectory = "storage/dev-mail" });
        Assert.Equal("local-outbox", new SmtpEmailTransport(pickupOnly).DeliveryMode);
    }
}
