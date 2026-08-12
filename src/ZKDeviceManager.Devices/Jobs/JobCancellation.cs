using System.Collections.Concurrent;

namespace ZKDeviceManager.Devices.Jobs;

/// <summary>
/// Registry of cancellation sources for currently-running jobs, so the UI can cancel a job that
/// the background runner is executing. (Queued jobs are cancelled by updating their DB status.)
/// </summary>
public sealed class JobCancellation
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    public void Register(int jobId, CancellationTokenSource cts) => _running[jobId] = cts;

    public void Unregister(int jobId)
    {
        if (_running.TryRemove(jobId, out var cts)) cts.Dispose();
    }

    /// <summary>Request cancellation of a running job. Returns true if the job was running.</summary>
    public bool Cancel(int jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            try { cts.Cancel(); } catch { }
            return true;
        }
        return false;
    }

    public bool IsRunning(int jobId) => _running.ContainsKey(jobId);
}
