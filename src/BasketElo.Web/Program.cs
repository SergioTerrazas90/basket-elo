using BasketElo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped(_ =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
    var baseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
        ? new Uri("http://localhost:5147/")
        : new Uri(apiBaseUrl.TrimEnd('/') + "/");

    return new HttpClient { BaseAddress = baseAddress };
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
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
