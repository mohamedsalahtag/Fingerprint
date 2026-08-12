using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using zkemkeeper;
using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Devices.Model;

namespace ZKDeviceManager.Devices;

/// <summary>
/// zkemkeeper COM SDK implementation. Mirrors the proven call sequences from the legacy
/// OMSManager forms (frmUserInfo / frmBackupandRestore) and the IFace_x64 UserInfo sample.
/// MUST be invoked on the shared STA thread (see <see cref="Sta.StaExecutor"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public class ZkDeviceService : IZkDeviceService
{
    private const int MachineNumber = 1;   // ignored for TCP, but required by the API

    private readonly ILogger<ZkDeviceService> _log;
    private readonly ZkDeviceOptions _options;
    public ZkDeviceService(ILogger<ZkDeviceService> log, ZkDeviceOptions options)
    {
        _log = log;
        _options = options;
    }

    private static IZKEM Open(DeviceConnection conn)
    {
        int lastCode = 0;
        // Terminals intermittently refuse a connection (SDK error -6) when momentarily busy;
        // retry a few times before giving up.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            IZKEM zk = new CZKEMClass();
            if (conn.CommKey != 0)
                zk.SetCommPassword(conn.CommKey);
            if (zk.Connect_Net(conn.Ip, conn.Port))
                return zk;
            zk.GetLastError(ref lastCode);
            if (attempt < 3) Thread.Sleep(800);
        }
        throw new ZkDeviceException($"Cannot connect to {conn.Ip}:{conn.Port} after 3 attempts", lastCode);
    }

    public bool TestConnection(DeviceConnection conn, out string? error)
    {
        error = null;
        IZKEM? zk = null;
        try
        {
            zk = Open(conn);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { zk?.Disconnect(); } catch { /* ignore */ }
        }
    }

    public DeviceInfo ReadDeviceInfo(DeviceConnection conn)
    {
        var zk = Open(conn);
        try
        {
            var info = new DeviceInfo();
            string s = "";
            if (zk.GetSerialNumber(MachineNumber, out var sn)) info.SerialNumber = sn;
            if (zk.GetFirmwareVersion(MachineNumber, ref s)) info.FirmwareVersion = s;
            s = ""; if (zk.GetPlatform(MachineNumber, ref s)) info.Platform = s;
            if (zk.GetProductCode(MachineNumber, out var pc)) info.ProductCode = pc;
            s = ""; if (zk.GetSDKVersion(ref s)) info.SdkVersion = s;
            try { s = ""; if (zk.GetDeviceMAC(MachineNumber, ref s)) info.Mac = s; } catch { }

            info.AdminCount = Status(zk, 1);
            info.UserCount = Status(zk, 2);
            info.FingerprintCount = Status(zk, 3);
            info.RecordCount = Status(zk, 6);
            info.FingerprintCapacity = Status(zk, 7);
            info.UserCapacity = Status(zk, 8);
            info.FaceCount = Status(zk, 21);
            info.FaceCapacity = Status(zk, 22);
            return info;
        }
        finally { try { zk.Disconnect(); } catch { } }
    }

    private static int Status(IZKEM zk, int code)
    {
        int v = 0;
        return zk.GetDeviceStatus(MachineNumber, code, ref v) ? v : 0;
    }

    public void Control(DeviceConnection conn, DeviceControlAction action)
    {
        var zk = Open(conn);
        try
        {
            bool ok = action switch
            {
                DeviceControlAction.EnableUi => zk.EnableDevice(MachineNumber, true),
                DeviceControlAction.DisableUi => zk.EnableDevice(MachineNumber, false),
                DeviceControlAction.Restart => zk.RestartDevice(MachineNumber),
                DeviceControlAction.PowerOff => zk.PowerOffDevice(MachineNumber),
                DeviceControlAction.ClearAllUsers => zk.ClearData(MachineNumber, 5),
                _ => false
            };
            if (!ok)
            {
                int code = 0;
                zk.GetLastError(ref code);
                throw new ZkDeviceException($"Control action {action} failed", code);
            }
            if (action == DeviceControlAction.ClearAllUsers)
                zk.RefreshData(MachineNumber);
        }
        finally { try { zk.Disconnect(); } catch { } }
    }

    public DateTime? SyncTime(DeviceConnection conn, DateTime now)
    {
        var zk = Open(conn);
        try
        {
            // The write only sticks on many firmwares when the device is disabled first.
            zk.EnableDevice(MachineNumber, false);
            try
            {
                bool ok = zk.SetDeviceTime2(MachineNumber, now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
                if (!ok)
                {
                    // Fall back to the PC-clock variant (some models ignore SetDeviceTime2).
                    ok = zk.SetDeviceTime(MachineNumber);
                }
                if (!ok)
                {
                    int code = 0;
                    zk.GetLastError(ref code);
                    throw new ZkDeviceException("SetDeviceTime2/SetDeviceTime failed", code);
                }
                zk.RefreshData(MachineNumber);
            }
            finally { try { zk.EnableDevice(MachineNumber, true); } catch { } }

            // Read the clock back so the caller can confirm it actually applied.
            int y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0;
            if (zk.GetDeviceTime(MachineNumber, ref y, ref mo, ref d, ref h, ref mi, ref s))
            {
                try { return new DateTime(y, mo, d, h, mi, s); } catch { return null; }
            }
            return null;
        }
        finally { try { zk.Disconnect(); } catch { } }
    }

}
