using ZKDeviceManager.Devices.Model;

namespace ZKDeviceManager.Devices;

/// <summary>
/// Control-plane operations over a ZK terminal via the zkemkeeper COM SDK. Every method opens its
/// own connection, performs the work, and disconnects. Implementations are NOT thread-safe against
/// the same COM apartment — callers must funnel all invocations through the single STA executor
/// (see <c>StaExecutor</c>).
///
/// NOTE: bulk data operations (download/copy of users, fingerprints and faces) are deliberately NOT
/// here — they run over the PUSH/ADMS channel, because the SDK's face read/write calls hang forever
/// on modern Windows. This interface is now control-only.
/// </summary>
public interface IZkDeviceService
{
    /// <summary>Attempt a connection; returns false with a reason instead of throwing.</summary>
    bool TestConnection(DeviceConnection conn, out string? error);

    /// <summary>Read serial / firmware / capacities and counts from the device.</summary>
    DeviceInfo ReadDeviceInfo(DeviceConnection conn);

    /// <summary>Enable/disable UI, restart, power off, or clear users.</summary>
    void Control(DeviceConnection conn, DeviceControlAction action);

    /// <summary>Write the device clock, then read it back. Returns the device's clock after the write
    /// (null if the device didn't report it) so callers can confirm the change actually applied.</summary>
    DateTime? SyncTime(DeviceConnection conn, DateTime now);
}
