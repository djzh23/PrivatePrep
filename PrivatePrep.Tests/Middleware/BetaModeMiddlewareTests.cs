using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrivatePrep.Configuration;
using PrivatePrep.Middleware;
using PrivatePrep.Services.Auth;

namespace PrivatePrep.Tests.Middleware;

public class BetaModeMiddlewareTests
{
    private const string AllowedList =
        "ijd.zouh@yahoo.com,zouh.ijd@gmail.com,platon10.rochd@gmail.com,manonaubrion@gmail.com";

    [Fact]
    public async Task Disabled_PassesEveryRequest()
    {
        var result = await InvokeAsync(
            enabled: false,
            path: "/api/agent/analyze",
            anonymous: false,
            email: "stranger@example.com");

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_AnonymousOnProtectedPath_PassesThroughToNormalAuth()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/agent/analyze",
            anonymous: true,
            email: null);

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_AllowedEmail_PassesThrough()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/agent/analyze",
            anonymous: false,
            email: "ijd.zouh@yahoo.com");

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_DisallowedEmail_Returns403WithMessage()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/agent/analyze",
            anonymous: false,
            email: "stranger@example.com");

        Assert.False(result.NextCalled);
        Assert.Equal(403, result.Status);

        using var doc = JsonDocument.Parse(result.Body);
        Assert.Equal("beta_access_required", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("ijd.zouh@yahoo.com", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Enabled_Health_AlwaysPassesThrough()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/health",
            anonymous: true,
            email: null);

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_AllowedEmail_IsCaseInsensitive()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/profile",
            anonymous: false,
            email: "IJD.ZOUH@YAHOO.COM");

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_OptionsPreflight_AlwaysPassesThrough()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/agent/analyze",
            anonymous: false,
            email: "stranger@example.com",
            method: HttpMethods.Options);

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_StripeWebhook_AlwaysPassesThrough()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/stripe/webhook",
            anonymous: true,
            email: null);

        Assert.True(result.NextCalled);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Enabled_AuthenticatedWithoutEmailClaim_Returns403()
    {
        var result = await InvokeAsync(
            enabled: true,
            path: "/api/agent/analyze",
            anonymous: false,
            email: null);

        Assert.False(result.NextCalled);
        Assert.Equal(403, result.Status);
        Assert.Contains("beta_access_required", result.Body);
    }

    private static async Task<(bool NextCalled, int Status, string Body)> InvokeAsync(
        bool enabled,
        string path,
        bool anonymous,
        string? email,
        string? method = null)
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mw = new BetaModeMiddleware(
            next,
            Options.Create(new BetaModeOptions
            {
                Enabled = enabled,
                AllowedEmails = AllowedList,
            }),
            NullLogger<BetaModeMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = method ?? HttpMethods.Post;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var claims = new List<Claim> { new("sub", "user_1") };
        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim("email", email));

        if (!anonymous)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Clerk"));

        var user = new AppUserContext
        {
            UserId = anonymous ? "ip:127.0.0.1" : "user_1",
            IsAnonymous = anonymous,
        };

        await mw.InvokeAsync(context, user);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (nextCalled, context.Response.StatusCode, body);
    }
}
