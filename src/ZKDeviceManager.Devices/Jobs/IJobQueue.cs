using System.Threading.Channels;

namespace ZKDeviceManager.Devices.Jobs;

/// <summary>
/// In-memory signal that a persisted <c>SyncJob</c> row is ready to run. The job body and
/// audit trail live in the database; this channel just wakes the background runner promptly.
/// </summary>
public interface IJobQueue
{
    /// <summary>Signal that job <paramref name="jobId"/> has been queued.</summary>
    ValueTask EnqueueAsync(int jobId, CancellationToken ct = default);

    ChannelReader<int> Reader { get; }
}

public sealed class JobQueue : IJobQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(int jobId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(jobId, ct);

    public ChannelReader<int> Reader => _channel.Reader;
}
