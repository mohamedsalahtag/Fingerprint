namespace ZKDeviceManager.Devices;

/// <summary>Tunables for device downloads (bound from config section "Devices").</summary>
public class ZkDeviceOptions
{
    /// <summary>
    /// Attempt to download face templates via GetUserFaceStr. Default false: on many newer
    /// visible-light face terminals (e.g. firmware 6.60) this call never returns and would hang
    /// the download. Enable only for older IR iFace devices known to support it.
    /// </summary>
    public bool DownloadFaces { get; set; } = false;

    /// <summary>Highest finger index to probe per user (0-9). Lowering it speeds up downloads.</summary>
    public int MaxFingerIndex { get; set; } = 9;
}
