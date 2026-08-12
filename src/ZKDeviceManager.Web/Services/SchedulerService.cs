using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Devices.Jobs;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// Runs enabled <see cref="Data.Entities.ScheduledTask"/>s once per day at their configured local time.
/// Checks every minute; a task fires when the clock has passed its time and it hasn't already run today.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerService> _log;

    public SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();

        var tasks = await db.ScheduledTasks.Where(t => t.Enabled).ToListAsync(ct);
        var now = DateTime.Now;

        foreach (var t in tasks)
        {
            var lastLocal = t.LastRunUtc?.ToLocalTime();
            bool alreadyToday = lastLocal is not null && lastLocal.Value.Date >= now.Date;
            if (now.TimeOfDay < t.TimeOfDay.ToTimeSpan() || alreadyToday) continue;

            _log.LogInformation("Scheduler running {Task}", t.Type);
            string result;
            try { result = await AutomationTasks.RunAsync(t.Type, db, jobs); }
            catch (Exception ex) { result = "Failed: " + ex.Message; _log.LogError(ex, "Scheduled {Task} failed", t.Type); }

            t.LastRunUtc = DateTime.UtcNow;
            t.LastResult = result.Length > 500 ? result[..500] : result;
            await db.SaveChangesAsync(ct);
        }
    }
}
