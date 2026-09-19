using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Devices;
using ZKDeviceManager.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Allow running as a Windows Service (no-op when launched as a console app).
builder.Host.UseWindowsService(o => o.ServiceName = "ZKDeviceManager");

// Blazor Server (interactive server components).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SQL Server via EF Core.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing connection string 'Default'.");
// Use a factory so Blazor components can create short-lived contexts, and also expose a
// scoped AppDbContext for the JobService and background runner scopes.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// ZK PUSH/ADMS ingestion (device-initiated HTTP; works on modern Windows unlike the COM face SDK).
builder.Services.AddSingleton<ZKDeviceManager.Web.Push.PushIngest>();

// ZK device SDK wrapper, STA executor, job queue + background runner.
// Devices:UseSimulator=true uses an in-memory fake device (for demo/validation off-network).
var useSimulator = builder.Configuration.GetValue<bool>("Devices:UseSimulator");
var deviceOptions = builder.Configuration.GetSection("Devices").Get<ZkDeviceOptions>() ?? new ZkDeviceOptions();
builder.Services.AddZkDevices(useSimulator, deviceOptions);

// Daily automation (nightly sync / log pull / backup).
builder.Services.AddHostedService<ZKDeviceManager.Web.Services.SchedulerService>();

// --- Active Directory (LDAP) authentication ---
// Safety hatch: when "Auth:Enabled" is false the whole app stays anonymous (so a directory hiccup can
// never lock everyone out). Flip it in appsettings.json + restart; no rebuild needed.
builder.Services.AddSingleton<ZKDeviceManager.Web.Services.LdapAuthenticator>();
builder.Services.AddSingleton<ZKDeviceManager.Web.Services.AdDirectory>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Cookie.Name = "zkdm_auth";
    });
var authEnabled = builder.Configuration.GetValue("Auth:Enabled", false);
builder.Services.AddAuthorization(o =>
{
    if (authEnabled)
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

// Apply migrations / create the database on startup, then fold any duplicate device identities
// (registered-by-IP vs auto-created "PUSH {sn}") into one record so counts are consistent.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    try
    {
        await ZKDeviceManager.Web.Services.DeviceReconciliation.MergeDuplicatesBySerialAsync(
            db, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
    }
    catch (Exception ex)
    {
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Device reconciliation failed at startup (continuing).");
    }

    // Seed one scheduled-task row per type (disabled by default).
    foreach (ScheduledTaskType tt in Enum.GetValues<ScheduledTaskType>())
        if (!await db.ScheduledTasks.AnyAsync(x => x.Type == tt))
            db.ScheduledTasks.Add(new ScheduledTask { Type = tt, Enabled = false, TimeOfDay = new TimeOnly(2, 0) });
    await db.SaveChangesAsync();

    // Seed the commissioning roster (expected terminals from the master sheet) on first run.
    await ZKDeviceManager.Web.Services.CommissioningSeed.SeedIfEmptyAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ZK ADMS/iClock PUSH endpoints (device -> server over HTTP).
ZKDeviceManager.Web.Push.PushEndpoints.MapPushEndpoints(app);

// Excel (.xlsx) report downloads for users and attendance.
ZKDeviceManager.Web.Export.ExportEndpoints.MapExportEndpoints(app);

// --- Auth endpoints (anonymous) ---
app.MapGet("/login", (string? error, string? returnUrl) =>
    Results.Content(LoginHtml(error, returnUrl), "text/html")).AllowAnonymous();

app.MapPost("/auth/login", async (HttpContext ctx, ZKDeviceManager.Web.Services.LdapAuthenticator ldap,
    IDbContextFactory<AppDbContext> dbf, IConfiguration cfg) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var ret = form["returnUrl"].ToString();

    if (!ldap.Validate(username, password, out var err))
        return Results.Redirect($"/login?error={Uri.EscapeDataString(err)}&returnUrl={Uri.EscapeDataString(ret)}");

    // sAMAccountName (login) for allow-list matching.
    var sam = (username.Contains('\\') ? username[(username.IndexOf('\\') + 1)..]
              : username.Contains('@') ? username[..username.IndexOf('@')] : username).Trim().ToLowerInvariant();

    await using var db = dbf.CreateDbContext();

    // Optional: only accounts explicitly granted access may sign in.
    if (cfg.GetValue("Auth:RestrictToGranted", false) && !await db.AppAccessUsers.AnyAsync(a => a.Login == sam))
        return Results.Redirect($"/login?error={Uri.EscapeDataString("Your account has not been granted access to this app. Contact the administrator.")}&returnUrl={Uri.EscapeDataString(ret)}");

    var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    db.AuditLogs.Add(new AuditLog { User = username, Action = "Sign in", Ip = ctx.Connection.RemoteIpAddress?.ToString() });
    await db.SaveChangesAsync();

    return Results.LocalRedirect(string.IsNullOrWhiteSpace(ret) ? "/" : ret);
}).AllowAnonymous().DisableAntiforgery();

// Lightweight session probe used by the reconnect UI: when a Blazor circuit can't be rejoined we need to
// know WHY. Anonymous on purpose so it answers instead of redirecting — "authenticated:false" means the
// sign-in cookie expired and the browser should go to /login rather than sit on "Failed to rejoin".
app.MapGet("/auth/ping", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { authenticated = ctx.User?.Identity?.IsAuthenticated == true });
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbf) =>
{
    var user = ctx.User.Identity?.Name ?? "";
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await using var db = dbf.CreateDbContext();
    db.AuditLogs.Add(new AuditLog { User = user, Action = "Sign out", Ip = ctx.Connection.RemoteIpAddress?.ToString() });
    await db.SaveChangesAsync();
    return Results.LocalRedirect("/login");
}).DisableAntiforgery();

// Download a JSON backup file produced by the scheduler / "Run now".
app.MapGet("/backup/download", (string name) =>
{
    if (string.IsNullOrEmpty(name) || name.Contains("..") || name.Contains('/') || name.Contains('\\'))
        return Results.BadRequest();
    var path = Path.Combine(ZKDeviceManager.Web.Services.AutomationTasks.BackupDir, name);
    return File.Exists(path) ? Results.File(path, "application/json", name) : Results.NotFound();
});

app.Run();

static string LoginHtml(string? error, string? returnUrl)
{
    string enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    var err = string.IsNullOrEmpty(error) ? "" : $"<div class='err'>{enc(error)}</div>";
    return $$"""
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Sign in · ZK Device Manager</title>
<style>
 *{box-sizing:border-box} body{font-family:Segoe UI,system-ui,sans-serif;background:#eef1f5;margin:0;
 min-height:100vh;display:flex;align-items:center;justify-content:center}
 .card{background:#fff;padding:2rem;border-radius:10px;box-shadow:0 4px 20px rgba(0,0,0,.12);width:340px}
 h1{font-size:1.15rem;margin:0 0 .25rem} .sub{color:#777;font-size:.85rem;margin:0 0 1.2rem}
 label{font-size:.8rem;color:#444} input{width:100%;padding:.55rem;margin:.25rem 0 .9rem;border:1px solid #ccc;
 border-radius:6px;font-size:.95rem} button{width:100%;padding:.65rem;background:#0d6efd;color:#fff;border:0;
 border-radius:6px;font-size:1rem;cursor:pointer} button:hover{background:#0b5ed7}
 .err{background:#fde8e8;color:#a10000;padding:.5rem .7rem;border-radius:6px;font-size:.85rem;margin-bottom:.9rem}
</style></head><body>
<form class="card" method="post" action="/auth/login" autocomplete="on">
 <h1>ZK Device Manager</h1><p class="sub">Sign in with your Sharbatly domain account.</p>
 {{err}}
 <input type="hidden" name="returnUrl" value="{{enc(returnUrl)}}">
 <label>Username</label><input name="username" autofocus autocomplete="username" placeholder="e.g. mohamed.tag">
 <label>Password</label><input name="password" type="password" autocomplete="current-password">
 <button type="submit">Sign in</button>
</form></body></html>
""";
}
