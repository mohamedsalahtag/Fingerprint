using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Devices.Model;

namespace ZKDeviceManager.Devices;

/// <summary>
/// A simulator implementation of <see cref="IZkDeviceService"/> for demoing / validating the app
/// without a physical terminal (e.g. on a host that isn't on the device network). Generates
/// synthetic users + templates and simulates progress. Enabled via config "Devices:UseSimulator".
/// </summary>
public class FakeZkDeviceService : IZkDeviceService
{
    // Deterministic-ish generator seeded from the device IP so each device looks stable.
    private static int SeedFor(string ip) => Math.Abs(ip.GetHashCode()) % 90 + 10; // 10..99 users

    public bool TestConnection(DeviceConnection conn, out string? error)
    {
        error = null;
        return true;
    }

    public DeviceInfo ReadDeviceInfo(DeviceConnection conn)
    {
        var n = SeedFor(conn.Ip);
        return new DeviceInfo
        {
            SerialNumber = $"SIM{Math.Abs(conn.Ip.GetHashCode()) % 1000000:D6}",
            FirmwareVersion = "Ver 6.60 (simulator)",
            Platform = "ZMM220_TFT",
            ProductCode = "iFace-SIM",
            SdkVersion = "SIM 1.0",
            AdminCount = 1,
            UserCount = n,
            FingerprintCount = n * 2,
            FaceCount = n / 2,
            RecordCount = n * 37,
            UserCapacity = 10000,
            FingerprintCapacity = 8000,
            FaceCapacity = 3000
        };
    }

    public void Control(DeviceConnection conn, DeviceControlAction action) { /* no-op */ }

    public DateTime? SyncTime(DeviceConnection conn, DateTime now) => now;
}
