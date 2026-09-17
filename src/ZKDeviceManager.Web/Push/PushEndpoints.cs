using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Push;

/// <summary>
/// ZK ADMS / iClock PUSH protocol endpoints. Terminals configured with this server as their
/// "Cloud/ADMS server" connect here over HTTP and upload users, biometric templates (incl. faces)
/// and attendance — no COM SDK, so it works on modern Windows where GetUserFaceStr spins.
/// </summary>
public static class PushEndpoints
{
    private static readonly object _rawLock = new();
    private static readonly string _rawPath = Path.Combine(AppContext.BaseDirectory, "push_raw.log");
    private static bool _rawEnabled;   // debug only; enable via config "Push:RawLog"
    /// <summary>Max bytes of commands handed to a terminal in one getrequest poll (config "Push:MaxResponseBytes").</summary>
    private static int _maxResponseBytes = 1024;
    private static void Raw(string what)
    {
        if (!_rawEnabled) return;
        try { lock (_rawLock) File.AppendAllText(_rawPath, $"{DateTime.Now:HH:mm:ss} {what}\n"); } catch { }
    }

    public static void MapPushEndpoints(this WebApplication app)
    {
        _rawEnabled = app.Configuration.GetValue<bool>("Push:RawLog");
        _maxResponseBytes = app.Configuration.GetValue("Push:MaxResponseBytes", 1024);

        // Terminals connect here WITHOUT any app login — these endpoints must stay anonymous even when
        // Active Directory auth is enabled for the human-facing UI.
        var group = app.MapGroup("").AllowAnonymous();

        // Handshake: device asks for its options when it first connects / periodically.
        group.MapGet("/iclock/cdata", async (HttpContext ctx, PushIngest ingest) =>
        {
            var sn = ctx.Request.Query["SN"].ToString();
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(sn)) await ingest.TouchDeviceAsync(sn, ip, ctx.Request.QueryString.Value);

            var options =
                $"GET OPTION FROM: {sn}\n" +
                "Stamp=9999\n" +
                "OpStamp=9999\n" +
                "ErrorDelay=30\n" +
                "Delay=10\n" +
                "TransTimes=00:00;14:05\n" +
                "TransInterval=1\n" +
                "TransFlag=1111111111\n" +
                "TimeZone=180\n" +
                "Realtime=1\n" +
                "Encrypt=0\n";
            return Results.Text(options, "text/plain");
        });

        // Data upload: device posts USERINFO / BIODATA(face) / ATTLOG records here.
        group.MapPost("/iclock/cdata", async (HttpContext ctx, PushIngest ingest) =>
        {
            var sn = ctx.Request.Query["SN"].ToString();
            var table = ctx.Request.Query["table"].ToString();
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            Raw($"POST cdata SN={sn} table={table} qs={ctx.Request.QueryString} len={body.Length}\n---\n{(body.Length > 2000 ? body[..2000] : body)}\n===");
            try { await ingest.IngestAsync(sn, ip, table, body); }
            catch (Exception ex) { app.Logger.LogError(ex, "PUSH ingest error"); }
            return Results.Text("OK", "text/plain");
        }).DisableAntiforgery();

        // Command poll: device asks whether the server has anything for it to do.
        group.MapGet("/iclock/getrequest", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbf, PushIngest ingest) =>
        {
            var sn = ctx.Request.Query["SN"].ToString();
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(sn)) await ingest.TouchDeviceAsync(sn, ip);

            await using var db = dbf.CreateDbContext();
            var pending = await db.PushCommands
                .Where(c => c.SerialNumber == sn && c.DeliveredUtc == null)
                .OrderBy(c => c.Id).Take(200).ToListAsync();
            if (pending.Count == 0)
                return Results.Text("OK", "text/plain");

            // IMPORTANT: budget the response size. These terminals silently drop everything after the
            // first command when the reply is large — proven in the field: every command <= ~150 bytes
            // (USERINFO/ENROLL/DELETE) gets acknowledged, while ~1.7-1.9 KB FACE/FINGERTMP commands sent
            // in the same batch were NEVER acknowledged (0 of 2462). So small commands batch together and
            // a big template command is sent on its own, one per poll.
            var lines = new List<string>();
            var budget = _maxResponseBytes;
            foreach (var c in pending)
            {
                var line = $"C:{c.Id}:{c.CommandText}";
                if (lines.Count > 0 && line.Length > budget) break;   // always deliver at least one
                lines.Add(line);
                c.DeliveredUtc = DateTime.UtcNow;
                budget -= line.Length;
                if (budget <= 0) break;
            }
            await db.SaveChangesAsync();
            var resp = string.Join('\n', lines) + "\n";
            Raw($"GET getrequest SN={sn} -> DELIVER:\n{resp}");
            return Results.Text(resp, "text/plain");
        });

        // Command result: device reports the outcome of a command.
        group.MapPost("/iclock/devicecmd", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbf) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            Raw($"POST devicecmd: {body}");
            // body like: ID=12&Return=0&CMD=DATA — tolerate duplicate keys (some firmware repeats them).
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var a = part.Split('=', 2);
                if (a.Length == 2) kv[a[0]] = a[1];   // last value wins; never throws on duplicates
            }
            if (kv.TryGetValue("ID", out var ids) && int.TryParse(ids, out var id))
            {
                await using var db = dbf.CreateDbContext();
                var c = await db.PushCommands.FindAsync(id);
                if (c != null)
                {
                    c.CompletedUtc = DateTime.UtcNow;
                    c.Result = kv.GetValueOrDefault("Return");
                    await db.SaveChangesAsync();
                }
            }
            return Results.Text("OK", "text/plain");
        }).DisableAntiforgery();

        // Some firmwares probe this; keep them happy.
        group.MapGet("/iclock/ping", () => Results.Text("OK", "text/plain"));
    }
}
