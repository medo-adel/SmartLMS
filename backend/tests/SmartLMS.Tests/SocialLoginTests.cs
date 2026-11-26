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
        // a Google account already linked to another tenant's account cannot be claimed here
        var r4 = await _c.Post("/auth/social/google", new { idToken = "valid-google-token", device = Device("g2") }, tenant: slug2);
        Assert.Equal(409, r4.Status);
        Assert.Equal("EXTERNAL_IDENTITY_IN_USE", r4.Body.GetProperty("code").GetString());

        // bad tokens and unverified provider emails are refused; unknown providers are 501
        Assert.Equal(401, (await _c.Post("/auth/social/google", new { idToken = "garbage", device = Device("g3") }, tenant: slug)).Status);
        Assert.Equal(403, (await _c.Post("/auth/social/google", new { idToken = "unverified-google-token", device = Device("g3") }, tenant: slug)).Status);
        Assert.Equal(501, (await _c.Post("/auth/social/apple", new { idToken = "x", device = Device("g3") }, tenant: slug)).Status);
    }
}
