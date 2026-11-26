namespace SmartLMS.Tests;

/// <summary>Google social sign-in through the real API with a hermetic verifier (fake id tokens).</summary>
public sealed class SocialLoginTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private readonly HttpClient _c;
    private const string PW = ApiFactory.Password;
    private static string Fp(string s) => "fingerprint-" + s + "-0123456789";
    private static string U() => Guid.NewGuid().ToString("N")[..8];
    private static object Device(string fp) => new { fingerprint = Fp(fp), deviceType = "Mobile", platform = "test", deviceName = "t" };
    public SocialLoginTests(ApiFactory f) { _f = f; _c = f.CreateClient(); }

    private async Task<string> NewTenant()
    {
        var sa = (await _c.Post("/auth/login", new { email = ApiFactory.SaEmail, password = PW, device = Device("sa") })).Data.GetProperty("accessToken").GetString()!;
        var slug = "t" + U();
        var r = await _c.Post("/tenants", new { name = slug, slug, adminFullName = "Admin", adminEmail = $"admin@{slug}.test", adminPassword = PW, maxMobileDevices = 5, maxWebDevices = 5 }, sa, null, Fp("sa"));
        Assert.Equal(201, r.Status);
        return slug;
    }

    [Fact]
    public async Task Google_sign_in_creates_links_and_rejects_bad_tokens()
    {
        var slug = await NewTenant();
        // providers probe lists google with the configured client id (the login page keys off this)
        var prov = await _c.Get("/auth/external-providers", tenant: slug);
        Assert.True(prov.Success, prov.Body.ToString());
        var g = Assert.Single(prov.Data.GetProperty("providers").EnumerateArray());
        Assert.Equal("google", g.GetProperty("provider").GetString()); Assert.Equal("test-google-client-id", g.GetProperty("clientId").GetString());

        // first sign-in creates a confirmed Student account and returns working tokens
        var r1 = await _c.Post("/auth/social/google", new { idToken = "valid-google-token", device = Device("g1") }, tenant: slug);
        Assert.True(r1.Success, r1.Body.ToString());
        var uid = r1.Data.GetProperty("user").GetProperty("id").GetString();
        var token = r1.Data.GetProperty("accessToken").GetString()!;
        Assert.True((await _c.Get("/wishlist", token, slug, Fp("g1"))).Success);

        // second sign-in returns the same account (linked, not duplicated)
        var r2 = await _c.Post("/auth/social/google", new { idToken = "valid-google-token", device = Device("g1") }, tenant: slug);
        Assert.Equal(uid, r2.Data.GetProperty("user").GetProperty("id").GetString());

        // an existing password account with the same email gets the Google login linked
        var mail = "social@mail.test";
        var slug2 = await NewTenant();
        await _c.Post("/auth/register", new { fullName = "Social User", email = mail, password = PW }, tenant: slug2);
        await _c.Post("/auth/verify-email", new { email = mail, code = _f.Otp.Last(mail, "EmailVerification") }, tenant: slug2);
        var r3 = await _c.Post("/auth/social/google", new { idToken = "second-google-token", device = Device("g2") }, tenant: slug2);
        Assert.True(r3.Success, r3.Body.ToString());
        // and the password path still works for the same account
        Assert.True((await _c.Post("/auth/login", new { email = mail, password = PW, device = Device("g2") }, tenant: slug2)).Success);
        // the SAME Google account is allowed in the other academy: accounts are per-tenant rows, so it links
        // to that tenant's account (same id as r3), distinct from the account in the first academy
        var r4 = await _c.Post("/auth/social/google", new { idToken = "valid-google-token", device = Device("g2") }, tenant: slug2);
        Assert.True(r4.Success, r4.Body.ToString());
        Assert.Equal(r3.Data.GetProperty("user").GetProperty("id").GetString(), r4.Data.GetProperty("user").GetProperty("id").GetString());
        Assert.NotEqual(uid, r4.Data.GetProperty("user").GetProperty("id").GetString());

        // privileged accounts never get a social login linked by email match
        var adminMail = $"admin@{slug2}.test";
        _f.Google.Tokens["admin-google-token"] = new SmartLMS.Application.Interfaces.ExternalIdentity("google", "g-sub-9", adminMail, "Admin", true);
        var r5 = await _c.Post("/auth/social/google", new { idToken = "admin-google-token", device = Device("g2") }, tenant: slug2);
        Assert.Equal(403, r5.Status);
        Assert.Equal("SOCIAL_LINK_NOT_ALLOWED", r5.Body.GetProperty("code").GetString());

        // an existing password account whose email was never verified gets linked AND confirmed by provider proof
        var slug3 = await NewTenant();
        _f.Google.Tokens["nov-google-token"] = new SmartLMS.Application.Interfaces.ExternalIdentity("google", "g-sub-10", "nov@mail.test", "No Verify Yet", true);
        await _c.Post("/auth/register", new { fullName = "No Verify Yet", email = "nov@mail.test", password = PW }, tenant: slug3);
        var r6 = await _c.Post("/auth/social/google", new { idToken = "nov-google-token", device = Device("g3") }, tenant: slug3);
        Assert.True(r6.Success, r6.Body.ToString());
        Assert.True(r6.Data.GetProperty("user").GetProperty("emailConfirmed").GetBoolean());

        // bad tokens and unverified provider emails are refused; unknown providers are 501
        Assert.Equal(401, (await _c.Post("/auth/social/google", new { idToken = "garbage", device = Device("g3") }, tenant: slug)).Status);
        Assert.Equal(403, (await _c.Post("/auth/social/google", new { idToken = "unverified-google-token", device = Device("g3") }, tenant: slug)).Status);
        Assert.Equal(501, (await _c.Post("/auth/social/apple", new { idToken = "x", device = Device("g3") }, tenant: slug)).Status);
    }
}
