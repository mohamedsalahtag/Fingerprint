using System.Collections.Concurrent;
using ZKDeviceManager.Devices.Model;

namespace ZKDeviceManager.Devices.Jobs;

/// <summary>
/// Broadcasts job progress/status changes to interested UI components (Blazor pages
/// subscribe and re-render) and holds the latest in-memory progress for running jobs so
/// the UI can show a smooth bar without a database write per user. A lightweight
/// in-process alternative to a SignalR hub.
/// </summary>
public interface IJobNotifier
{
    /// <summary>Raised frequently with live per-item progress (no DB reload needed).</summary>
    event Action<int>? ProgressReported;

    /// <summary>Raised on a status transition (queued → running → succeeded/failed); reload from DB.</summary>
    event Action<int>? JobChanged;

    /// <summary>Publish live progress for a running job.</summary>
    void Report(int jobId, JobProgress progress);

    /// <summary>Signal a status transition.</summary>
    void NotifyChanged(int jobId);

    /// <summary>Latest live progress for a running job, if any.</summary>
    bool TryGetLive(int jobId, out JobProgress progress);
}

public sealed class JobNotifier : IJobNotifier
{
    private readonly ConcurrentDictionary<int, JobProgress> _live = new();

    public event Action<int>? ProgressReported;
    public event Action<int>? JobChanged;

    public void Report(int jobId, JobProgress progress)
    {
        _live[jobId] = progress;
        ProgressReported?.Invoke(jobId);
    }

    public void NotifyChanged(int jobId)
    {
        _live.TryRemove(jobId, out _);
        JobChanged?.Invoke(jobId);
    }

    public bool TryGetLive(int jobId, out JobProgress progress)
        => _live.TryGetValue(jobId, out progress!);
}
