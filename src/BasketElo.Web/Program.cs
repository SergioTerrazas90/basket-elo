using BasketElo.Web.Components;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Web.Auth;
using BasketElo.Web.Billing;
using BasketElo.Web.Elo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Radzen;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using System.Globalization;
using BasketElo.Web.Localization;
using Stripe;

var builder = WebApplication.CreateBuilder(args);
const string devPersonaCookieName = "BasketElo.DevPersona";

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddMemoryCache();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRadzenComponents();
builder.Services.AddSingleton<EloRebuildNotificationCenter>();
builder.Services.AddHostedService<PostgresEloRebuildNotificationListener>();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<StripeBillingOptions>(builder.Configuration.GetSection(StripeBillingOptions.SectionName));
builder.Services.AddScoped<IApplicationUserLoginService, ApplicationUserLoginService>();
builder.Services.AddScoped<IStripeBillingService, StripeBillingService>();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

builder.Services.AddDbContext<BasketEloDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddEloJobStorage(builder.Configuration);

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
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var accountSelectionUrl = QueryHelpers.AddQueryString(
                context.RedirectUri,
                "prompt",
                "select_account");
            context.Response.Redirect(accountSelectionUrl);
            return Task.CompletedTask;
        };
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
});

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

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();
app.Use(async (httpContext, next) =>
{
    var source = CleanCampaignValue(httpContext.Request.Query["utm_source"]);
    var medium = CleanCampaignValue(httpContext.Request.Query["utm_medium"]);
    var campaign = CleanCampaignValue(httpContext.Request.Query["utm_campaign"]);
    if (source is not null || medium is not null || campaign is not null)
    {
        app.Logger.LogInformation(
            "Campaign visit source={CampaignSource} medium={CampaignMedium} campaign={CampaignName} path={Path}",
            source ?? "(none)",
            medium ?? "(none)",
            campaign ?? "(none)",
            httpContext.Request.Path);
    }

    await next(httpContext);
});
app.Use(async (httpContext, next) =>
{
    if (IsPrivateOrUtilityPath(httpContext.Request.Path))
    {
        httpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    await next(httpContext);
});
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

app.Use(async (httpContext, next) =>
{
    var resolution = await ResolveRequestCultureAsync(httpContext);
    if (resolution.PersistCookie)
    {
        SetCultureCookie(httpContext, resolution.CultureName);
    }

    var culture = SupportedCultures.GetCulture(resolution.CultureName);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    await next(httpContext);
});

app.UseAuthorization();
app.MapHangfireDashboard("/admin/jobs", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()]
});
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

app.MapGet("/culture/set", async (
    HttpContext httpContext,
    BasketEloDbContext dbContext,
    string? culture,
    string? returnUrl,
    bool? updateOnly) =>
{
    if (!SupportedCultures.TryNormalize(culture, out var normalizedCulture))
    {
        return Results.BadRequest("Unsupported culture.");
    }

    var userId = GetAuthenticatedUserId(httpContext.User);
    if (userId.HasValue)
    {
        var user = await dbContext.ApplicationUsers
            .SingleOrDefaultAsync(x => x.Id == userId.Value, httpContext.RequestAborted);
        if (user is not null && !string.Equals(user.PreferredCulture, normalizedCulture, StringComparison.Ordinal))
        {
            user.PreferredCulture = normalizedCulture;
            await dbContext.SaveChangesAsync(httpContext.RequestAborted);
        }
    }

    SetCultureCookie(httpContext, normalizedCulture);

    return updateOnly is true
        ? Results.NoContent()
        : Results.Redirect(NormalizeReturnUrl(httpContext, returnUrl));
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

app.MapPost("/billing/stripe/webhook", async (
    HttpRequest request,
    IStripeBillingService billingService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    if (!billingService.GetAvailability().WebhookEnabled)
    {
        return Results.NotFound();
    }

    var signatureHeader = request.Headers["Stripe-Signature"].ToString();
    if (string.IsNullOrWhiteSpace(signatureHeader))
    {
        return Results.BadRequest();
    }

    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync(cancellationToken);
    try
    {
        await billingService.ProcessWebhookAsync(payload, signatureHeader, cancellationToken);
        return Results.Ok();
    }
    catch (StripeException exception)
    {
        loggerFactory.CreateLogger("StripeWebhook").LogWarning(
            exception,
            "Rejected a Stripe webhook with an invalid signature or payload.");
        return Results.BadRequest();
    }
})
.DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/robots.txt", (HttpContext httpContext, IConfiguration configuration) =>
{
    var siteRoot = ResolveSiteRoot(httpContext, configuration);
    var robots = $"User-agent: *\nAllow: /\n\nSitemap: {siteRoot}sitemap.xml\n";
    return Results.Text(robots, "text/plain", Encoding.UTF8);
});
app.MapGet("/sitemap.xml", async (HttpContext httpContext, IConfiguration configuration, BasketEloDbContext dbContext, CancellationToken cancellationToken) =>
{
    var siteRoot = ResolveSiteRoot(httpContext, configuration);
    string[] publicPaths = ["", "movers", "browse", "model-lab", "how-it-works", "data-sources", "about", "sponsor"];
    var publicTeamPaths = await dbContext.TeamRatings
        .AsNoTracking()
        .Include(x => x.Team)
        .Where(x => x.RulesetVersion == BasketElo.Domain.Elo.EloRulesetVersions.Default)
        .Select(x => new { x.TeamId, x.EloPoolKey, x.RulesetVersion, TeamName = x.Team.CanonicalName })
        .ToListAsync(cancellationToken);
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var locations = publicPaths
        .Select(path => new Uri(new Uri(siteRoot), path).AbsoluteUri)
        .Concat(publicTeamPaths.Select(team =>
            new Uri(
                new Uri(siteRoot),
                $"team/{team.TeamId:D}/{ToSlug(team.TeamName)}?pool={Uri.EscapeDataString(team.EloPoolKey)}&ruleset={Uri.EscapeDataString(team.RulesetVersion)}")
                .AbsoluteUri));
    var sitemap = new XDocument(
        new XElement(ns + "urlset",
            locations.Select(location =>
                new XElement(ns + "url",
                    new XElement(ns + "loc", location)))));
    return Results.Text(sitemap.ToString(SaveOptions.DisableFormatting), "application/xml", Encoding.UTF8);
});

app.Run();

static async Task<(string CultureName, bool PersistCookie)> ResolveRequestCultureAsync(HttpContext httpContext)
{
    // A language explicitly selected on this device must win immediately, even if
    // an older account preference is still being synchronized or another tab is open.
    if (SupportedCultures.TryNormalize(
        httpContext.Request.Cookies[SupportedCultures.CultureCookieName],
        out var cookieCulture))
    {
        return (cookieCulture, false);
    }

    var userId = GetAuthenticatedUserId(httpContext.User);
    if (userId.HasValue)
    {
        var dbContext = httpContext.RequestServices.GetRequiredService<BasketEloDbContext>();
        var preferredCulture = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == userId.Value)
            .Select(x => x.PreferredCulture)
            .SingleOrDefaultAsync(httpContext.RequestAborted);

        if (SupportedCultures.TryNormalize(preferredCulture, out var normalizedPreference))
        {
            return (normalizedPreference, false);
        }
    }

    return (CultureInference.Infer(httpContext.Request) ?? SupportedCultures.English, true);
}

static void SetCultureCookie(HttpContext httpContext, string cultureName)
{
    httpContext.Response.Cookies.Append(
        SupportedCultures.CultureCookieName,
        cultureName,
        new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365)
        });
}

static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
{
    var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(value, out var userId) ? userId : null;
}

static string ResolveSiteRoot(HttpContext httpContext, IConfiguration configuration)
{
    var configuredUrl = configuration["Seo:SiteUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredUrl))
    {
        return configuredUrl.TrimEnd('/') + "/";
    }

    return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/";
}

static string? CleanCampaignValue(string? value)
{
    var cleaned = value?.Trim();
    return string.IsNullOrWhiteSpace(cleaned)
        ? null
        : cleaned.Length <= 80 ? cleaned : cleaned[..80];
}

static string ToSlug(string value)
{
    var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
    var slug = new string(normalized
        .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray());

    return string.Join('-', slug
        .ToLowerInvariant()
        .Split([' ', '/', '\\', '.', ',', ':', ';', '&', '+', '(', ')', '[', ']', '{', '}', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(part => part.Split('-', StringSplitOptions.RemoveEmptyEntries)));
}

static bool IsPrivateOrUtilityPath(PathString path)
{
    var value = path.Value ?? string.Empty;
    string[] prefixes =
    [
        "/admin", "/backfill", "/games", "/upcoming", "/home", "/counter",
        "/weather", "/error", "/auth", "/dev", "/billing", "/signin-google", "/model-lab/runs"
    ];

    return prefixes.Any(prefix => value.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}

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
