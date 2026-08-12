using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using ZKDeviceManager.Devices.Jobs;
using ZKDeviceManager.Devices.Sta;

namespace ZKDeviceManager.Devices;

public static class DependencyInjection
{
    /// <summary>Register the ZK device SDK wrapper, STA executor, job queue and background runner.</summary>
    /// <param name="useSimulator">When true, use the in-memory <see cref="FakeZkDeviceService"/>
    /// (no hardware / SDK needed) instead of the real zkemkeeper COM implementation.</param>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddZkDevices(this IServiceCollection services, bool useSimulator = false,
                                                  ZkDeviceOptions? options = null)
    {
        services.AddSingleton(options ?? new ZkDeviceOptions());
        services.AddSingleton<StaExecutor>();
        if (useSimulator)
            services.AddSingleton<IZkDeviceService, FakeZkDeviceService>();
        else
            services.AddSingleton<IZkDeviceService, ZkDeviceService>();
        services.AddSingleton<IJobQueue, JobQueue>();
        services.AddSingleton<IJobNotifier, JobNotifier>();
        services.AddSingleton<JobCancellation>();
        services.AddScoped<JobService>();
        services.AddHostedService<DeviceJobRunner>();
        return services;
    }
}
