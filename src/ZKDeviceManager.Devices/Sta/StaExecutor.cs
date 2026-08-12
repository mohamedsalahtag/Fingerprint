using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace ZKDeviceManager.Devices.Sta;

/// <summary>
/// Runs all zkemkeeper COM work on one dedicated STA thread. The SDK COM object uses the
/// apartment (STA) threading model, so every call must originate from the same STA thread;
/// this also naturally serializes device access so two operations never race one terminal.
///
/// Some SDK calls can hang indefinitely on certain firmware (e.g. GetUserFaceStr / ReadAllTemplate
/// on newer face terminals). A hung native call can't be aborted, so <see cref="RunAsync{T}"/>
/// supports a timeout: if the work doesn't finish in time, the stuck worker thread is abandoned
/// and a fresh STA thread takes over, keeping the app responsive.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StaExecutor : IDisposable
{
    private readonly object _gate = new();
    private BlockingCollection<Action> _queue = new();
    private Thread _thread;
    private readonly ILogger<StaExecutor> _log;
    private bool _disposed;

    public StaExecutor(ILogger<StaExecutor> log)
    {
        _log = log;
        _thread = StartThread(_queue);
    }

    private Thread StartThread(BlockingCollection<Action> queue)
    {
        var t = new Thread(() => Loop(queue))
        {
            IsBackground = true,
            Name = "ZK-STA-Worker"
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t;
    }

    private void Loop(BlockingCollection<Action> queue)
    {
        try
        {
            foreach (var work in queue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { _log.LogError(ex, "Unhandled error on STA worker"); }
            }
        }
        catch (ObjectDisposedException) { /* queue replaced/disposed */ }
        catch (InvalidOperationException) { /* CompleteAdding on replaced queue */ }
    }

    /// <summary>
    /// Queue work on the STA thread. If <paramref name="timeout"/> elapses first, the worker is
    /// presumed hung: it is abandoned, a fresh STA thread is started, and the task faults with
    /// <see cref="TimeoutException"/>.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<T> func, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StaExecutor));
            _queue.Add(() =>
            {
                if (tcs.Task.IsCompleted) return;
                try { tcs.TrySetResult(func()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
        }

        if (timeout is null)
            return await tcs.Task.ConfigureAwait(false);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout.Value, ct)).ConfigureAwait(false);
        if (completed == tcs.Task)
            return await tcs.Task.ConfigureAwait(false);

        // Timed out: the worker thread is stuck in a native call. Abandon it and recover.
        RecycleWorker();
        throw new TimeoutException($"Device operation exceeded {timeout.Value.TotalSeconds:F0}s and was abandoned.");
    }

    public Task RunAsync(Action action, CancellationToken ct = default, TimeSpan? timeout = null)
        => RunAsync<object?>(() => { action(); return null; }, ct, timeout);

    /// <summary>Abandon the (presumed hung) worker thread and start a fresh one.</summary>
    private void RecycleWorker()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _log.LogWarning("STA worker hung; abandoning it and starting a fresh worker.");
            var old = _queue;
            _queue = new BlockingCollection<Action>();
            _thread = StartThread(_queue);
            try { old.CompleteAdding(); } catch { /* the stuck thread still holds it */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _queue.CompleteAdding(); } catch { }
        }
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
