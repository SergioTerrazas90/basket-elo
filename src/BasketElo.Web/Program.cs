using BasketElo.Web.Components;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Web.Auth;
using BasketElo.Web.Elo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Radzen;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var authDisabledUserId = Guid.Parse("00000000-0000-0000-0000-000000000024");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRadzenComponents();
builder.Services.AddSingleton<EloRebuildNotificationCenter>();
builder.Services.AddHostedService<PostgresEloRebuildNotificationListener>();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddScoped<IApplicationUserLoginService, ApplicationUserLoginService>();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

builder.Services.AddDbContext<BasketEloDbContext>(options =>
    options.UseNpgsql(connectionString));

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var isGoogleLoginConfigured = !string.IsNullOrWhiteSpace(googleClientId) &&
    !string.IsNullOrWhiteSpace(googleClientSecret);

var authenticationBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/forbidden";
        options.Cookie.Name = "BasketElo.Auth";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });

if (authOptions.Enabled && isGoogleLoginConfigured)
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.ClaimActions.MapJsonKey(AuthClaimTypes.AvatarUrl, "picture");
        options.Events.OnCreatingTicket = async context =>
        {
            var principal = context.Principal ?? throw new InvalidOperationException("Google did not return a user principal.");
            var providerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var displayName = principal.FindFirstValue(ClaimTypes.Name) ?? email;
            var avatarUrl = principal.FindFirstValue(AuthClaimTypes.AvatarUrl);

            var loginService = context.HttpContext.RequestServices.GetRequiredService<IApplicationUserLoginService>();
            var login = await loginService.UpsertExternalLoginAsync(
                "google",
                providerUserId,
                email,
                displayName,
                avatarUrl,
                context.HttpContext.RequestAborted);

            if (principal.Identity is ClaimsIdentity identity)
            {
                foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToList())
                {
                    identity.RemoveClaim(claim);
                }

                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                {
                    identity.RemoveClaim(claim);
                }

                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, login.UserId.ToString()));
                identity.AddClaim(new Claim(ClaimTypes.Email, login.Email));
                identity.AddClaim(new Claim(ClaimTypes.Name, login.DisplayName));

                if (!string.IsNullOrWhiteSpace(login.AvatarUrl))
                {
                    identity.AddClaim(new Claim(AuthClaimTypes.AvatarUrl, login.AvatarUrl));
                }

                foreach (var role in login.Roles)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        };
    });
}

builder.Services.AddAuthorization();
builder.Services.AddTransient<AuthenticatedApiHttpMessageHandler>();

builder.Services.AddHttpClient(
    "BasketElo.Api",
    client =>
    {
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
        client.BaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? new Uri("http://localhost:5147/")
            : new Uri(apiBaseUrl.TrimEnd('/') + "/");
    })
    .AddHttpMessageHandler<AuthenticatedApiHttpMessageHandler>();

builder.Services.AddScoped(serviceProvider =>
{
    var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("BasketElo.Api");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
if (!authOptions.Enabled)
{
    app.Use(async (httpContext, next) =>
    {
        httpContext.User = CreateAuthDisabledPrincipal(authDisabledUserId);
        await next(httpContext);
    });
}

app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/login", (HttpContext httpContext, IConfiguration configuration, string? returnUrl) =>
{
    var normalizedReturnUrl = NormalizeReturnUrl(httpContext, returnUrl);

    if (!authOptions.Enabled)
    {
        return Results.Redirect(normalizedReturnUrl);
    }

    if (httpContext.User.Identity?.IsAuthenticated == true)
    {
        return Results.Redirect(normalizedReturnUrl);
    }

    if (string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) ||
        string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]))
    {
        return Results.Problem(
            "Google login is not configured. Set Authentication__Google__ClientId and Authentication__Google__ClientSecret.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Challenge(
        new AuthenticationProperties
        {
            RedirectUri = normalizedReturnUrl
        },
        [GoogleDefaults.AuthenticationScheme]);
});

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static string NormalizeReturnUrl(HttpContext httpContext, string? returnUrl)
{
    if (!string.IsNullOrWhiteSpace(returnUrl) &&
        Uri.TryCreate(returnUrl, UriKind.Relative, out _) &&
        !IsAuthPath(returnUrl))
    {
        return returnUrl;
    }

    var fallback = httpContext.Request.Headers.Referer.ToString();
    return Uri.TryCreate(fallback, UriKind.Absolute, out var referer) &&
        referer.Host == httpContext.Request.Host.Host &&
        PortsMatch(httpContext.Request.Host.Port, referer.Port) &&
        !IsAuthPath(referer.PathAndQuery)
        ? referer.PathAndQuery
        : "/";
}

static bool IsAuthPath(string path)
{
    var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
    return normalizedPath.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith("/signin-google", StringComparison.OrdinalIgnoreCase);
}

static bool PortsMatch(int? requestPort, int refererPort)
{
    return requestPort is null || refererPort == requestPort.Value;
}

static ClaimsPrincipal CreateAuthDisabledPrincipal(Guid userId)
{
    var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "Local access"),
            new Claim(ClaimTypes.Email, "local@basket-elo"),
            new Claim(ClaimTypes.Role, "admin")
        ],
        "AuthDisabled");

    return new ClaimsPrincipal(identity);
}
