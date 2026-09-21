using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PrivatePrep.Services.Auth;

/// <summary>
/// Extracts and verifies the Clerk userId from the JWT Bearer token using JWKS.
/// Tokens that cannot be verified (bad signature, expired, JWKS unreachable) are treated as anonymous.
/// Unverified payload parsing exists only in Development, when no Clerk:Issuer is configured.
/// </summary>
public class ClerkAuthService
{
    private readonly ILogger<ClerkAuthService> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _oidcConfigManager;
    private readonly string? _issuer;
    private readonly bool _jwksEnabled;

    public ClerkAuthService(IConfiguration config, IHostEnvironment env, ILogger<ClerkAuthService> logger)
    {
        _logger = logger;

        _issuer = config["Clerk:Issuer"]?.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(_issuer))
        {
            var jwksUrl = $"{_issuer}/.well-known/openid-configuration";
            _oidcConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwksUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = _issuer.StartsWith("https", StringComparison.OrdinalIgnoreCase) });
            _jwksEnabled = true;

            logger.LogInformation("ClerkAuthService: JWKS enabled. Issuer={Issuer} OIDC={OidcUrl}", _issuer, jwksUrl);
        }
        else
        {
            // Refuse to boot the API without verifiable Clerk auth outside Development.
            // An unverified-JWT path with no Clerk:Issuer would let any forged token pass as the user
            // identified by its "sub" claim: full auth bypass.
            if (!env.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "ClerkAuthService: Clerk:Issuer is not configured. "
                    + "The unverified-JWT fallback is only allowed in Development. "
                    + $"Set CLERK__ISSUER / CLERK_ISSUER for environment '{env.EnvironmentName}'.");
            }

            logger.LogWarning(
                "ClerkAuthService: No Clerk:Issuer configured. JWT verification DISABLED (unverified parsing only). "
                + "This is only permitted in Development; current environment is '{Env}'.",
                env.EnvironmentName);
        }
    }

    /// <summary>
    /// Pre-fetches JWKS keys during application startup so the first request never blocks
    /// on the well-known OIDC fetch. Call this from Program.cs after building the app.
    /// </summary>
    public async Task WarmupAsync()
    {
        if (!_jwksEnabled || _oidcConfigManager is null) return;
        try
        {
            var oidcConfig = await _oidcConfigManager.GetConfigurationAsync(CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogInformation("ClerkAuthService: JWKS keys pre-fetched successfully. Keys={KeyCount}",
                oidcConfig.SigningKeys?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClerkAuthService: Failed to pre-fetch JWKS keys. JWT verification will retry on first request.");
        }
    }

    /// <summary>
    /// Resolves the userId from the Bearer token via JWKS validation (or unverified parsing in Development).
    /// The principal keeps Clerk claim names as issued, including the custom <c>email</c> claim.
    /// </summary>
    public virtual async Task<(string? userId, bool isAnonymous, ClaimsPrincipal? principal)> ExtractUserIdAsync(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AnonymousFallback(request);
        }

        var token = authHeader["Bearer ".Length..];

        if (_jwksEnabled)
        {
            var principal = await ValidateTokenWithJwksAsync(token);
            var userId = ReadUserId(principal);
            if (userId is not null && principal is not null)
                return (userId, false, principal);

            _logger.LogWarning("JWT signature verification failed. Treating as anonymous.");
            return AnonymousFallback(request);
        }

        return ExtractUnverified(token, request);
    }

    private async Task<ClaimsPrincipal?> ValidateTokenWithJwksAsync(string token)
    {
        try
        {
            // ConfigurationManager caches the config and swaps in fresh keys after RequestRefresh().
            var oidcConfig = await _oidcConfigManager!.GetConfigurationAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (oidcConfig.SigningKeys == null || !oidcConfig.SigningKeys.Any())
            {
                _logger.LogError("JWKS returned no signing keys from {Issuer}", _issuer);
                return null;
            }

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidIssuers = [_issuer!, $"{_issuer}/"],
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuerSigningKey = true,
            };

            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false,
            };
            handler.InboundClaimTypeMap.Clear();
            return handler.ValidateToken(token, validationParams, out _);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("JWT expired");
            return null;
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            _logger.LogWarning("JWT signing key not found in JWKS, requesting refresh");
            _oidcConfigManager!.RequestRefresh();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("JWT validation failed: {ExType}", ex.GetType().Name);
            return null;
        }
    }

    private (string? userId, bool isAnonymous, ClaimsPrincipal? principal) ExtractUnverified(string token, HttpRequest request)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return AnonymousFallback(request);

            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
                             .Replace('-', '+').Replace('_', '/');

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var userId = ReadJsonString(claims, "sub");

            if (string.IsNullOrEmpty(userId))
                return AnonymousFallback(request);

            var identity = new ClaimsIdentity("Clerk");
            if (claims is not null)
            {
                foreach (var (key, el) in claims)
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var value = el.GetString();
                        if (!string.IsNullOrEmpty(value))
                            identity.AddClaim(new Claim(key, value));
                    }
                }
            }

            return (userId, false, new ClaimsPrincipal(identity));
        }
        catch
        {
            return AnonymousFallback(request);
        }
    }

    private static string? ReadUserId(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return null;

        var sub = principal.FindFirst("sub")?.Value
               ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }

    private static string? ReadJsonString(Dictionary<string, JsonElement>? claims, string key)
    {
        if (claims is null || !claims.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        return el.GetString();
    }

    private static (string? userId, bool isAnonymous, ClaimsPrincipal? principal) AnonymousFallback(HttpRequest request)
    {
        var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return ($"ip:{ip}", true, null);
    }
}
