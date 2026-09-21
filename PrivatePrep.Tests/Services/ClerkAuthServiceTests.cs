using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PrivatePrep.Services.Auth;
using PrivatePrep.Tests.Support;

namespace PrivatePrep.Tests.Services;

public class ClerkAuthServiceTests
{
    private const string UserId = "user_2abc";
    private const string ClientIp = "203.0.113.7";

    // ---- startup guard: no verifiable auth outside Development ------------------------------

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Constructor_WithoutIssuerOutsideDevelopment_Throws(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(issuer: null, environment));

        Assert.Contains("Clerk:Issuer", ex.Message);
    }

    [Fact]
    public void Constructor_WithBlankIssuerOutsideDevelopment_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateService(issuer: "   ", "Production"));
    }

    [Fact]
    public void Constructor_WithoutIssuerInDevelopment_DoesNotThrow()
    {
        var service = CreateService(issuer: null, "Development");

        Assert.NotNull(service);
    }

    // ---- request without usable credentials -------------------------------------------------

    [Fact]
    public async Task ExtractUserIdAsync_WithoutAuthorizationHeader_IsAnonymousKeyedByIp()
    {
        await using var oidc = await StartOidcAsync();
        var service = CreateService(oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest());

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithoutRemoteAddress_UsesUnknownIp()
    {
        await using var oidc = await StartOidcAsync();
        var service = CreateService(oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest(remoteIp: null));

        Assert.True(result.isAnonymous);
        Assert.Equal("ip:unknown", result.userId);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithNonBearerScheme_IsAnonymous()
    {
        await using var oidc = await StartOidcAsync();
        var service = CreateService(oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest("Basic dXNlcjpwYXNz"));

        AssertAnonymous(result);
    }

    // ---- verified path (Clerk:Issuer configured) --------------------------------------------

    [Fact]
    public async Task ExtractUserIdAsync_WithValidToken_ReturnsUserAndPrincipalWithClerkClaims()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, oidc.Issuer, extraClaims: [new Claim("email", "anna@example.de")]);

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        Assert.False(result.isAnonymous);
        Assert.Equal(UserId, result.userId);
        Assert.NotNull(result.principal);
        Assert.Equal(UserId, result.principal.FindFirst("sub")?.Value);
        Assert.Equal("anna@example.de", result.principal.FindFirst("email")?.Value);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithLowercaseBearerScheme_Authenticates()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest($"bearer {token}"));

        Assert.False(result.isAnonymous);
        Assert.Equal(UserId, result.userId);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithTrailingSlashIssuerClaim_Authenticates()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer + "/");
        var token = CreateToken(key, oidc.Issuer + "/");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        Assert.False(result.isAnonymous);
        Assert.Equal(UserId, result.userId);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithExpiredToken_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, oidc.Issuer, expires: DateTime.UtcNow.AddHours(-1));

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithForeignIssuer_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, "https://evil.example.com");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithForgedSignatureUnderKnownKid_IsAnonymous()
    {
        using var realKey = new TestSigningKey("ins_1");
        using var attackerKey = new TestSigningKey("ins_1"); // same kid, different key material
        await using var oidc = await StartOidcAsync(realKey);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(attackerKey, oidc.Issuer, subject: "user_victim");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithUnknownKid_IsAnonymous()
    {
        using var realKey = new TestSigningKey("ins_1");
        using var otherKey = new TestSigningKey("ins_other");
        await using var oidc = await StartOidcAsync(realKey);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(otherKey, oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithUnsignedAlgNoneToken_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateUnsignedToken($$"""{"sub":"user_victim","iss":"{{oidc.Issuer}}"}""");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithValidTokenLackingSubject_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, oidc.Issuer, subject: null);

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WithGarbageToken_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest("Bearer not-a-jwt"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_WhenProviderPublishesNoKeys_IsAnonymous()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(); // empty JWKS
        var service = CreateService(oidc.Issuer);
        var token = CreateToken(key, oidc.Issuer);

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Fact]
    public async Task ExtractUserIdAsync_AfterProviderKeyRotation_RecoversWithoutRestart()
    {
        using var oldKey = new TestSigningKey("ins_old");
        using var newKey = new TestSigningKey("ins_new");
        await using var oidc = await StartOidcAsync(oldKey);
        var service = CreateService(oidc.Issuer);
        await service.WarmupAsync();

        oidc.SetKeys(newKey); // provider rotates its signing key
        var token = CreateToken(newKey, oidc.Issuer);

        // The first requests may still see the old key set while the refresh runs in the background.
        var result = await PollUntilAuthenticatedAsync(service, token, TimeSpan.FromSeconds(5));

        Assert.False(result.isAnonymous);
        Assert.Equal(UserId, result.userId);
    }

    // ---- warm-up ----------------------------------------------------------------------------

    [Fact]
    public async Task WarmupAsync_FetchesKeysFromProvider()
    {
        using var key = new TestSigningKey("ins_1");
        await using var oidc = await StartOidcAsync(key);
        var service = CreateService(oidc.Issuer);

        await service.WarmupAsync();

        Assert.Equal(1, oidc.JwksRequestCount);
    }

    [Fact]
    public async Task WarmupAsync_WhenProviderUnreachable_DoesNotThrow()
    {
        var service = CreateService("http://127.0.0.1:1");

        await service.WarmupAsync();
    }

    [Fact]
    public async Task WarmupAsync_WithoutIssuerInDevelopment_DoesNotThrow()
    {
        var service = CreateService(issuer: null, "Development");

        await service.WarmupAsync();
    }

    // ---- unverified fallback: Development only ----------------------------------------------

    [Fact]
    public async Task ExtractUserIdAsync_InDevelopmentWithoutIssuer_ReadsSubjectFromUnsignedToken()
    {
        var service = CreateService(issuer: null, "Development");
        var token = CreateUnsignedToken($$"""{"sub":"{{UserId}}","email":"dev@example.de","n":5}""");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        Assert.False(result.isAnonymous);
        Assert.Equal(UserId, result.userId);
        Assert.NotNull(result.principal);
        Assert.Equal("dev@example.de", result.principal.FindFirst("email")?.Value);
        Assert.Null(result.principal.FindFirst("n")); // only string claims are copied
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"sub":123}""")]
    [InlineData("""{"sub":""}""")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public async Task ExtractUserIdAsync_InDevelopment_WithUnusablePayload_IsAnonymous(string payload)
    {
        var service = CreateService(issuer: null, "Development");
        var token = CreateUnsignedToken(payload);

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    [Theory]
    [InlineData("only.two")]
    [InlineData("a.b.c")] // three parts, payload is not valid base64
    public async Task ExtractUserIdAsync_InDevelopment_WithMalformedToken_IsAnonymous(string token)
    {
        var service = CreateService(issuer: null, "Development");

        var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));

        AssertAnonymous(result);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static Task<FakeOidcServer> StartOidcAsync(params TestSigningKey[] keys) =>
        FakeOidcServer.StartAsync(keys);

    private static ClerkAuthService CreateService(string? issuer, string environment = "Production")
    {
        var settings = new Dictionary<string, string?>();
        if (issuer is not null)
            settings["Clerk:Issuer"] = issuer;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);

        return new ClerkAuthService(config, env.Object, NullLogger<ClerkAuthService>.Instance);
    }

    private static HttpRequest CreateRequest(string? authorization = null, string? remoteIp = ClientIp)
    {
        var ctx = new DefaultHttpContext();
        if (authorization is not null)
            ctx.Request.Headers.Authorization = authorization;
        ctx.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        return ctx.Request;
    }

    private static string CreateToken(
        TestSigningKey key,
        string issuer,
        string? subject = UserId,
        DateTime? expires = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        var claims = new List<Claim>(extraClaims ?? []);
        if (subject is not null)
            claims.Add(new Claim("sub", subject));

        var jwt = new JwtSecurityToken(
            issuer,
            null,
            claims,
            notBefore: expiry.AddHours(-2),
            expires: expiry,
            new SigningCredentials(key.PrivateKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string CreateUnsignedToken(string payloadJson) =>
        $"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{Base64UrlEncoder.Encode(payloadJson)}.";

    private static async Task<(string? userId, bool isAnonymous, ClaimsPrincipal? principal)> PollUntilAuthenticatedAsync(
        ClerkAuthService service, string token, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var result = await service.ExtractUserIdAsync(CreateRequest($"Bearer {token}"));
            if (!result.isAnonymous || DateTime.UtcNow >= deadline)
                return result;
            await Task.Delay(100);
        }
    }

    private static void AssertAnonymous((string? userId, bool isAnonymous, ClaimsPrincipal? principal) result)
    {
        Assert.True(result.isAnonymous);
        Assert.Equal($"ip:{ClientIp}", result.userId);
        Assert.Null(result.principal);
    }
}
