using BasketElo.Web.Components;
using BasketElo.Domain.Entities;
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
const string devPersonaCookieName = "BasketElo.DevPersona";

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
                identity.AddClaim(new Claim(AuthClaimTypes.AuthMode, "google"));

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
        var persona = ResolveDevPersona(httpContext.Request.Cookies[devPersonaCookieName]);
        if (persona.UserId.HasValue)
        {
            await EnsureDevPersonaUserAsync(httpContext, persona);
            httpContext.User = CreateDevPersonaPrincipal(persona);
        }
        else
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

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

app.MapGet("/dev/persona", (HttpContext httpContext, string? persona, string? returnUrl) =>
{
    if (authOptions.Enabled)
    {
        return Results.NotFound();
    }

    var selectedPersona = ResolveDevPersona(persona).Key;
    httpContext.Response.Cookies.Append(
        devPersonaCookieName,
        selectedPersona,
        new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30)
        });

    return Results.Redirect(NormalizeReturnUrl(httpContext, returnUrl));
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

static DevPersona ResolveDevPersona(string? key)
{
    return key?.Trim().ToLowerInvariant() switch
    {
        "free" => new DevPersona(
            "free",
            "Free user",
            Guid.Parse("00000000-0000-0000-0000-000000000025"),
            "dev-free@basket-elo.local",
            []),
        "paid" => new DevPersona(
            "paid",
            "Paying user",
            Guid.Parse("00000000-0000-0000-0000-000000000026"),
            "dev-paid@basket-elo.local",
            []),
        "admin" => new DevPersona(
            "admin",
            "Admin user",
            Guid.Parse("00000000-0000-0000-0000-000000000024"),
            "dev-admin@basket-elo.local",
            [ApplicationRoleKeys.Admin]),
        _ => new DevPersona("anonymous", "Anonymous", null, null, [])
    };
}

static async Task EnsureDevPersonaUserAsync(HttpContext httpContext, DevPersona persona)
{
    if (!persona.UserId.HasValue || string.IsNullOrWhiteSpace(persona.Email))
    {
        return;
    }

    var dbContext = httpContext.RequestServices.GetRequiredService<BasketEloDbContext>();
    var now = DateTime.UtcNow;
    var normalizedEmail = AuthOptions.NormalizeEmail(persona.Email);
    var user = await dbContext.ApplicationUsers
        .Include(x => x.UserRoles)
        .SingleOrDefaultAsync(x => x.Id == persona.UserId.Value, httpContext.RequestAborted);

    if (user is null)
    {
        user = new ApplicationUser
        {
            Id = persona.UserId.Value,
            CreatedAtUtc = now
        };
        dbContext.ApplicationUsers.Add(user);
    }

    user.DisplayName = persona.DisplayName;
    user.Email = persona.Email;
    user.NormalizedEmail = normalizedEmail;
    user.LastLoginAtUtc = now;

    if (persona.Roles.Count > 0)
    {
        var roles = await dbContext.ApplicationRoles
            .Where(x => persona.Roles.Contains(x.Key))
            .ToListAsync(httpContext.RequestAborted);

        foreach (var role in roles)
        {
            if (user.UserRoles.Any(x => x.RoleId == role.Id))
            {
                continue;
            }

            user.UserRoles.Add(new ApplicationUserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                CreatedAtUtc = now
            });
        }
    }

    await dbContext.SaveChangesAsync(httpContext.RequestAborted);
}

static ClaimsPrincipal CreateDevPersonaPrincipal(DevPersona persona)
{
    var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, persona.UserId!.Value.ToString()),
            new Claim(ClaimTypes.Name, persona.DisplayName),
            new Claim(ClaimTypes.Email, persona.Email!),
            new Claim(AuthClaimTypes.AuthMode, "google")
        ],
        "DevPersona");

    foreach (var role in persona.Roles)
    {
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }

    return new ClaimsPrincipal(identity);
}

sealed record DevPersona(
    string Key,
    string DisplayName,
    Guid? UserId,
    string? Email,
    IReadOnlyCollection<string> Roles);
