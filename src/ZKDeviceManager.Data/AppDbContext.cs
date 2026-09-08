using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EnrolledUser> EnrolledUsers => Set<EnrolledUser>();
    public DbSet<BiometricTemplate> BiometricTemplates => Set<BiometricTemplate>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<PushCommand> PushCommands => Set<PushCommand>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<TemplateChange> TemplateChanges => Set<TemplateChange>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppAccessUser> AppAccessUsers => Set<AppAccessUser>();
    public DbSet<ExpectedDevice> ExpectedDevices => Set<ExpectedDevice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Device>(e =>
        {
            e.HasIndex(x => new { x.IpAddress, x.Port }).IsUnique();
        });

        b.Entity<EnrolledUser>(e =>
        {
            e.HasIndex(x => new { x.SourceDeviceId, x.DeviceUserId }).IsUnique();
            e.HasOne(x => x.SourceDevice)
             .WithMany(d => d.Users)
             .HasForeignKey(x => x.SourceDeviceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BiometricTemplate>(e =>
        {
            e.Property(x => x.Data).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.EnrolledUserId, x.Type, x.FingerIndex });
            e.HasOne(x => x.EnrolledUser)
             .WithMany(u => u.Templates)
             .HasForeignKey(x => x.EnrolledUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AttendanceLog>(e =>
        {
            e.HasIndex(x => new { x.DeviceId, x.DeviceUserId, x.Timestamp }).IsUnique();
            e.HasOne(x => x.Device)
             .WithMany()
             .HasForeignKey(x => x.DeviceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserProfile>(e => e.HasIndex(x => x.Pin).IsUnique());

        b.Entity<TemplateChange>(e =>
        {
            e.Property(x => x.OldData).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.DeviceId, x.Pin });
            e.HasIndex(x => x.ChangedUtc);
        });

        b.Entity<ScheduledTask>(e => e.HasIndex(x => x.Type).IsUnique());
        b.Entity<AuditLog>(e => e.HasIndex(x => x.WhenUtc));
        b.Entity<AppAccessUser>(e => e.HasIndex(x => x.Login).IsUnique());
        b.Entity<ExpectedDevice>(e => { e.HasIndex(x => x.Ip); e.HasIndex(x => x.Site); });

        b.Entity<Department>(e => e.HasIndex(x => x.Name).IsUnique());
        b.Entity<Holiday>(e => e.HasIndex(x => x.Date).IsUnique());
        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.Pin).IsUnique();
            e.HasOne(x => x.Department).WithMany(d => d.Employees)
             .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Shift).WithMany(s => s.Employees)
             .HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PushDevice>(e => e.HasIndex(x => x.SerialNumber).IsUnique());
        b.Entity<PushCommand>(e =>
        {
            e.HasIndex(x => new { x.SerialNumber, x.CompletedUtc });
            e.Property(x => x.CommandText).HasColumnType("nvarchar(max)");
        });

        b.Entity<SyncJob>(e =>
        {
            e.HasOne(x => x.Device)
             .WithMany()
             .HasForeignKey(x => x.DeviceId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetDevice)
             .WithMany()
             .HasForeignKey(x => x.TargetDeviceId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
