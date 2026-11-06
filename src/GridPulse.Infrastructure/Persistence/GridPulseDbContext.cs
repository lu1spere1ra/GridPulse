using GridPulse.Core.Alerts;
using GridPulse.Core.Meters;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.Infrastructure.Persistence;

public sealed class GridPulseDbContext(DbContextOptions<GridPulseDbContext> options) : DbContext(options)
{
    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<ReadingRow> Readings => Set<ReadingRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meter>(meter =>
        {
            meter.Property(m => m.Serial).HasMaxLength(32);
            meter.HasIndex(m => m.Serial).IsUnique();
            meter.Property(m => m.Name).HasMaxLength(128);
            meter.Property(m => m.Zone).HasMaxLength(64);
            meter.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<AlertRule>(rule =>
        {
            rule.Property(r => r.Name).HasMaxLength(128);
            rule.Property(r => r.Type).HasConversion<string>().HasMaxLength(16);
            rule.Property(r => r.Aggregate).HasConversion<string>().HasMaxLength(16);
            rule.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            rule.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alert>(alert =>
        {
            alert.Property(a => a.State).HasConversion<string>().HasMaxLength(16);
            alert.Property(a => a.Message).HasMaxLength(512);
            alert.HasOne<AlertRule>().WithMany().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.Cascade);
            alert.HasOne<Meter>().WithMany().HasForeignKey(a => a.MeterId).OnDelete(DeleteBehavior.Cascade);

            // At most one open alert per (rule, meter); also the lookup used when resolving.
            alert.HasIndex(a => new { a.RuleId, a.MeterId })
                .IsUnique()
                .HasFilter("state = 'Firing'");
            alert.HasIndex(a => a.RaisedAt).IsDescending();
        });

        modelBuilder.Entity<ReadingRow>(reading =>
        {
            reading.ToTable("readings");

            // The primary key must contain the hypertable's partitioning column (time).
            // It also makes inserts idempotent: ON CONFLICT DO NOTHING drops QoS 1 redeliveries.
            reading.HasKey(r => new { r.MeterId, r.Time });
        });
    }
}
