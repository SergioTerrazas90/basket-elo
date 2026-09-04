using System.Security.Claims;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Localization;

public sealed class UserCultureResolverTests
{
    [Fact]
    public async Task StoredPreferenceWinsOverDeviceCookie()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(SupportedCultures.Spanish);
        dbContext.ApplicationUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var httpContext = CreateAuthenticatedContext(user.Id);
        httpContext.Request.Headers.Cookie = $"{SupportedCultures.CultureCookieName}={SupportedCultures.English}";

        var result = await new UserCultureResolver(dbContext).ResolveAsync(httpContext);

        Assert.Equal(SupportedCultures.Spanish, result.CultureName);
        Assert.True(result.PersistCookie);
    }

    [Fact]
    public async Task FirstAuthenticatedVisitStoresIpInferredCulture()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(preferredCulture: null);
        dbContext.ApplicationUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var httpContext = CreateAuthenticatedContext(user.Id);
        httpContext.Request.Headers["CF-IPCountry"] = "ES";
        httpContext.Request.Headers.AcceptLanguage = "en-US,en;q=0.9";

        var result = await new UserCultureResolver(dbContext).ResolveAsync(httpContext);

        Assert.Equal(SupportedCultures.Spanish, result.CultureName);
        Assert.True(result.PersistCookie);
        Assert.Equal(SupportedCultures.Spanish, user.PreferredCulture);
    }

    [Fact]
    public async Task ExistingDeviceChoiceSeedsNewUserPreference()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(preferredCulture: null);
        dbContext.ApplicationUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var httpContext = CreateAuthenticatedContext(user.Id);
        httpContext.Request.Headers.Cookie = $"{SupportedCultures.CultureCookieName}={SupportedCultures.English}";
        httpContext.Request.Headers["CF-IPCountry"] = "ES";

        var result = await new UserCultureResolver(dbContext).ResolveAsync(httpContext);

        Assert.Equal(SupportedCultures.English, result.CultureName);
        Assert.False(result.PersistCookie);
        Assert.Equal(SupportedCultures.English, user.PreferredCulture);
    }

    [Fact]
    public void IpCountryTakesPriorityForInitialInference()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["CF-IPCountry"] = "MX";
        httpContext.Request.Headers.AcceptLanguage = "en-US,en;q=0.9";

        var result = CultureInference.Infer(httpContext.Request);

        Assert.Equal(SupportedCultures.Spanish, result);
    }

    [Fact]
    public void BrowserLanguageIsFallbackWhenIpCountryIsNotSpanishSpeaking()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["CF-IPCountry"] = "FR";
        httpContext.Request.Headers.AcceptLanguage = "es-ES,es;q=0.9";

        var result = CultureInference.Infer(httpContext.Request);

        Assert.Equal(SupportedCultures.Spanish, result);
    }

    private static BasketEloDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BasketEloDbContext(options);
    }

    private static ApplicationUser CreateUser(string? preferredCulture) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Culture test user",
        Email = $"{Guid.NewGuid():N}@example.test",
        NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
        PreferredCulture = preferredCulture,
        CreatedAtUtc = DateTime.UtcNow,
        LastLoginAtUtc = DateTime.UtcNow
    };

    private static DefaultHttpContext CreateAuthenticatedContext(Guid userId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "test"));
        return context;
    }
}
