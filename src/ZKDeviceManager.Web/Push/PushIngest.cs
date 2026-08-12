using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Push;

/// <summary>
/// Parses and stores data that ZK terminals push over the ADMS/iClock HTTP protocol
/// (USERINFO, BIODATA including FACE templates, ATTLOG). No COM SDK involved, so it is
/// immune to the GetUserFaceStr spin bug on modern Windows.
/// </summary>
public class PushIngest
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<PushIngest> _log;

    public PushIngest(IDbContextFactory<AppDbContext> dbf, ILogger<PushIngest> log)
    {
        _dbf = dbf;
        _log = log;
    }

    // ZK BioData "Type" values (biometric kind).
    private const int BioFinger = 1;
    private const int BioFace = 2;

    public async Task<PushDevice> TouchDeviceAsync(string sn, string? ip, string? info = null)
    {
        await using var db = _dbf.CreateDbContext();
        var pd = await db.PushDevices.FirstOrDefaultAsync(x => x.SerialNumber == sn);
        if (pd == null)
        {
            pd = new PushDevice { SerialNumber = sn, FirstSeenUtc = DateTime.UtcNow };
            db.PushDevices.Add(pd);
        }
        pd.LastIp = ip ?? pd.LastIp;
        pd.LastSeenUtc = DateTime.UtcNow;
        if (info != null) pd.Info = info.Length > 200 ? info[..200] : info;
        await db.SaveChangesAsync();
        return pd;
    }

    /// <summary>Ensure a Device row exists for this serial (so pushed users have a home).</summary>
    private static async Task<Device> EnsureDeviceAsync(AppDbContext db, string sn, string? ip)
    {
        var d = await db.Devices.FirstOrDefaultAsync(x => x.SerialNumber == sn);
        if (d == null && ip != null)
            d = await db.Devices.FirstOrDefaultAsync(x => x.IpAddress == ip);
        if (d == null)
        {
            d = new Device
            {
                Name = $"PUSH {sn}",
                IpAddress = ip ?? sn,
                Port = 4370,
                SerialNumber = sn,
                CreatedUtc = DateTime.UtcNow
            };
            db.Devices.Add(d);
            await db.SaveChangesAsync();
        }
        else if (string.IsNullOrEmpty(d.SerialNumber))
        {
            d.SerialNumber = sn;
        }
        return d;
    }

    /// <summary>Handle a POST /iclock/cdata body for the given table. Returns record count.</summary>
    public async Task<int> IngestAsync(string sn, string? ip, string? table, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return 0;
        await using var db = _dbf.CreateDbContext();
        var device = await EnsureDeviceAsync(db, sn, ip);
        var pd = await db.PushDevices.FirstOrDefaultAsync(x => x.SerialNumber == sn);

        var lines = body.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int count = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Records are usually "TAG key=val\tkey=val" or just "key=val\t...".
            var tag = FirstToken(line);
            var kv = ParseKv(line);

            if (tag.Equals("FACE", StringComparison.OrdinalIgnoreCase) || tag.Equals("BIOPHOTO", StringComparison.OrdinalIgnoreCase))
            {
                // Face template record: FACE PIN=.. FID=.. SIZE=.. VALID=.. TMP=<base64>
                if (await UpsertFaceAsync(db, device, kv))
                {
                    if (pd != null) pd.FacesReceived++;
                    count++;
                }
            }
            else if (tag.Equals("FP", StringComparison.OrdinalIgnoreCase) || tag.Equals("FINGERTMP", StringComparison.OrdinalIgnoreCase))
            {
                if (await UpsertFingerAsync(db, device, kv))
                {
                    if (pd != null) pd.FingersReceived++;
                    count++;
                }
            }
            else if (line.StartsWith("USER ", StringComparison.OrdinalIgnoreCase) || (kv.ContainsKey("PIN") && kv.ContainsKey("Name")))
            {
                await UpsertUserAsync(db, device, kv);
                if (pd != null) pd.UsersReceived++;
                count++;
            }
            else if (tag.Equals("BIODATA", StringComparison.OrdinalIgnoreCase) || (kv.ContainsKey("Tmp") && kv.ContainsKey("Type")))
            {
                if (await UpsertBioAsync(db, device, kv) is int t)
                {
                    if (pd != null) { if (t == BioFace) pd.FacesReceived++; else if (t == BioFinger) pd.FingersReceived++; }
                    count++;
                }
            }
            else if ((table?.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase) ?? false) || tag.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase))
            {
                if (await AddAttlogAsync(db, device, line))
                {
                    if (pd != null) pd.LogsReceived++;
                    count++;
                }
            }
            // OPERLOG and others are ignored for now.
        }

        await db.SaveChangesAsync();
        _log.LogInformation("PUSH ingest SN={SN} table={Table} records={Count}", sn, table, count);
        return count;
    }

    private static async Task UpsertUserAsync(AppDbContext db, Device device, Dictionary<string, string> kv)
    {
        var pin = kv.GetValueOrDefault("PIN") ?? kv.GetValueOrDefault("Pin");
        if (string.IsNullOrWhiteSpace(pin)) return;
        var u = await db.EnrolledUsers.FirstOrDefaultAsync(x => x.SourceDeviceId == device.Id && x.DeviceUserId == pin);
        if (u == null)
        {
            u = new EnrolledUser { SourceDeviceId = device.Id, DeviceUserId = pin };
            db.EnrolledUsers.Add(u);
        }
        u.Name = kv.GetValueOrDefault("Name") ?? u.Name;
        if (int.TryParse(kv.GetValueOrDefault("Pri"), out var pri)) u.Privilege = pri;
        u.Password = string.IsNullOrEmpty(kv.GetValueOrDefault("Passwd")) ? u.Password : kv["Passwd"];
        u.CardNumber = string.IsNullOrEmpty(kv.GetValueOrDefault("Card")) ? u.CardNumber : kv["Card"];
        u.Enabled = kv.GetValueOrDefault("Enabled") != "0";
        u.LastDownloadedUtc = DateTime.UtcNow;
    }

    private static async Task<EnrolledUser> GetOrCreateUserAsync(AppDbContext db, Device device, string pin)
    {
        var u = await db.EnrolledUsers.Include(x => x.Templates)
                   .FirstOrDefaultAsync(x => x.SourceDeviceId == device.Id && x.DeviceUserId == pin);
        if (u == null)
        {
            u = new EnrolledUser { SourceDeviceId = device.Id, DeviceUserId = pin, LastDownloadedUtc = DateTime.UtcNow };
            db.EnrolledUsers.Add(u);
            await db.SaveChangesAsync();
        }
        return u;
    }

    private static async Task<bool> UpsertFaceAsync(AppDbContext db, Device device, Dictionary<string, string> kv)
    {
        var pin = kv.GetValueOrDefault("PIN");
        var tmp = kv.GetValueOrDefault("TMP");
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(tmp)) return false;
        int.TryParse(kv.GetValueOrDefault("FID"), out var fid);
        int.TryParse(kv.GetValueOrDefault("SIZE"), out var size);
        var valid = kv.GetValueOrDefault("VALID") == "0" ? 0 : 1;

        var u = await GetOrCreateUserAsync(db, device, pin);
        var t = u.Templates.FirstOrDefault(x => x.Type == TemplateType.Face && x.FingerIndex == fid);
        if (t == null)
            u.Templates.Add(new BiometricTemplate { Type = TemplateType.Face, FingerIndex = fid, Flag = valid, Data = tmp, Length = size > 0 ? size : tmp.Length, DownloadedUtc = DateTime.UtcNow });
        else { ArchiveIfChanged(db, device, pin, t, tmp); t.Data = tmp; t.Length = size > 0 ? size : tmp.Length; t.Flag = valid; t.DownloadedUtc = DateTime.UtcNow; }
        return true;
    }

    private static async Task<bool> UpsertFingerAsync(AppDbContext db, Device device, Dictionary<string, string> kv)
    {
        var pin = kv.GetValueOrDefault("PIN");
        var tmp = kv.GetValueOrDefault("TMP");
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(tmp)) return false;
        int.TryParse(kv.GetValueOrDefault("FID"), out var fid);
        int.TryParse(kv.GetValueOrDefault("SIZE"), out var size);
        var valid = kv.GetValueOrDefault("VALID") == "0" ? 0 : 1;

        var u = await GetOrCreateUserAsync(db, device, pin);
        var t = u.Templates.FirstOrDefault(x => x.Type == TemplateType.Fingerprint && x.FingerIndex == fid);
        if (t == null)
            u.Templates.Add(new BiometricTemplate { Type = TemplateType.Fingerprint, FingerIndex = fid, Flag = valid, Data = tmp, Length = size > 0 ? size : tmp.Length, DownloadedUtc = DateTime.UtcNow });
        else { ArchiveIfChanged(db, device, pin, t, tmp); t.Data = tmp; t.Length = size > 0 ? size : tmp.Length; t.Flag = valid; t.DownloadedUtc = DateTime.UtcNow; }
        return true;
    }

    private static async Task<int?> UpsertBioAsync(AppDbContext db, Device device, Dictionary<string, string> kv)
    {
        var pin = kv.GetValueOrDefault("Pin") ?? kv.GetValueOrDefault("PIN");
        var tmp = kv.GetValueOrDefault("Tmp");
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(tmp)) return null;
        int.TryParse(kv.GetValueOrDefault("Type"), out var type);
        int.TryParse(kv.GetValueOrDefault("No") ?? kv.GetValueOrDefault("Index"), out var index);

        var u = await db.EnrolledUsers.Include(x => x.Templates)
                   .FirstOrDefaultAsync(x => x.SourceDeviceId == device.Id && x.DeviceUserId == pin);
        if (u == null)
        {
            u = new EnrolledUser { SourceDeviceId = device.Id, DeviceUserId = pin, LastDownloadedUtc = DateTime.UtcNow };
            db.EnrolledUsers.Add(u);
            await db.SaveChangesAsync();
        }

        var tt = type == BioFace ? TemplateType.Face : TemplateType.Fingerprint;
        var fi = tt == TemplateType.Face ? 50 : index;
        var existing = u.Templates.FirstOrDefault(x => x.Type == tt && x.FingerIndex == fi);
        if (existing == null)
        {
            u.Templates.Add(new BiometricTemplate
            { Type = tt, FingerIndex = fi, Flag = 1, Data = tmp, Length = tmp.Length, DownloadedUtc = DateTime.UtcNow });
        }
        else
        {
            ArchiveIfChanged(db, device, pin, existing, tmp);
            existing.Data = tmp; existing.Length = tmp.Length; existing.DownloadedUtc = DateTime.UtcNow;
        }
        return type;
    }

    /// <summary>Archive the previous template payload when an ingested one genuinely differs (re-enrollment).</summary>
    private static void ArchiveIfChanged(AppDbContext db, Device device, string pin, BiometricTemplate existing, string newData)
    {
        if (existing.Data == newData) return;
        db.TemplateChanges.Add(new TemplateChange
        {
            DeviceId = device.Id,
            Pin = pin,
            Type = existing.Type,
            FingerIndex = existing.FingerIndex,
            OldData = existing.Data,
            OldLength = existing.Length,
            ChangedUtc = DateTime.UtcNow
        });
    }

    private static async Task<bool> AddAttlogAsync(AppDbContext db, Device device, string line)
    {
        // ATTLOG is tab-separated positional: PIN \t Time \t Status \t Verify \t WorkCode ...
        var body = line.StartsWith("ATTLOG", StringComparison.OrdinalIgnoreCase) ? line.Substring(6).Trim() : line;
        var parts = body.Split('\t');
        if (parts.Length < 2) return false;
        var pin = parts[0].Trim();
        if (!DateTime.TryParse(parts[1].Trim(), out var ts)) return false;
        int.TryParse(parts.ElementAtOrDefault(3)?.Trim(), out var verify);
        int.TryParse(parts.ElementAtOrDefault(2)?.Trim(), out var status);

        bool exists = await db.AttendanceLogs.AnyAsync(l => l.DeviceId == device.Id && l.DeviceUserId == pin && l.Timestamp == ts);
        if (exists) return false;
        db.AttendanceLogs.Add(new AttendanceLog
        {
            DeviceId = device.Id, DeviceName = device.Name, DeviceIp = device.IpAddress,
            DeviceUserId = pin, Timestamp = ts, VerifyMode = verify, InOutMode = status
        });
        return true;
    }

    private static string FirstToken(string line)
    {
        int i = line.IndexOfAny(new[] { ' ', '\t' });
        return i < 0 ? line : line[..i];
    }

    private static Dictionary<string, string> ParseKv(string line)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // strip a leading tag like "USER " / "BIODATA "
        var body = line;
        var sp = line.IndexOf(' ');
        if (sp > 0 && !line[..sp].Contains('=')) body = line[(sp + 1)..];
        foreach (var part in body.Split('\t', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..];
            d[k] = v;
        }
        return d;
    }
}
