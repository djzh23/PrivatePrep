using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace PrivatePrep.Tests.Support;

/// <summary>RSA key pair that can sign test JWTs and be published as a JWK.</summary>
internal sealed class TestSigningKey : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    public TestSigningKey(string kid)
    {
        Kid = kid;
        PrivateKey = new RsaSecurityKey(_rsa) { KeyId = kid };
    }

    public string Kid { get; }

    public RsaSecurityKey PrivateKey { get; }

    public object ToJwk()
    {
        var p = _rsa.ExportParameters(false);
        return new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid = Kid,
            n = Base64UrlEncoder.Encode(p.Modulus),
            e = Base64UrlEncoder.Encode(p.Exponent),
        };
    }

    public void Dispose() => _rsa.Dispose();
}

/// <summary>
/// Minimal in-process OIDC provider (discovery document + JWKS) on a random localhost port,
/// so JWT verification can be tested end to end without network access or production seams.
/// </summary>
internal sealed class FakeOidcServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private volatile IReadOnlyList<TestSigningKey> _keys;
    private int _jwksRequests;

    private FakeOidcServer(WebApplication app, string issuer, IReadOnlyList<TestSigningKey> keys)
    {
        _app = app;
        Issuer = issuer;
        _keys = keys;
    }

    /// <summary>Base URL, e.g. <c>http://127.0.0.1:53211</c>. Usable as <c>Clerk:Issuer</c>.</summary>
    public string Issuer { get; }

    public int JwksRequestCount => Volatile.Read(ref _jwksRequests);

    /// <summary>Replaces the published key set, simulating a provider-side key rotation.</summary>
    public void SetKeys(params TestSigningKey[] keys) => _keys = keys;

    public static async Task<FakeOidcServer> StartAsync(params TestSigningKey[] keys)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        FakeOidcServer? server = null;
        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
        {
            issuer = server!.Issuer,
            jwks_uri = $"{server.Issuer}/jwks",
        }));
        app.MapGet("/jwks", () =>
        {
            Interlocked.Increment(ref server!._jwksRequests);
            return Results.Text(
                JsonSerializer.Serialize(new { keys = server._keys.Select(k => k.ToJwk()) }),
                "application/json");
        });

        await app.StartAsync();
        server = new FakeOidcServer(app, app.Urls.Single().TrimEnd('/'), keys);
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
