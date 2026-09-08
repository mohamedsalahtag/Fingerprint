using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// Seeds the <see cref="ExpectedDevice"/> roster from the master site sheet the first time the app runs
/// (only when the table is empty — after that it's fully user-editable on the Commissioning page).
/// IPs are transcribed from the sheet image; rows with an uncertain/duplicate address are flagged
/// VerifyIp = true. KH-CS-IN uses its real IP (192.168.8.81), which differs from the sheet.
/// </summary>
public static class CommissioningSeed
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.ExpectedDevices.AnyAsync()) return;
        db.ExpectedDevices.AddRange(Roster.Select(r => new ExpectedDevice
        {
            Name = r.Name, Ip = r.Ip, Site = r.Site, VerifyIp = r.Verify
        }));
        await db.SaveChangesAsync();
    }

    private record R(string Name, string Ip, string Site, bool Verify = false);

    private static readonly R[] Roster =
    {
        // Jeddah HQ
        new("JD-HO-IN-1", "10.65.70.68", "Jeddah HQ"),
        new("JD-HO-IN-2", "10.65.70.74", "Jeddah HQ"),
        new("JD-HO-OUT", "10.65.70.80", "Jeddah HQ"),
        new("JD-HO-OUT-2", "10.65.70.89", "Jeddah HQ"),
        new("JD-PK-IN-1", "10.65.70.69", "Jeddah HQ"),
        new("JD-PK-IN-2", "10.65.70.70", "Jeddah HQ"),
        new("JD-PK-OUT-1", "10.65.70.82", "Jeddah HQ"),
        new("JD-PK-OUT-2", "10.65.70.83", "Jeddah HQ"),
        new("JD-HORECA-IN / Quality Control", "10.65.70.60", "Jeddah HQ"),
        new("JD-HORECA-OUT", "10.65.70.84", "Jeddah HQ"),
        new("JD-WS-IN", "10.65.70.67", "Jeddah HQ"),
        new("JD-WS-OUT", "10.65.70.87", "Jeddah HQ"),
        new("JD-TECH-OFIC-IN", "10.65.70.65", "Jeddah HQ"),
        new("JD-TECH-OFIC-OUT", "10.65.70.86", "Jeddah HQ"),
        new("Sales-office-IN", "10.65.70.66", "Jeddah HQ"),
        new("Sales-Office-Out", "10.65.70.88", "Jeddah HQ"),
        new("ATCL-Office-IN", "10.65.70.63", "Jeddah HQ"),
        new("ATCL-Office-OUT", "10.65.70.91", "Jeddah HQ"),
        new("ATCL_Yard", "10.65.70.54", "Jeddah HQ"),
        new("Packing Line", "10.65.70.64", "Jeddah HQ"),
        new("PackinLineJDH-1", "192.168.3.62", "Jeddah HQ"),
        new("PackingLineJDH-2", "192.168.3.72", "Jeddah HQ"),
        new("CS-JD-IN", "192.168.3.66", "Jeddah HQ"),
        new("CS-JD-OUT", "10.65.70.81", "Jeddah HQ"),
        new("Fdd-JD-IN", "10.65.70.55", "Jeddah HQ"),
        new("FDD-JD-OUT", "10.65.70.85", "Jeddah HQ"),
        new("QC-JD-IN", "10.65.70.96", "Jeddah HQ"),
        new("QC-JD-OUT", "10.65.70.95", "Jeddah HQ"),
        new("BackPassage-JD-IN", "10.65.70.79", "Jeddah HQ"),
        new("BackPassage-JD-OUT", "10.65.70.90", "Jeddah HQ"),
        new("Banana Coridore-2", "10.65.70.52", "Jeddah HQ"),
        new("Logistic Office", "192.168.3.63", "Jeddah HQ"),
        new("Jeddah-WM", "10.65.70.71", "Jeddah HQ"),
        new("CCTV control Room-JE", "10.65.70.72", "Jeddah HQ"),
        new("Halgah (Jeddah)", "192.168.82.50", "Jeddah HQ"),
        // Dammam
        new("DM-HO-IN", "192.168.6.90", "Dammam"),
        new("DM-HO-OUT", "192.168.7.81", "Dammam"),
        new("DM-FDD-IN", "192.168.6.87", "Dammam"),
        new("DM-FDD-OUT", "192.168.7.82", "Dammam"),
        new("DM-RecieveArea-IN", "192.168.7.87", "Dammam", true),
        new("DM-RecieveArea-OUT", "192.168.7.85", "Dammam"),
        new("DM-PK-IN", "192.168.6.82", "Dammam"),
        new("DM-PK-OUT", "192.168.7.83", "Dammam"),
        new("DM-LadyOffice-IN", "192.168.6.89", "Dammam"),
        new("DM-LadyOffice-OUT", "192.168.7.84", "Dammam"),
        new("DM-Maintenance-IN", "192.168.7.86", "Dammam"),
        new("DM-Maintenance-OUT", "192.168.7.88", "Dammam"),
        new("Dammam-11", "192.168.6.91", "Dammam"),
        // Riyadh
        new("RD-HO-IN", "192.168.24.32", "Riyadh"),
        new("RD-HO-OUT", "192.168.25.37", "Riyadh"),
        new("RD-CS-IN", "192.168.24.30", "Riyadh"),
        new("RD-CS-IN-2", "192.168.24.36", "Riyadh", true),
        new("RD-CS-OUT", "192.168.25.34", "Riyadh"),
        new("RD-CS-OUT-2", "192.168.25.35", "Riyadh", true),
        new("RD-PK-IN(1)", "192.168.24.30", "Riyadh", true),
        new("RD-PK-IN(2)", "192.168.24.31", "Riyadh"),
        new("RD-PK-OUT(1)", "192.168.25.36", "Riyadh"),
        new("RD-PK-OUT(2)", "192.168.25.35", "Riyadh", true),
        new("RD-WSHP-IN", "192.168.24.37", "Riyadh"),
        new("RD-WSHP-OUT", "192.168.24.33", "Riyadh"),
        new("RD-AZIZYH1-IN", "192.168.80.82", "Riyadh"),
        new("RD-AZIZYH1-OUT", "192.168.80.82", "Riyadh", true),
        new("RD-AZIZYH2-IN", "192.168.81.81", "Riyadh"),
        new("RD-AZIZYH2-OUT", "192.168.81.81", "Riyadh", true),
        new("Riyadh Azizyah", "192.168.84.51", "Riyadh"),
        // Buraidah
        new("Buraidh-Office-IN", "192.168.14.80", "Buraidah"),
        new("Buraidh-Office-OUT", "192.168.14.85", "Buraidah"),
        new("Buraidh-CS-IN", "192.168.14.81", "Buraidah"),
        new("Buraidh-CS-OUT", "192.168.14.83", "Buraidah"),
        // Tabuk
        new("TABOUK-CS-IN", "192.168.20.30", "Tabuk"),
        new("Tabuk-CS-OUT", "192.168.20.31", "Tabuk"),
        new("Tabuk Halagh-IN", "192.168.18.80", "Tabuk"),
        new("Tabuk Halgah-OUT", "192.168.18.81", "Tabuk"),
        // Al-Hasa
        new("Al-Hasa-IN", "192.168.22.81", "Al-Hasa"),
        new("Al-Hasa-OUT", "192.168.22.80", "Al-Hasa"),
        // Makkah
        new("Makkah Branch-In", "192.168.10.80", "Makkah"),
        new("Makkah Branch-OUT", "192.168.10.81", "Makkah"),
        // Madina
        new("Madina Branch-IN", "192.168.12.82", "Madina"),
        new("Madinah Coldstore-IN", "192.168.12.81", "Madina"),
        // Khamis
        new("KH-Office-IN", "10.65.8.85", "Khamis"),
        new("KH-Office-OUT", "192.168.8.84", "Khamis"),
        new("KH-CS-IN", "192.168.8.81", "Khamis"),
        new("KH-CS-OUT", "192.168.8.83", "Khamis"),
        // Bahrain
        new("Bahrain Coldstore", "192.168.54.30", "Bahrain"),
        new("Bahrain-Coldstore-IN", "192.168.54.32", "Bahrain"),
        new("Bahrain-Coldstore-OUT", "192.168.54.34", "Bahrain"),
        new("AskerOffice-BH-IN", "192.168.54.31", "Bahrain"),
        new("AskerOffice-BH-OUT", "192.168.54.33", "Bahrain"),
        new("Bahrain Halgah", "192.168.52.20", "Bahrain"),
        new("Halgah-Bahrain-IN", "192.168.52.21", "Bahrain"),
        new("Halgah-Bahrain-OUT", "192.168.52.22", "Bahrain"),
    };
}
